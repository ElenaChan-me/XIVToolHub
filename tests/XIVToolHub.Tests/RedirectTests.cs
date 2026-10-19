using System.Net;
using System.Text;
using XIVToolHub.Core.GitHub;
using XIVToolHub.Core.Models;

namespace XIVToolHub.Tests;

public class RedirectTests
{
    private static readonly GitHubSource Old = new() { Owner = "owner", Repo = "old-name", AssetPattern = "app-*.zip" };

    [Fact]
    public async Task RenamedPrivateRepositoryKeepsToken()
    {
        var handler = new FakeGitHubHandler();
        // 改名したリポジトリは、GitHub が新しいリポジトリの API へ転送する
        handler.Redirect("/repos/owner/old-name/releases/latest", "https://api.github.com/repositories/42/releases/latest");
        handler.Route("/repositories/42/releases/latest", r => r.Headers.Authorization is null
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(TestData.ReleaseJson("owner", "new-name", "v2", (7, "app-win.zip")), Encoding.UTF8, "application/json"),
            });
        handler.Route("/repos/owner/new-name/releases/assets/7", _ => new HttpResponseMessage(HttpStatusCode.Found)
        {
            Headers = { Location = new Uri("https://objects.example.com/app-win.zip") },
        });
        // アセットの実体の置き場所にはトークンを渡さない
        handler.Route("/app-win.zip", r => r.Headers.Authorization is null
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) }
            : new HttpResponseMessage(HttpStatusCode.BadRequest));
        var client = new GitHubReleaseClient(new HttpClient(handler) { BaseAddress = new Uri(GitHubReleaseClient.ApiBaseUrl) }, () => "token");

        var release = await client.GetLatestReleaseAsync(Old, CancellationToken.None);
        Assert.Equal("v2", release.TagName);

        using var temp = new TempDirectory();
        var path = temp.Combine("app.zip");
        await client.DownloadAssetAsync(Old, release.Assets[0], path, null, CancellationToken.None);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(path));
    }

    [Fact]
    public async Task EndlessRedirectsStop()
    {
        var handler = new FakeGitHubHandler();
        handler.Redirect("/repos/owner/old-name/releases/latest", "/repos/owner/old-name/releases/latest");
        var client = new GitHubReleaseClient(new HttpClient(handler) { BaseAddress = new Uri(GitHubReleaseClient.ApiBaseUrl) }, () => null);
        await Assert.ThrowsAsync<XIVToolHub.Core.HubException>(() => client.GetLatestReleaseAsync(Old, CancellationToken.None));
    }
}
