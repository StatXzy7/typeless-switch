using System.Text.Json.Serialization;

namespace TypelessSwitch.Core.Models;

public sealed record TypelessSession
{
    [JsonPropertyName("email")]
    public required string Email { get; init; }

    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    [JsonPropertyName("refresh_token")]
    public required string RefreshToken { get; init; }

    [JsonPropertyName("login_time")]
    public long LoginTime { get; init; }

    [JsonPropertyName("user_id")]
    public required string UserId { get; init; }
}
