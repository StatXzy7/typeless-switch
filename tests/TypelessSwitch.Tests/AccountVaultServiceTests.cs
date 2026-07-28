using System.Text;
using TypelessSwitch.Core;
using TypelessSwitch.Core.Models;

namespace TypelessSwitch.Tests;

public sealed class AccountVaultServiceTests
{
    [Fact]
    public async Task SaveLoadAndDelete_ProtectsSessionForCurrentWindowsUser()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), $"typeless-switch-vault-{Guid.NewGuid():N}");
        var paths = new TypelessPaths(
            Path.Combine(root, "roaming"),
            Path.Combine(root, "local"),
            Path.Combine(root, "programs"),
            Path.Combine(root, "documents"));
        var vault = new AccountVaultService(paths);
        var session = new TypelessSession
        {
            Email = "saved@example.com",
            UserId = "saved-user",
            AccessToken = "sensitive-access-token",
            RefreshToken = "sensitive-refresh-token",
            LoginTime = 123
        };

        try
        {
            await vault.SaveAsync(session);

            Assert.True(vault.HasSession(session.UserId));
            var file = Assert.Single(Directory.EnumerateFiles(paths.AccountVaultDirectory));
            var raw = await File.ReadAllBytesAsync(file);
            Assert.DoesNotContain(session.AccessToken, Encoding.UTF8.GetString(raw), StringComparison.Ordinal);
            Assert.DoesNotContain(session.RefreshToken, Encoding.UTF8.GetString(raw), StringComparison.Ordinal);

            var restored = await vault.LoadAsync(session.UserId);
            Assert.Equal(session, restored);

            await vault.DeleteAsync(session.UserId);
            Assert.False(vault.HasSession(session.UserId));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AccountRegistry_Delete_RemovesOnlySelectedUser()
    {
        var root = Path.Combine(Path.GetTempPath(), $"typeless-switch-registry-{Guid.NewGuid():N}");
        var paths = new TypelessPaths(localAppData: Path.Combine(root, "local"));
        var registry = new AccountRegistryService(paths);

        try
        {
            await registry.SaveAsync(new AccountRecord("first@example.com", "first", DateTimeOffset.UtcNow));
            await registry.SaveAsync(new AccountRecord("second@example.com", "second", DateTimeOffset.UtcNow));
            await registry.DeleteAsync("first");

            var accounts = await registry.LoadAsync();
            var remaining = Assert.Single(accounts);
            Assert.Equal("second", remaining.UserId);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
