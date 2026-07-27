using System.Text.Json;
using System.Text.Json.Serialization;

namespace TypelessSwitch.Core;

public sealed record AccountRecord(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("last_used_at")] DateTimeOffset LastUsedAt);

public sealed class AccountRegistryService
{
    private readonly TypelessPaths _paths;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public AccountRegistryService(TypelessPaths paths) => _paths = paths;

    public async Task<IReadOnlyList<AccountRecord>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.AccountsFile)) return [];
        try
        {
            await using var stream = File.OpenRead(_paths.AccountsFile);
            return await JsonSerializer.DeserializeAsync<List<AccountRecord>>(stream, Options, cancellationToken) ?? [];
        }
        catch (JsonException) { return []; }
    }

    public async Task SaveAsync(AccountRecord account, CancellationToken cancellationToken = default)
    {
        var accounts = (await LoadAsync(cancellationToken)).ToList();
        accounts.RemoveAll(item => string.Equals(item.Email, account.Email, StringComparison.OrdinalIgnoreCase));
        accounts.Insert(0, account);
        Directory.CreateDirectory(_paths.AppDataDirectory);
        await using var stream = File.Create(_paths.AccountsFile);
        await JsonSerializer.SerializeAsync(stream, accounts, Options, cancellationToken);
    }
}
