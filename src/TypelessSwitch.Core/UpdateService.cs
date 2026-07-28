using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using TypelessSwitch.Core.Models;

namespace TypelessSwitch.Core;

public sealed class UpdateService
{
    public static readonly Uri LatestReleaseEndpoint = new(
        "https://api.github.com/repos/StatXzy7/typeless-switch/releases/latest");
    public static readonly Uri LatestReleasePageEndpoint = new(
        "https://github.com/StatXzy7/typeless-switch/releases/latest");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly Uri _latestReleaseEndpoint;
    private readonly Uri _latestReleasePageEndpoint;

    public UpdateService(HttpClient httpClient, Uri? latestReleaseEndpoint = null, Uri? latestReleasePageEndpoint = null)
    {
        _httpClient = httpClient;
        _latestReleaseEndpoint = latestReleaseEndpoint ?? LatestReleaseEndpoint;
        _latestReleasePageEndpoint = latestReleasePageEndpoint ?? LatestReleasePageEndpoint;
    }

    public async Task<AppUpdateInfo?> CheckLatestAsync(
        Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await CheckLatestFromApiAsync(currentVersion, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidDataException)
        {
            // The unauthenticated GitHub API is rate-limited. The public release page
            // remains available and is paired with a signed-by-hash sidecar asset.
            return await CheckLatestFromReleasePageAsync(currentVersion, cancellationToken);
        }
    }

    private async Task<AppUpdateInfo?> CheckLatestFromApiAsync(
        Version currentVersion,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _latestReleaseEndpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.UserAgent.ParseAdd("Typeless-Switch/0.3.2");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"读取更新信息失败（HTTP {(int)response.StatusCode}）。", null, response.StatusCode);

        var release = JsonSerializer.Deserialize<GitHubRelease>(json, JsonOptions)
            ?? throw new InvalidDataException("GitHub 返回了空的 Release 信息。");
        if (!TryParseVersion(release.TagName, out var releaseVersion) ||
            releaseVersion <= currentVersion)
            return null;

        var asset = release.Assets.FirstOrDefault(candidate =>
            !string.IsNullOrWhiteSpace(candidate.Name) &&
            candidate.Name.StartsWith("TypelessSwitch-", StringComparison.OrdinalIgnoreCase) &&
            candidate.Name.EndsWith("-win-x64-setup.exe", StringComparison.OrdinalIgnoreCase));
        if (asset is null)
            throw new InvalidDataException("最新 Release 没有可用的 Windows x64 安装包。");

        var downloadUri = ParseGitHubUri(asset.BrowserDownloadUrl, "安装包下载地址");
        var releaseUri = ParseGitHubUri(release.HtmlUrl, "Release 地址");
        var sha256 = ParseSha256(asset.Digest);
        if (sha256 is null)
            throw new InvalidDataException("最新安装包没有可验证的 SHA-256 摘要，已停止更新。");

        return new AppUpdateInfo(
            releaseVersion,
            release.TagName!,
            asset.Name!,
            downloadUri,
            asset.Size,
            sha256,
            releaseUri);
    }

    private async Task<AppUpdateInfo?> CheckLatestFromReleasePageAsync(
        Version currentVersion,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _latestReleasePageEndpoint);
        request.Headers.UserAgent.ParseAdd("Typeless-Switch/0.3.2");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"读取 GitHub Release 页面失败（HTTP {(int)response.StatusCode}）。", null, response.StatusCode);

        var tagName = ExtractReleaseTag(response.RequestMessage?.RequestUri, html)
            ?? throw new InvalidDataException("无法从 GitHub Release 页面识别版本号。");
        if (!TryParseVersion(tagName, out var releaseVersion) ||
            releaseVersion <= currentVersion)
            return null;

        var assetName = $"TypelessSwitch-{releaseVersion.ToString(3)}-win-x64-setup.exe";
        var downloadUri = ParseGitHubUri(
            $"https://github.com/StatXzy7/typeless-switch/releases/download/{Uri.EscapeDataString(tagName)}/{assetName}",
            "安装包下载地址");
        var releaseUri = ParseGitHubUri(
            $"https://github.com/StatXzy7/typeless-switch/releases/tag/{Uri.EscapeDataString(tagName)}",
            "Release 地址");
        var sha256 = await ReadDigestAsync(new Uri(downloadUri + ".sha256"), cancellationToken);
        return new AppUpdateInfo(releaseVersion, tagName, assetName, downloadUri, -1, sha256, releaseUri);
    }

    private async Task<string> ReadDigestAsync(Uri digestUri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, digestUri);
        request.Headers.UserAgent.ParseAdd("Typeless-Switch/0.3.2");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"读取安装包 SHA-256 文件失败（HTTP {(int)response.StatusCode}）。", null, response.StatusCode);
        return ParseSha256Text(text)
            ?? throw new InvalidDataException("安装包 SHA-256 文件格式不正确。");
    }

    private static string? ExtractReleaseTag(Uri? finalRequestUri, string html)
    {
        var candidate = finalRequestUri?.AbsolutePath is { } path
            ? Regex.Match(path, @"/releases/tag/(v?[0-9][A-Za-z0-9._-]*)", RegexOptions.IgnoreCase).Groups[1].Value
            : string.Empty;
        if (!string.IsNullOrWhiteSpace(candidate)) return candidate;
        var match = Regex.Match(html, @"/releases/tag/(v?[0-9][A-Za-z0-9._-]*)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    public async Task<string> DownloadInstallerAsync(
        AppUpdateInfo update,
        string outputDirectory,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(update.AssetName) ||
            !string.Equals(Path.GetFileName(update.AssetName), update.AssetName, StringComparison.Ordinal) ||
            update.AssetName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("更新安装包文件名不安全，已停止下载。");

        Directory.CreateDirectory(outputDirectory);
        var installerPath = Path.Combine(outputDirectory, update.AssetName);
        var temporaryPath = installerPath + $".{Guid.NewGuid():N}.download";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, update.DownloadUri);
            request.Headers.UserAgent.ParseAdd("Typeless-Switch/0.3.2");
            using var response = await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"下载更新失败（HTTP {(int)response.StatusCode}）。", null, response.StatusCode);

            var total = response.Content.Headers.ContentLength ?? update.Size;
            long downloaded = 0;
            string actualSha256;
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            {
                await using var output = new FileStream(
                    temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[64 * 1024];
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    hash.AppendData(buffer, 0, read);
                    downloaded += read;
                    progress?.Report(new UpdateDownloadProgress(downloaded, total));
                }

                await output.FlushAsync(cancellationToken);
                actualSha256 = Convert.ToHexString(hash.GetHashAndReset());
            }
            if (!string.Equals(actualSha256, update.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("下载的安装包 SHA-256 校验失败，文件未被使用。");

            File.Move(temporaryPath, installerPath, overwrite: true);
            return installerPath;
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static Uri ParseGitHubUri(string? value, string description)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"GitHub 返回的{description}不安全。");
        return uri;
    }

    private static string? ParseSha256(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest)) return null;
        var value = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? digest["sha256:".Length..]
            : digest;
        return value.Length == 64 && value.All(Uri.IsHexDigit) ? value : null;
    }

    private static string? ParseSha256Text(string text)
    {
        var match = Regex.Match(text, @"(?<![0-9a-f])([0-9a-f]{64})(?![0-9a-f])", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static bool TryParseVersion(string? tagName, out Version version)
    {
        version = new Version();
        if (string.IsNullOrWhiteSpace(tagName)) return false;
        var value = tagName.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(value, out var parsed) || parsed is null) return false;
        version = parsed;
        return true;
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; init; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; init; }

        [JsonPropertyName("assets")]
        public IReadOnlyList<GitHubAsset> Assets { get; init; } = [];
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; init; }

        [JsonPropertyName("size")]
        public long Size { get; init; }

        [JsonPropertyName("digest")]
        public string? Digest { get; init; }
    }
}
