using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TypelessSwitch.Core;

public sealed class LocalStateService
{
    private static readonly string[] SessionEntries =
    [
        ".updaterId", "Cookies", "Cookies-journal", "Local Storage", "Session Storage",
        "SharedStorage", "SharedStorage-wal", "SharedStorage-shm", "Trust Tokens",
        "Trust Tokens-journal", "Network Persistent State", "TransportSecurity",
        "blob_storage", "Cache", "Code Cache", "GPUCache", "DawnGraphiteCache", "DawnWebGPUCache"
    ];

    private readonly TypelessPaths _paths;
    private readonly bool _resetWindowsCredential;
    public LocalStateService(TypelessPaths paths, bool resetWindowsCredential = false)
    {
        _paths = paths;
        _resetWindowsCredential = resetWindowsCredential;
    }

    public async Task StopTypelessAsync(CancellationToken cancellationToken = default)
    {
        foreach (var process in Process.GetProcessesByName("Typeless"))
        {
            using (process)
            {
                try
                {
                    if (!process.CloseMainWindow()) process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(8), cancellationToken);
                }
                catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                }
            }
        }
    }

    public Task<string> BackupAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var backupRoot = Path.Combine(Path.GetTempPath(), $"typeless-switch-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}");
        Directory.CreateDirectory(backupRoot);
        if (Directory.Exists(_paths.UserDataDirectory))
            CopyDirectory(_paths.UserDataDirectory, Path.Combine(backupRoot, "user-data"));
        if (File.Exists(_paths.DeviceCacheFile))
        {
            var destination = Path.Combine(backupRoot, "device.cache");
            File.Copy(_paths.DeviceCacheFile, destination, overwrite: true);
        }
        return Task.FromResult(backupRoot);
    }

    public async Task ClearForLoginAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(_paths.UserDataFile)) File.Delete(_paths.UserDataFile);
        await ClearAppStorageAsync(cancellationToken);
        if (File.Exists(_paths.DeviceCacheFile)) File.Delete(_paths.DeviceCacheFile);

        foreach (var entry in SessionEntries)
        {
            DeletePath(Path.Combine(_paths.UserDataDirectory, entry));
            DeletePath(Path.Combine(_paths.UserDataDirectory, "Partitions", "no-proxy-session", entry));
        }

        if (_resetWindowsCredential) TryDeleteCredential();
    }

    public Task RestoreAsync(string backupRoot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var userDataBackup = Path.Combine(backupRoot, "user-data");
        if (Directory.Exists(_paths.UserDataDirectory)) Directory.Delete(_paths.UserDataDirectory, recursive: true);
        if (Directory.Exists(userDataBackup))
        {
            CopyDirectory(userDataBackup, _paths.UserDataDirectory);
        }
        var deviceBackup = Path.Combine(backupRoot, "device.cache");
        if (File.Exists(_paths.DeviceCacheFile)) File.Delete(_paths.DeviceCacheFile);
        if (File.Exists(deviceBackup))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_paths.DeviceCacheFile)!);
            File.Copy(deviceBackup, _paths.DeviceCacheFile, overwrite: true);
        }
        return Task.CompletedTask;
    }

    public void StartTypeless()
    {
        var executable = _paths.FindTypelessExecutable();
        if (executable is null) return;
        Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
    }

    private async Task ClearAppStorageAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_paths.AppStorageFile)) return;
        try
        {
            var json = await File.ReadAllTextAsync(_paths.AppStorageFile, cancellationToken);
            var root = JsonNode.Parse(json)?.AsObject();
            if (root is null) return;
            foreach (var key in new[] { "userData", "quotaUsage", "currentRoute", "TYPELESS_418_SEND_ERROR_COUNT", "TYPELESS_TIME_DIFF" })
                root.Remove(key);
            await File.WriteAllTextAsync(_paths.AppStorageFile, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        }
        catch (JsonException) { }
    }

    private static void TryDeleteCredential()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("cmdkey.exe", "/delete:Typeless.deviceIdentifier")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            process?.WaitForExit(3_000);
        }
        catch { }
    }

    private static void DeletePath(string path)
    {
        if (File.Exists(path)) File.Delete(path);
        else if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}
