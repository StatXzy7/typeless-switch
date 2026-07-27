using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TypelessSwitch.Core.Models;

namespace TypelessSwitch.Core;

public sealed class SessionStoreService
{
    private const int Iterations = 10_000;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private readonly TypelessPaths _paths;

    public SessionStoreService(TypelessPaths paths) => _paths = paths;

    public async Task<TypelessSession?> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.UserDataFile)) return null;
        var bytes = await File.ReadAllBytesAsync(_paths.UserDataFile, cancellationToken);

        foreach (var key in CandidateAppKeys())
        {
            try
            {
                var plaintext = Decrypt(bytes, key);
                using var document = JsonDocument.Parse(plaintext);
                if (!document.RootElement.TryGetProperty("userData", out var raw)) continue;
                var userJson = raw.ValueKind == JsonValueKind.String ? raw.GetString() : raw.GetRawText();
                if (string.IsNullOrWhiteSpace(userJson)) continue;
                var session = JsonSerializer.Deserialize<TypelessSession>(userJson, JsonOptions);
                if (!string.IsNullOrWhiteSpace(session?.AccessToken)) return session;
            }
            catch (Exception exception) when (exception is CryptographicException or JsonException or DecoderFallbackException)
            {
                // Try another supported Windows architecture key.
            }
        }

        throw new InvalidDataException("无法解密 Typeless 登录状态。请在 Typeless 中重新登录并完全退出后重试。");
    }

    public async Task WriteAsync(TypelessSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        Directory.CreateDirectory(_paths.UserDataDirectory);
        var inner = JsonSerializer.Serialize(session, JsonOptions);
        var outer = JsonSerializer.Serialize(new Dictionary<string, string> { ["userData"] = inner }, JsonOptions);
        var encrypted = Encrypt(outer, DeriveAppKey(
            "win32-x64", OperatingSystem.IsWindows() ? "Typeless.exe" : "Typeless"));
        var temporaryFile = Path.Combine(_paths.UserDataDirectory, $"user-data.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporaryFile, encrypted, cancellationToken);
            File.Move(temporaryFile, _paths.UserDataFile, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryFile)) File.Delete(temporaryFile);
        }
    }

    public static byte[] DeriveAppKey(string platformArchitecture = "win32-x64", string appName = "Typeless")
    {
        var seed = DeriveSeed(platformArchitecture);
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(seed + appName),
            Encoding.UTF8.GetBytes("typeless-user-service"),
            Iterations,
            HashAlgorithmName.SHA256,
            32);
    }

    public static byte[] Encrypt(string plaintext, byte[] appKey, byte[]? iv = null)
    {
        iv ??= RandomNumberGenerator.GetBytes(16);
        if (iv.Length != 16) throw new ArgumentException("IV must contain 16 bytes.", nameof(iv));
        var cipherKey = DeriveCipherKey(appKey, iv);
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = cipherKey;
        aes.IV = iv;
        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        var output = new byte[17 + ciphertext.Length];
        Buffer.BlockCopy(iv, 0, output, 0, 16);
        output[16] = (byte)':';
        Buffer.BlockCopy(ciphertext, 0, output, 17, ciphertext.Length);
        return output;
    }

    public static string Decrypt(byte[] payload, byte[] appKey)
    {
        if (payload.Length < 33 || payload[16] != (byte)':') throw new CryptographicException("Invalid conf payload.");
        var iv = payload[..16];
        var cipherKey = DeriveCipherKey(appKey, iv);
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = cipherKey;
        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        var plaintext = decryptor.TransformFinalBlock(payload, 17, payload.Length - 17);
        return Encoding.UTF8.GetString(plaintext);
    }

    private static byte[] DeriveCipherKey(byte[] appKey, byte[] iv)
    {
        // conf/electron-store uses Node's Buffer.toString() (UTF-8) as the PBKDF2 salt.
        var saltString = Encoding.UTF8.GetString(iv);
        return Rfc2898DeriveBytes.Pbkdf2(
            appKey,
            Encoding.UTF8.GetBytes(saltString),
            Iterations,
            HashAlgorithmName.SHA512,
            32);
    }

    private static IEnumerable<byte[]> CandidateAppKeys()
    {
        var appNames = new[] { "Typeless.exe", "Typeless", "typeless" };
        var architectures = new[] { "win32-x64", "win32-arm64", "win32-ia32" };
        foreach (var architecture in architectures)
            foreach (var appName in appNames)
            {
                var appKey = DeriveAppKey(architecture, appName);
                yield return appKey;
                yield return Encoding.UTF8.GetBytes(Convert.ToHexString(appKey).ToLowerInvariant());
                yield return Encoding.UTF8.GetBytes(DeriveSeed(architecture) + appName);
            }
    }

    private static string DeriveSeed(string platformArchitecture) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(platformArchitecture))).ToLowerInvariant();
}
