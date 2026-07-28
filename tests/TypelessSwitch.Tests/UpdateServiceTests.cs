using System.Net;
using System.Security.Cryptography;
using System.Text;
using TypelessSwitch.Core;
using TypelessSwitch.Core.Models;

namespace TypelessSwitch.Tests;

public sealed class UpdateServiceTests
{
    [Fact]
    public async Task CheckLatest_ReturnsNewWindowsReleaseWithDigest()
    {
        var payload = Encoding.UTF8.GetBytes("installer");
        var digest = Convert.ToHexString(SHA256.HashData(payload));
        var handler = new UpdateHandler($$"""
            {
              "tag_name": "v0.2.0",
              "html_url": "https://github.com/StatXzy7/typeless-switch/releases/tag/v0.2.0",
              "assets": [
                {
                  "name": "TypelessSwitch-0.2.0-win-x64-setup.exe",
                  "browser_download_url": "https://github.com/StatXzy7/typeless-switch/releases/download/v0.2.0/TypelessSwitch-0.2.0-win-x64-setup.exe",
                  "size": {{payload.Length}},
                  "digest": "sha256:{{digest}}"
                }
              ]
            }
            """, payload);

        var update = await new UpdateService(new HttpClient(handler)).CheckLatestAsync(new Version(0, 1, 2));

        Assert.NotNull(update);
        Assert.Equal(new Version(0, 2, 0), update.Version);
        Assert.Equal(digest, update.Sha256);
        Assert.Equal("TypelessSwitch-0.2.0-win-x64-setup.exe", update.AssetName);
    }

    [Fact]
    public async Task CheckLatest_IgnoresOlderRelease()
    {
        var handler = new UpdateHandler("""
            {
              "tag_name": "v0.1.1",
              "html_url": "https://github.com/StatXzy7/typeless-switch/releases/tag/v0.1.1",
              "assets": []
            }
            """, []);

        var update = await new UpdateService(new HttpClient(handler)).CheckLatestAsync(new Version(0, 1, 2));

        Assert.Null(update);
    }

    [Fact]
    public async Task DownloadInstaller_VerifiesSha256BeforeReplacingFile()
    {
        var payload = Encoding.UTF8.GetBytes("verified installer content");
        var digest = Convert.ToHexString(SHA256.HashData(payload));
        var handler = new UpdateHandler("{}", payload);
        var update = new AppUpdateInfo(
            new Version(0, 2, 0),
            "v0.2.0",
            "TypelessSwitch-0.2.0-win-x64-setup.exe",
            new Uri("https://github.com/StatXzy7/typeless-switch/releases/download/v0.2.0/installer.exe"),
            payload.Length,
            digest,
            new Uri("https://github.com/StatXzy7/typeless-switch/releases/tag/v0.2.0"));
        var root = Path.Combine(Path.GetTempPath(), $"typeless-switch-update-{Guid.NewGuid():N}");

        try
        {
            var path = await new UpdateService(new HttpClient(handler)).DownloadInstallerAsync(update, root);

            Assert.Equal(payload, await File.ReadAllBytesAsync(path));
            Assert.DoesNotContain(Directory.EnumerateFiles(root), file => file.EndsWith(".download", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class UpdateHandler(string releaseJson, byte[] installer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("/releases/latest", StringComparison.Ordinal) == true)
                return Task.FromResult(JsonResponse(releaseJson));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(installer)
            });
        }

        private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
