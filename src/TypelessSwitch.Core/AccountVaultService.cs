using System.Security.Cryptography;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using TypelessSwitch.Core.Models;

namespace TypelessSwitch.Core;

[SupportedOSPlatform("windows")]
public sealed class AccountVaultService
{
    private static readonly byte[] Entropy = SHA256.HashData(
        Encoding.UTF8.GetBytes("TypelessSwitch.AccountVault.v1"));
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TypelessPaths _paths;

    public AccountVaultService(TypelessPaths paths) => _paths = paths;

    public bool HasSession(string userId) => File.Exists(GetSessionPath(userId));

    public async Task SaveAsync(TypelessSession session, CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        ArgumentNullException.ThrowIfNull(session);
        Directory.CreateDirectory(_paths.AccountVaultDirectory);
        var destination = GetSessionPath(session.UserId);
        var temporaryFile = destination + $".{Guid.NewGuid():N}.tmp";
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(session, JsonOptions);
        byte[]? protectedData = null;
        try
        {
            protectedData = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
            await File.WriteAllBytesAsync(temporaryFile, protectedData, cancellationToken);
            File.Move(temporaryFile, destination, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            if (protectedData is not null) CryptographicOperations.ZeroMemory(protectedData);
            if (File.Exists(temporaryFile)) File.Delete(temporaryFile);
        }
    }

    public async Task<TypelessSession?> LoadAsync(string userId, CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        var path = GetSessionPath(userId);
        if (!File.Exists(path)) return null;
        var protectedData = await File.ReadAllBytesAsync(path, cancellationToken);
        byte[]? plaintext = null;
        try
        {
            plaintext = ProtectedData.Unprotect(protectedData, Entropy, DataProtectionScope.CurrentUser);
            var session = JsonSerializer.Deserialize<TypelessSession>(plaintext, JsonOptions)
                ?? throw new InvalidDataException("保存的账号会话为空。");
            if (!string.Equals(session.UserId, userId, StringComparison.Ordinal))
                throw new InvalidDataException("保存的账号会话身份不匹配。");
            return session;
        }
        catch (CryptographicException exception)
        {
            throw new InvalidDataException("无法解密保存的账号会话。该会话只可由保存它的 Windows 用户读取。", exception);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("保存的账号会话格式已损坏。", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedData);
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public Task DeleteAsync(string userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetSessionPath(userId);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string GetSessionPath(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var fileName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId))).ToLowerInvariant();
        return Path.Combine(_paths.AccountVaultDirectory, fileName + ".session");
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("账号会话库需要 Windows DPAPI。");
    }
}
