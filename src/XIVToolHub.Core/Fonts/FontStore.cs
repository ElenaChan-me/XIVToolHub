using XIVToolHub.Core.Install;
using XIVToolHub.Core.Storage;

namespace XIVToolHub.Core.Fonts;

/// <summary>
/// 追加フォントの取得と保管。フォントは %LOCALAPPDATA%\XIVToolHub\fonts\フォントID に置く。
/// </summary>
public sealed class FontStore
{
    public const long MaxDownloadBytes = 200L * 1024 * 1024;

    private static readonly string[] FontExtensions = [".ttf", ".otf", ".ttc"];
    private const int CopyBufferSize = 81920;
    private const int MaxFileNameLength = 100;

    private readonly HubPaths _paths;
    private readonly HttpClient _http;

    public FontStore(HubPaths paths, HttpClient http)
    {
        _paths = paths;
        _http = http;
    }

    public string FontsDirectory => Path.Combine(_paths.DataDirectory, "fonts");

    public string FontDirectory(string fontId) => Path.Combine(FontsDirectory, fontId);

    /// <summary>取得済みのフォントファイル。無ければ null。</summary>
    public string? FindFontFile(FontDefinition font)
    {
        ArgumentNullException.ThrowIfNull(font);
        var directory = FontDirectory(font.Id);
        if (!Directory.Exists(directory))
        {
            return null;
        }
        var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(IsFontFileName)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return files.FirstOrDefault(f => font.PreferredFile is not null
                                         && string.Equals(Path.GetFileName(f), font.PreferredFile, StringComparison.OrdinalIgnoreCase))
               ?? files.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Contains("Regular", StringComparison.OrdinalIgnoreCase))
               ?? files.FirstOrDefault();
    }

    /// <summary>フォント ID から取得済みのフォントファイルを探す。未取得・不明な ID なら null。</summary>
    public string? ResolvePath(string? fontId) => FontCatalog.Find(fontId) is { } font ? FindFontFile(font) : null;

    /// <summary>
    /// 公式の配布元からダウンロードする。カタログに登録した URL 以外には接続しない。
    /// </summary>
    public async Task<string> DownloadAsync(FontDefinition font, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(font);
        if (font.DownloadUrl is null || !Uri.TryCreate(font.DownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new HubException($"{font.Name} は自動でダウンロードできません。公式ページからダウンロードして取り込んでください。");
        }

        // 取り込み時はファイル名がそのまま残るので、配布元のファイル名で保存する
        var tempDirectory = Path.Combine(FontsDirectory, $"{font.Id}-{Guid.NewGuid():N}.download");
        var fileName = Path.GetFileName(uri.AbsolutePath);
        if (!IsPlainFileName(fileName))
        {
            fileName = "font.zip";
        }
        var tempPath = Path.Combine(tempDirectory, fileName);
        Directory.CreateDirectory(tempDirectory);
        try
        {
            HttpResponseMessage response;
            try
            {
                response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new HubException($"{font.Name} をダウンロードできませんでした。ネットワークを確認してください。", ex);
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new HubException($"{font.Name} の配布元からエラーが返されました（{(int)response.StatusCode}）。公式ページから取り込んでください。");
                }
                var total = response.Content.Headers.ContentLength;
                if (total > MaxDownloadBytes)
                {
                    throw new HubException($"{font.Name} のファイルが大きすぎます。");
                }

                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferSize, useAsync: true);
                var buffer = new byte[CopyBufferSize];
                long written = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    written += read;
                    if (written > MaxDownloadBytes)
                    {
                        throw new HubException($"{font.Name} のファイルが想定より大きいため中断しました。");
                    }
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    if (total is > 0)
                    {
                        progress?.Report(Math.Min(1.0, (double)written / total.Value));
                    }
                }
            }

            return Import(font, tempPath);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    /// <summary>
    /// 利用者がダウンロードしたフォント（.ttf / .otf / .ttc、またはそれを含む .zip）を取り込む。
    /// </summary>
    public string Import(FontDefinition font, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(font);
        if (!File.Exists(sourcePath))
        {
            throw new HubException("選んだファイルが見つかりません。");
        }
        if (new FileInfo(sourcePath).Length > MaxDownloadBytes)
        {
            throw new HubException("ファイルが大きすぎます。");
        }

        var staging = Path.Combine(FontsDirectory, $"{font.Id}-{Guid.NewGuid():N}.staging");
        try
        {
            Directory.CreateDirectory(staging);
            if (IsZip(sourcePath))
            {
                var extracted = Path.Combine(staging, "zip");
                SafeZipExtractor.Extract(sourcePath, extracted);
                var fonts = Directory.EnumerateFiles(extracted, "*", SearchOption.AllDirectories).Where(IsFontFileName).ToList();
                foreach (var file in fonts.Where(IsFontData))
                {
                    File.Copy(file, Path.Combine(staging, Path.GetFileName(file)), overwrite: true);
                }
                Directory.Delete(extracted, recursive: true);
            }
            else if (IsFontFileName(sourcePath) && IsFontData(sourcePath))
            {
                File.Copy(sourcePath, Path.Combine(staging, Path.GetFileName(sourcePath)));
            }
            else
            {
                throw new HubException("フォントファイル（.ttf / .otf / .ttc）か、それを含む ZIP を選んでください。");
            }

            if (!Directory.EnumerateFiles(staging).Any())
            {
                throw new HubException("ZIP の中にフォントファイルが見つかりません。");
            }

            // 前に取り込んだものと入れ替える
            var target = FontDirectory(font.Id);
            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }
            Directory.Move(staging, target);
            return FindFontFile(font) ?? throw new HubException("フォントファイルが見つかりません。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new HubException($"フォントを取り込めませんでした。{ex.Message}", ex);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    public void Remove(FontDefinition font)
    {
        ArgumentNullException.ThrowIfNull(font);
        var directory = FontDirectory(font.Id);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static bool IsPlainFileName(string name) =>
        name.Length is > 0 and <= MaxFileNameLength && name is not ("." or "..") && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static bool IsFontFileName(string path) =>
        FontExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static bool IsZip(string path) => StartsWith(path, "PK\u0003\u0004"u8);

    /// <summary>拡張子だけでなく、先頭のバイトでフォントの形式かを確かめる。</summary>
    private static bool IsFontData(string path) =>
        StartsWith(path, [0x00, 0x01, 0x00, 0x00]) || StartsWith(path, "OTTO"u8) || StartsWith(path, "true"u8) || StartsWith(path, "ttcf"u8);

    private static bool StartsWith(string path, ReadOnlySpan<byte> signature)
    {
        Span<byte> head = stackalloc byte[4];
        using var stream = File.OpenRead(path);
        return stream.Read(head) == signature.Length && head.SequenceEqual(signature);
    }
}
