using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using XIVToolHub.Core.Models;

namespace XIVToolHub.Core.GitHub;

public sealed record ReleaseAsset(long Id, string Name, long Size, string ApiUrl);

public sealed record ReleaseInfo(string TagName, string? Name, DateTimeOffset? PublishedAt, bool Prerelease, IReadOnlyList<ReleaseAsset> Assets);

/// <summary>
/// GitHub API からリリース情報とアセットを取得する。Private リポジトリはトークンが必要。
/// </summary>
public sealed class GitHubReleaseClient
{
    public const string ApiBaseUrl = "https://api.github.com/";

    /// <summary>1つのアセットとして許容する上限。JRE 同梱のアプリでも収まり、誤ったファイルは止められる値。</summary>
    public const long MaxAssetBytes = 1024L * 1024 * 1024;

    private const int ReleasesPerPage = 20;
    private const int MaxRedirects = 5;
    private const int CopyBufferSize = 81920;

    private readonly HttpClient _http;
    private readonly Func<string?> _tokenProvider;

    public GitHubReleaseClient(HttpClient http, Func<string?> tokenProvider)
    {
        _http = http;
        _tokenProvider = tokenProvider;
    }

    public static HttpClient CreateHttpClient()
    {
        // 転送は SendAsync で自分で辿る。自動に任せるとトークンが外れ、改名したリポジトリが Private だと 404 になるため
        var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(ApiBaseUrl),
            Timeout = TimeSpan.FromMinutes(30),
        };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("XIVToolHub", "1.0"));
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return http;
    }

    public async Task<ReleaseInfo> GetLatestReleaseAsync(GitHubSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var path = source.IncludePrerelease
            ? $"repos/{source.Owner}/{source.Repo}/releases?per_page={ReleasesPerPage}"
            : $"repos/{source.Owner}/{source.Repo}/releases/latest";

        using var request = CreateRequest(HttpMethod.Get, path, "application/vnd.github+json");
        using var response = await SendAsync(request, source, cancellationToken).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (source.IncludePrerelease)
            {
                var releases = await JsonSerializer.DeserializeAsync<List<ReleaseDto>>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
                var latest = releases?.Where(r => !r.Draft).OrderByDescending(r => r.PublishedAt).FirstOrDefault()
                    ?? throw new HubException($"{source.RepositoryUrl} にリリースがありません。");
                return latest.ToInfo();
            }

            var release = await JsonSerializer.DeserializeAsync<ReleaseDto>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false)
                ?? throw new HubException("リリース情報を読み取れませんでした。");
            return release.ToInfo();
        }
        catch (JsonException ex)
        {
            throw new HubException("GitHub から想定外の応答がありました。", ex);
        }
    }

    public async Task DownloadAssetAsync(GitHubSource source, ReleaseAsset asset, string destinationPath, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(asset);
        if (asset.Size > MaxAssetBytes)
        {
            throw new HubException($"{asset.Name} は大きすぎるためダウンロードしません。");
        }
        // API の URL 以外を指す値は、改ざんされた応答の可能性があるので使わない
        if (!Uri.TryCreate(asset.ApiUrl, UriKind.Absolute, out var uri) || uri.Host != "api.github.com" || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new HubException("ダウンロード先の URL が不正です。");
        }

        // 実ファイルは別ホストへリダイレクトされる。HttpClient はホストが変わると認証ヘッダーを送らない。
        using var request = CreateRequest(HttpMethod.Get, uri.ToString(), "application/octet-stream");
        using var response = await SendAsync(request, source, cancellationToken, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

        var total = response.Content.Headers.ContentLength ?? asset.Size;
        if (total > MaxAssetBytes)
        {
            throw new HubException($"{asset.Name} は大きすぎるためダウンロードしません。");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
        var tempPath = destinationPath + ".part";
        try
        {
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferSize, useAsync: true))
            {
                var buffer = new byte[CopyBufferSize];
                long written = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    written += read;
                    if (written > MaxAssetBytes)
                    {
                        throw new HubException($"{asset.Name} が想定より大きいため中断しました。");
                    }
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    if (total > 0)
                    {
                        progress?.Report(Math.Min(1.0, (double)written / total));
                    }
                }
            }
            File.Move(tempPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    /// <summary>
    /// アセット名のパターン（* と ?）に一致するものを選ぶ。複数あれば名前順で最初のもの。
    /// </summary>
    public static ReleaseAsset? SelectAsset(ReleaseInfo release, string pattern)
    {
        ArgumentNullException.ThrowIfNull(release);
        return release.Assets
            .Where(a => MatchesPattern(a.Name, pattern))
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    public static bool MatchesPattern(string name, string pattern)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(pattern);
        return MatchesAt(name, 0, pattern, 0);
    }

    private static bool MatchesAt(string name, int ni, string pattern, int pi)
    {
        while (pi < pattern.Length)
        {
            var p = pattern[pi];
            if (p == '*')
            {
                for (var skip = ni; skip <= name.Length; skip++)
                {
                    if (MatchesAt(name, skip, pattern, pi + 1))
                    {
                        return true;
                    }
                }
                return false;
            }
            if (ni >= name.Length || (p != '?' && char.ToLowerInvariant(p) != char.ToLowerInvariant(name[ni])))
            {
                return false;
            }
            ni++;
            pi++;
        }
        return ni == name.Length;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, string accept)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        var token = _tokenProvider();
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        }
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, GitHubSource source, CancellationToken cancellationToken, HttpCompletionOption option = HttpCompletionOption.ResponseContentRead)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, option, cancellationToken).ConfigureAwait(false);
            var current = request;
            for (var hop = 0; IsRedirect(response.StatusCode) && response.Headers.Location is { } location; hop++)
            {
                response.Dispose();
                if (hop >= MaxRedirects)
                {
                    throw new HubException("GitHub からの転送が多すぎます。");
                }
                var from = current.RequestUri!.IsAbsoluteUri ? current.RequestUri : new Uri(_http.BaseAddress!, current.RequestUri);
                var next = Redirected(current, new Uri(from, location));
                // 呼び出し元の要求は呼び出し元が破棄する
                if (!ReferenceEquals(current, request))
                {
                    current.Dispose();
                }
                current = next;
                response = await _http.SendAsync(current, option, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (HttpRequestException ex)
        {
            throw new HubException("GitHub に接続できませんでした。ネットワークを確認してください。", ex);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        var status = response.StatusCode;
        response.Dispose();
        throw status switch
        {
            // Private リポジトリはトークンがないと 404 になる
            HttpStatusCode.NotFound => new HubException(
                $"{source.RepositoryUrl} のリリースが見つかりません。リポジトリ名、リリースの有無、Private の場合は GitHub トークンを確認してください。"),
            HttpStatusCode.Unauthorized => new HubException("GitHub トークンが無効です。設定から登録し直してください。"),
            HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests => new HubException(
                "GitHub の利用制限に達したか、アクセス権がありません。しばらく待つか、GitHub トークンを登録してください。"),
            _ => new HubException($"GitHub からエラーが返されました（{(int)status}）。"),
        };
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    /// <summary>
    /// 転送先への要求を作る。改名したリポジトリは同じ API への転送なのでトークンを付け直し、
    /// アセットの実体（GitHub の外のストレージ）にはトークンを渡さない。
    /// </summary>
    private static HttpRequestMessage Redirected(HttpRequestMessage original, Uri target)
    {
        if (target.Scheme != Uri.UriSchemeHttps)
        {
            throw new HubException("GitHub から安全でない転送先が返されました。");
        }
        var next = new HttpRequestMessage(HttpMethod.Get, target);
        foreach (var accept in original.Headers.Accept)
        {
            next.Headers.Accept.Add(accept);
        }
        if (original.Headers.Authorization is { } auth && string.Equals(target.Host, new Uri(ApiBaseUrl).Host, StringComparison.OrdinalIgnoreCase))
        {
            next.Headers.Authorization = auth;
        }
        return next;
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private sealed class ReleaseDto
    {
        public string TagName { get; set; } = "";

        public string? Name { get; set; }

        public bool Draft { get; set; }

        public bool Prerelease { get; set; }

        public DateTimeOffset? PublishedAt { get; set; }

        public List<AssetDto> Assets { get; set; } = [];

        public ReleaseInfo ToInfo()
        {
            if (string.IsNullOrWhiteSpace(TagName))
            {
                throw new HubException("リリースのタグ名がありません。");
            }
            return new ReleaseInfo(TagName, Name, PublishedAt, Prerelease,
                [.. Assets.Where(a => !string.IsNullOrEmpty(a.Name) && !string.IsNullOrEmpty(a.Url))
                    .Select(a => new ReleaseAsset(a.Id, a.Name, a.Size, a.Url))]);
        }
    }

    private sealed class AssetDto
    {
        public long Id { get; set; }

        public string Name { get; set; } = "";

        public long Size { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; } = "";
    }
}
