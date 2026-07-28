namespace TypelessSwitch.Core.Models;

public sealed record AppUpdateInfo(
    Version Version,
    string TagName,
    string AssetName,
    Uri DownloadUri,
    long Size,
    string Sha256,
    Uri ReleaseUri);

public sealed record UpdateDownloadProgress(long BytesDownloaded, long TotalBytes);
