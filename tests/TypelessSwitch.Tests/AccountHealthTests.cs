using System.Text;
using System.Text.Json;
using TypelessSwitch.Core;
using TypelessSwitch.Core.Models;

namespace TypelessSwitch.Tests;

public sealed class AccountHealthTests
{
    [Fact]
    public void Evaluate_DistinguishesHealthyExpiredAndInvalidSessions()
    {
        var healthy = Session("person@example.com", "user-1", Jwt(DateTimeOffset.UtcNow.AddHours(1)));
        var expired = healthy with { RefreshToken = Jwt(DateTimeOffset.UtcNow.AddMinutes(-5)) };
        var invalid = healthy with { AccessToken = "" };

        Assert.Equal(AccountHealthStatus.Healthy, AccountHealth.Evaluate(healthy));
        Assert.Equal(AccountHealthStatus.NeedsLogin, AccountHealth.Evaluate(expired));
        Assert.Equal(AccountHealthStatus.InvalidSession, AccountHealth.Evaluate(invalid));
    }

    [Fact]
    public void IsExpectedIdentity_RequiresBothUserIdAndEmail()
    {
        var expected = Session("Person@example.com", "user-1", "opaque-refresh");
        Assert.True(AccountHealth.IsExpectedIdentity(
            expected with { Email = "person@example.com" }, expected));
        Assert.False(AccountHealth.IsExpectedIdentity(
            expected with { UserId = "other" }, expected));
        Assert.False(AccountHealth.IsExpectedIdentity(
            expected with { Email = "other@example.com" }, expected));
    }

    [Fact]
    public async Task SessionVerifier_RequiresTwoStableMatchingReads()
    {
        var expected = Session("person@example.com", "user-1", "opaque-refresh");
        var other = expected with { UserId = "other" };
        var queue = new Queue<TypelessSession?>([other, expected, expected]);
        var verifier = new SessionVerificationService(_ =>
            Task.FromResult(queue.Count > 0 ? queue.Dequeue() : expected));

        var result = await verifier.VerifyAsync(
            expected,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(200),
            TimeSpan.FromMilliseconds(1));

        Assert.Equal(expected.UserId, result.UserId);
        Assert.Empty(queue);
    }

    [Fact]
    public async Task SessionVerifier_RejectsPersistentIdentityMismatch()
    {
        var expected = Session("person@example.com", "user-1", "opaque-refresh");
        var verifier = new SessionVerificationService(_ =>
            Task.FromResult<TypelessSession?>(expected with { UserId = "other" }));

        await Assert.ThrowsAsync<InvalidDataException>(() => verifier.VerifyAsync(
            expected,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(15),
            TimeSpan.FromMilliseconds(1)));
    }

    private static TypelessSession Session(string email, string userId, string refreshToken) => new()
    {
        Email = email,
        UserId = userId,
        AccessToken = "access-token",
        RefreshToken = refreshToken,
        LoginTime = 1
    };

    private static string Jwt(DateTimeOffset expiration)
    {
        static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{Encode("{\"alg\":\"none\"}")}.{Encode(JsonSerializer.Serialize(new { exp = expiration.ToUnixTimeSeconds() }))}.signature";
    }
}
