using System.Text;
using TypelessSwitch.Core;
using TypelessSwitch.Core.Models;

namespace TypelessSwitch.Tests;

public sealed class SessionStoreServiceTests
{
    [Fact]
    public void DeriveAppKey_MatchesNodeFixture()
    {
        var actual = Convert.ToHexString(SessionStoreService.DeriveAppKey()).ToLowerInvariant();
        Assert.Equal("174309e269888d746fc6b206ee450f4463f13c2f2b6a4bae19ac52a0f03dae84", actual);
    }

    [Fact]
    public void Decrypt_ReadsNodeConfFixtureWithInvalidUtf8Iv()
    {
        const string expected = "{\"userData\":\"{\\\"email\\\":\\\"fixture@example.com\\\"}\"}";
        var payload = Convert.FromBase64String(
            "gP8Awyh/4aoQIDBAUGBwgDpgT7rVDaf3u+hrFyUVgVUIXL8gt+lq484nzy1kkCdSONuJOb8i3ow3sKgbmTKxsERcTk2HHZEdGhLxdt3gF/Bl");

        var actual = SessionStoreService.Decrypt(payload, SessionStoreService.DeriveAppKey());

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task WriteAndRead_RoundTripsSession()
    {
        var root = Path.Combine(Path.GetTempPath(), $"typeless-switch-test-{Guid.NewGuid():N}");
        try
        {
            var paths = new TypelessPaths(Path.Combine(root, "roaming"), Path.Combine(root, "local"), Path.Combine(root, "programs"));
            var service = new SessionStoreService(paths);
            var expected = new TypelessSession
            {
                Email = "test@example.com",
                AccessToken = "access",
                RefreshToken = "refresh",
                UserId = "user-1",
                LoginTime = 1234
            };

            await service.WriteAsync(expected);
            var actual = await service.ReadAsync();

            Assert.Equal(expected, actual);
            Assert.Equal((byte)':', (await File.ReadAllBytesAsync(paths.UserDataFile))[16]);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DeriveWindowsExecutableAppKey_MatchesObservedTypelessVariant()
    {
        var actual = Convert.ToHexString(SessionStoreService.DeriveAppKey("win32-x64", "Typeless.exe")).ToLowerInvariant();
        Assert.Equal("0965dc377b8d9feed5f3405851f11d7ae582c4bf23c23d7c7c6a685943766b33", actual);
    }
}
