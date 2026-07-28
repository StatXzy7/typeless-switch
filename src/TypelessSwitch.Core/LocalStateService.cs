using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TypelessSwitch.Core;

public sealed class LocalStateService
{
    private const string BackupDirectoryPrefix = "typeless-switch-backup-";

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

    public async Task<bool> StopTypelessAsync(CancellationToken cancellationToken = default)
    {
        var processes = Process.GetProcessesByName("Typeless");
        var wasRunning = processes.Length > 0;
        foreach (var process in processes)
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

        return wasRunning;
    }

    public Task<string> BackupAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var backupRoot = Path.Combine(Path.GetTempPath(), $"{BackupDirectoryPrefix}{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(backupRoot);
        try
        {
            if (Directory.Exists(_paths.UserDataDirectory))
                CopyDirectory(_paths.UserDataDirectory, Path.Combine(backupRoot, "user-data"));
            if (File.Exists(_paths.DeviceCacheFile))
            {
                var destination = Path.Combine(backupRoot, "device.cache");
                File.Copy(_paths.DeviceCacheFile, destination, overwrite: true);
            }
            return Task.FromResult(backupRoot);
        }
        catch
        {
            TryDeleteBackup(backupRoot);
            throw;
        }
    }

    public async Task ClearForLoginAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(_paths.UserDataFile)) File.Delete(_paths.UserDataFile);
        await ClearAppStorageAsync(cancellationToken);
        if (File.Exists(_paths.DeviceCacheFile)) File.Delete(_paths.DeviceCacheFile);

        ClearSessionEntries();

        if (_resetWindowsCredential) TryDeleteCredential();
    }

    public async Task ClearForStoredSessionSwitchAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(_paths.UserDataFile)) File.Delete(_paths.UserDataFile);
        await ClearAppStorageAsync(cancellationToken);
        ClearSessionEntries();
    }

    public Task RestoreAsync(string backupRoot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsOwnedBackupDirectory(backupRoot) || !Directory.Exists(backupRoot))
            throw new InvalidDataException("切换前备份不存在或路径不安全，已停止恢复以保护当前数据。");

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

    public bool TryDeleteBackup(string backupRoot)
    {
        if (!IsOwnedBackupDirectory(backupRoot)) return false;
        try
        {
            if (Directory.Exists(backupRoot)) Directory.Delete(backupRoot, recursive: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
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

    private void ClearSessionEntries()
    {
        foreach (var entry in SessionEntries)
        {
            DeletePath(Path.Combine(_paths.UserDataDirectory, entry));
            DeletePath(Path.Combine(_paths.UserDataDirectory, "Partitions", "no-proxy-session", entry));
        }
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

    private static bool IsOwnedBackupDirectory(string backupRoot)
    {
        if (string.IsNullOrWhiteSpace(backupRoot)) return false;
        try
        {
            var fullPath = Path.GetFullPath(backupRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var temporaryRoot = Path.GetFullPath(Path.GetTempPath())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(Path.GetDirectoryName(fullPath), temporaryRoot, StringComparison.OrdinalIgnoreCase) &&
                   Path.GetFileName(fullPath).StartsWith(BackupDirectoryPrefix, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }
}
