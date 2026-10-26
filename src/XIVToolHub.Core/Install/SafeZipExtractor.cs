using System.IO.Compression;
using XIVToolHub.Core.Catalog;

namespace XIVToolHub.Core.Install;

/// <summary>
/// ダウンロードした ZIP を展開する。ZIP 内のパスは信用せず、展開先の外へ書き出すものや異常に大きいものは拒否する。
/// </summary>
public static class SafeZipExtractor
{
    public const long MaxTotalBytes = 4L * 1024 * 1024 * 1024;
    public const int MaxEntries = 100_000;

    public static void Extract(string zipPath, string destinationDirectory)
    {
        var root = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(root);
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;

        ZipArchive archive;
        try
        {
            // 日本語の Windows で作った ZIP はファイル名が Shift_JIS のことがある。UTF-8 の印がある項目は UTF-8 のまま読まれる
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            archive = ZipFile.Open(zipPath, ZipArchiveMode.Read, System.Text.Encoding.GetEncoding(932));
        }
        catch (InvalidDataException ex)
        {
            throw new HubException("ダウンロードしたファイルが ZIP として読めません。壊れている可能性があります。", ex);
        }

        using (archive)
        {
            if (archive.Entries.Count > MaxEntries)
            {
                throw new HubException("ZIP 内のファイル数が多すぎます。");
            }

            long total = 0;
            foreach (var entry in archive.Entries)
            {
                total += entry.Length;
                if (total > MaxTotalBytes)
                {
                    throw new HubException("展開後のサイズが大きすぎます。");
                }

                var isDirectory = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
                var relative = entry.FullName.TrimEnd('/', '\\');
                if (relative.Length == 0)
                {
                    continue;
                }
                if (!ToolCatalog.IsSafeRelativePath(relative))
                {
                    throw new HubException($"ZIP に不正なパスが含まれています: {entry.FullName}");
                }

                var target = Path.GetFullPath(Path.Combine(root, relative));
                if (!target.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                {
                    throw new HubException($"ZIP に展開先の外を指すパスが含まれています: {entry.FullName}");
                }

                if (isDirectory)
                {
                    Directory.CreateDirectory(target);
                    continue;
                }

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // 同じ名前のファイルが2つある ZIP、空き容量の不足、ウイルス対策ソフトのロックなど
                    throw new HubException($"{entry.FullName} を展開できませんでした。（{ex.Message}）", ex);
                }
            }
        }
    }
}
