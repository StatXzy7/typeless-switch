using System.Text.Json;
using System.Text.Json.Serialization;
using TypelessSwitch.Core.Models;

namespace TypelessSwitch.Core;

[JsonConverter(typeof(JsonStringEnumConverter<AccountHealthStatus>))]
public enum AccountHealthStatus
{
    Unknown,
    Healthy,
    NeedsLogin,
    InvalidSession,
    VerificationFailed
}

public static class AccountHealth
{
    public static AccountHealthStatus Evaluate(TypelessSession? session)
    {
        if (session is null ||
            string.IsNullOrWhiteSpace(session.Email) ||
            string.IsNullOrWhiteSpace(session.UserId) ||
            string.IsNullOrWhiteSpace(session.AccessToken) ||
            string.IsNullOrWhiteSpace(session.RefreshToken))
            return AccountHealthStatus.InvalidSession;

        return IsJwtExpired(session.RefreshToken)
            ? AccountHealthStatus.NeedsLogin
            : AccountHealthStatus.Healthy;
    }

    public static bool IsExpectedIdentity(TypelessSession? actual, TypelessSession expected) =>
        actual is not null &&
        string.Equals(actual.UserId, expected.UserId, StringComparison.Ordinal) &&
        string.Equals(actual.Email.Trim(), expected.Email.Trim(), StringComparison.OrdinalIgnoreCase);

    public static bool IsJwtExpired(string token, DateTimeOffset? now = null)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3) return false;
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
            return document.RootElement.TryGetProperty("exp", out var expiration) &&
                   DateTimeOffset.FromUnixTimeSeconds(expiration.GetInt64()) <=
                   (now ?? DateTimeOffset.UtcNow).AddMinutes(1);
        }
        catch (Exception exception) when (
            exception is FormatException or JsonException or ArgumentOutOfRangeException)
        {
            // Some supported sessions use opaque refresh tokens. Their lifetime cannot be
            // determined locally, so only a real switch verification may change the status.
            return false;
        }
    }
}
