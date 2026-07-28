using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TypelessSwitch.Core.Models;

namespace TypelessSwitch.Core;

public sealed class DictionaryService
{
    public static readonly Uri ListEndpoint = new("https://api.typeless.com/user/dictionary/list?size=10000");
    private static readonly Uri BulkEndpoint = new("https://api.typeless.com/user/dictionary/bulk-import");
    private static readonly Uri AddEndpoint = new("https://api.typeless.com/user/dictionary/add");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly HttpClient _httpClient;

    public DictionaryService(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<IReadOnlyList<DictionaryWord>> ListAsync(string authToken, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, ListEndpoint, authToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureAuthenticated(response, json);
        EnsureSuccess(response, json, "读取词典");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("status", out var status) && !string.Equals(status.GetString(), "OK", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Typeless 返回了词典读取失败状态。");
        if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("words", out var words)) return [];
        return JsonSerializer.Deserialize<List<DictionaryWord>>(words.GetRawText(), JsonOptions) ?? [];
    }

    public async Task<DictionaryExportResult> ExportAsync(
        TypelessSession session,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        // Typeless Desktop authenticates API requests with the long-lived refresh token.
        // The access token expires after a few hours and may remain stale on disk.
        var words = await ListAsync(session.RefreshToken, cancellationToken);
        Directory.CreateDirectory(outputDirectory);
        var jsonPath = Path.Combine(outputDirectory, "typeless-dictionary-export.json");
        var textPath = Path.Combine(outputDirectory, "typeless-dictionary-export.txt");
        var csvPath = Path.Combine(outputDirectory, "typeless-dictionary-export.csv");
        var export = new DictionaryExport
        {
            ExportedAt = DateTimeOffset.UtcNow,
            Account = new ExportAccount { Email = session.Email, UserId = session.UserId },
            TotalCount = words.Count,
            Words = words
        };
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(export, JsonOptions), new UTF8Encoding(false), cancellationToken);
        await File.WriteAllLinesAsync(textPath, words.Select(word => word.Term), new UTF8Encoding(false), cancellationToken);
        var csv = new List<string> { "term,lang,category,auto,replace" };
        csv.AddRange(words.Select(word => string.Join(',',
            Csv(word.Term), Csv(word.Language), Csv(word.Category), Csv(word.Auto ? "true" : "false"), Csv(word.Replace ? "true" : "false"))));
        await File.WriteAllLinesAsync(csvPath, csv, new UTF8Encoding(false), cancellationToken);
        return new DictionaryExportResult(words.Count, jsonPath, textPath, csvPath);
    }

    public async Task<DictionaryImportResult> ImportAsync(
        string authToken,
        string inputPath,
        DictionaryImportMode mode,
        int concurrency = 12,
        IProgress<DictionaryProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(inputPath);
        var source = await JsonSerializer.DeserializeAsync<DictionaryExport>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("导入文件不是有效的 Typeless Switch 词典文件。");
        var existing = await ListAsync(authToken, cancellationToken);
        var existingKeys = mode == DictionaryImportMode.Bulk
            ? existing.Select(word => word.Term).ToHashSet(StringComparer.Ordinal)
            : existing.Select(WordKey).ToHashSet(StringComparer.Ordinal);
        var toAdd = source.Words
            .Where(word => !string.IsNullOrWhiteSpace(word.Term))
            .Where(word => !existingKeys.Contains(mode == DictionaryImportMode.Bulk ? word.Term : WordKey(word)))
            .ToArray();
        var skipped = source.Words.Count - toAdd.Length;
        if (toAdd.Length == 0) return new DictionaryImportResult(0, 0, skipped, source.Words.Count);

        var succeeded = 0;
        var failed = 0;
        var completed = 0;
        if (mode == DictionaryImportMode.Bulk)
        {
            var chunks = toAdd.Chunk(200).ToArray();
            await Parallel.ForEachAsync(chunks, new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(concurrency, 1, Math.Min(16, chunks.Length)),
                CancellationToken = cancellationToken
            }, async (chunk, token) =>
            {
                var ok = await PostBulkAsync(authToken, chunk, token);
                if (ok) Interlocked.Add(ref succeeded, chunk.Length); else Interlocked.Add(ref failed, chunk.Length);
                var done = Interlocked.Add(ref completed, chunk.Length);
                progress?.Report(new(done, toAdd.Length, Volatile.Read(ref succeeded), Volatile.Read(ref failed), $"已处理 {done}/{toAdd.Length}"));
            });
        }
        else
        {
            await Parallel.ForEachAsync(toAdd, new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(concurrency, 1, 32),
                CancellationToken = cancellationToken
            }, async (word, token) =>
            {
                var ok = await PostWordAsync(authToken, word, token);
                if (ok) Interlocked.Increment(ref succeeded); else Interlocked.Increment(ref failed);
                var done = Interlocked.Increment(ref completed);
                progress?.Report(new(done, toAdd.Length, Volatile.Read(ref succeeded), Volatile.Read(ref failed), $"已处理 {done}/{toAdd.Length}"));
            });
        }

        return new DictionaryImportResult(succeeded, failed, skipped, source.Words.Count);
    }

    private async Task<bool> PostBulkAsync(string token, DictionaryWord[] words, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, BulkEndpoint, token);
        request.Content = JsonContent.Create(new { content = string.Join('\n', words.Select(word => word.Term)) });
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureAuthenticated(response, content);
        return IsSuccessful(response, content);
    }

    private async Task<bool> PostWordAsync(string token, DictionaryWord word, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, AddEndpoint, token);
        request.Content = JsonContent.Create(new
        {
            term = word.Term,
            lang = word.Language,
            category = word.Category,
            auto = word.Auto,
            replace = word.Replace,
            replace_targets = word.ReplaceTargets
        });
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureAuthenticated(response, content);
        return IsSuccessful(response, content);
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, Uri uri, string token)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.UserAgent.ParseAdd("Typeless-Switch/0.3.1");
        return request;
    }

    private static bool IsSuccessful(HttpResponseMessage response, string content)
    {
        if (!response.IsSuccessStatusCode) return false;
        if (string.IsNullOrWhiteSpace(content)) return true;
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.TryGetProperty("status", out var status) && string.Equals(status.GetString(), "FAIL", StringComparison.OrdinalIgnoreCase)) return false;
            if (root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False) return false;
            return true;
        }
        catch (JsonException) { return true; }
    }

    private static void EnsureSuccess(HttpResponseMessage response, string content, string action)
    {
        if (response.IsSuccessStatusCode) return;
        throw new HttpRequestException(
            $"{action}失败（HTTP {(int)response.StatusCode}）。{Sanitize(content)}", null, response.StatusCode);
    }

    private static void EnsureAuthenticated(HttpResponseMessage response, string content)
    {
        if (response.StatusCode is not (System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)) return;
        throw new HttpRequestException(
            $"Typeless 长期登录凭据无效（HTTP {(int)response.StatusCode}）。", null, response.StatusCode);
    }

    private static string Sanitize(string value) => value.Length <= 200 ? value : value[..200];
    private static string Csv(object? value) => $"\"{Convert.ToString(value)?.Replace("\"", "\"\"")}\"";
    private static string WordKey(DictionaryWord word) => $"{word.Term}||{word.Language ?? string.Empty}";
}
