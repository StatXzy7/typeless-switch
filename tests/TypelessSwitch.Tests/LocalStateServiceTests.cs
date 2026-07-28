using System.Text.Json.Nodes;
using TypelessSwitch.Core;

namespace TypelessSwitch.Tests;

public sealed class LocalStateServiceTests
{
    [Fact]
    public async Task StoredSessionSwitch_ClearsAccountStateButPreservesDeviceCache()
    {
        var root = Path.Combine(Path.GetTempPath(), $"typeless-switch-test-{Guid.NewGuid():N}");
        var paths = new TypelessPaths(Path.Combine(root, "roaming"), Path.Combine(root, "local"), Path.Combine(root, "programs"));
        var service = new LocalStateService(paths);
        Directory.CreateDirectory(paths.UserDataDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.DeviceCacheFile)!);
        Directory.CreateDirectory(Path.Combine(paths.UserDataDirectory, "Local Storage"));
        await File.WriteAllTextAsync(paths.UserDataFile, "old-session");
        await File.WriteAllTextAsync(paths.AppStorageFile, "{\"userData\":\"old\",\"keep\":\"yes\"}");
        await File.WriteAllTextAsync(paths.DeviceCacheFile, "same-device");
        await File.WriteAllTextAsync(Path.Combine(paths.UserDataDirectory, "Local Storage", "cache"), "cached");

        try
        {
            await service.ClearForStoredSessionSwitchAsync();

            Assert.False(File.Exists(paths.UserDataFile));
            Assert.False(Directory.Exists(Path.Combine(paths.UserDataDirectory, "Local Storage")));
            Assert.Equal("same-device", await File.ReadAllTextAsync(paths.DeviceCacheFile));
            var storage = JsonNode.Parse(await File.ReadAllTextAsync(paths.AppStorageFile))!.AsObject();
            Assert.False(storage.ContainsKey("userData"));
            Assert.Equal("yes", storage["keep"]!.GetValue<string>());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BackupClearAndRestore_PreservesOriginalState()
    {
        var root = Path.Combine(Path.GetTempPath(), $"typeless-switch-test-{Guid.NewGuid():N}");
        var paths = new TypelessPaths(Path.Combine(root, "roaming"), Path.Combine(root, "local"), Path.Combine(root, "programs"));
        var service = new LocalStateService(paths, resetWindowsCredential: false);
        Directory.CreateDirectory(paths.UserDataDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.DeviceCacheFile)!);
        Directory.CreateDirectory(Path.Combine(paths.UserDataDirectory, "Local Storage"));
        await File.WriteAllTextAsync(paths.UserDataFile, "original-session");
        await File.WriteAllTextAsync(paths.AppStorageFile, "{\"userData\":\"secret\",\"quotaUsage\":1,\"keep\":\"yes\"}");
        await File.WriteAllTextAsync(paths.DeviceCacheFile, "original-device");
        await File.WriteAllTextAsync(Path.Combine(paths.UserDataDirectory, "Local Storage", "cache"), "cached");

        try
        {
            var backup = await service.BackupAsync();
            await service.ClearForLoginAsync();

            Assert.False(File.Exists(paths.UserDataFile));
            Assert.False(File.Exists(paths.DeviceCacheFile));
            Assert.False(Directory.Exists(Path.Combine(paths.UserDataDirectory, "Local Storage")));
            var storage = JsonNode.Parse(await File.ReadAllTextAsync(paths.AppStorageFile))!.AsObject();
            Assert.False(storage.ContainsKey("userData"));
            Assert.False(storage.ContainsKey("quotaUsage"));
            Assert.Equal("yes", storage["keep"]!.GetValue<string>());

            await File.WriteAllTextAsync(paths.UserDataFile, "new-session");
            Directory.CreateDirectory(Path.GetDirectoryName(paths.DeviceCacheFile)!);
            await File.WriteAllTextAsync(paths.DeviceCacheFile, "new-device");
            await service.RestoreAsync(backup);

            Assert.Equal("original-session", await File.ReadAllTextAsync(paths.UserDataFile));
            Assert.Equal("original-device", await File.ReadAllTextAsync(paths.DeviceCacheFile));
            Assert.True(File.Exists(Path.Combine(paths.UserDataDirectory, "Local Storage", "cache")));
            Assert.True(service.TryDeleteBackup(backup));
            Assert.False(Directory.Exists(backup));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }


    [Fact]
    public void TryDeleteBackup_RejectsDirectoriesOutsideOwnedTemporaryBoundary()
    {
        var root = Path.Combine(Path.GetTempPath(), $"typeless-switch-boundary-{Guid.NewGuid():N}");
        var paths = new TypelessPaths(localAppData: Path.Combine(root, "local"));
        var service = new LocalStateService(paths);
        Directory.CreateDirectory(root);
        try
        {
            Assert.False(service.TryDeleteBackup(root));
            Assert.True(Directory.Exists(root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Restore_RejectsMissingBackupBeforeChangingCurrentState()
    {
        var root = Path.Combine(Path.GetTempPath(), $"typeless-switch-restore-{Guid.NewGuid():N}");
        var paths = new TypelessPaths(Path.Combine(root, "roaming"), Path.Combine(root, "local"));
        var service = new LocalStateService(paths);
        Directory.CreateDirectory(paths.UserDataDirectory);
        await File.WriteAllTextAsync(paths.UserDataFile, "current-session");
        var missingBackup = Path.Combine(
            Path.GetTempPath(), $"typeless-switch-backup-missing-{Guid.NewGuid():N}");

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => service.RestoreAsync(missingBackup));
            Assert.Equal("current-session", await File.ReadAllTextAsync(paths.UserDataFile));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
