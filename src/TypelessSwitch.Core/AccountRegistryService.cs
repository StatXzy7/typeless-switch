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
        accounts.RemoveAll(item =>
            string.Equals(item.UserId, account.UserId, StringComparison.Ordinal) ||
            string.Equals(item.Email, account.Email, StringComparison.OrdinalIgnoreCase));
        accounts.Insert(0, account);
        await WriteAsync(accounts, cancellationToken);
    }

    public async Task DeleteAsync(string userId, CancellationToken cancellationToken = default)
    {
        var accounts = (await LoadAsync(cancellationToken))
            .Where(item => !string.Equals(item.UserId, userId, StringComparison.Ordinal))
            .ToList();
        await WriteAsync(accounts, cancellationToken);
    }

    private async Task WriteAsync(IReadOnlyList<AccountRecord> accounts, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.AppDataDirectory);
        var temporaryFile = _paths.AccountsFile + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporaryFile))
                await JsonSerializer.SerializeAsync(stream, accounts, Options, cancellationToken);
            File.Move(temporaryFile, _paths.AccountsFile, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryFile)) File.Delete(temporaryFile);
        }
    }
}
