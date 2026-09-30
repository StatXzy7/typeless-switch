using System.Text;
using System.Text.Json;
using TypelessSwitch.Core.Models;

namespace TypelessSwitch.Core;

public sealed class DictionaryBackupService
{
    private const int MaximumCsvBytes = 5 * 1024 * 1024;
    private const int MaximumBackupBytes = 32 * 1024 * 1024;

    public Task<DictionaryCsvResult> ConvertToOfficialCsvAsync(
        string inputPath, string outputPath, CancellationToken cancellationToken = default) =>
        Task.Run(() => ConvertCoreAsync(inputPath, outputPath, cancellationToken), cancellationToken);

    private static async Task<DictionaryCsvResult> ConvertCoreAsync(
        string inputPath, string outputPath, CancellationToken cancellationToken = default)
    {
        var sourcePath = Path.GetFullPath(inputPath);
        var destinationPath = Path.GetFullPath(outputPath);
        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(sourcePath, destinationPath, pathComparison))
            throw new InvalidDataException("CSV 输出位置不能覆盖原 JSON 备份。");

        await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);
        if (input.Length > MaximumBackupBytes)
            throw new InvalidDataException("JSON 备份超过 32 MB，请拆分后再转换。");
        using var reader = new StreamReader(input, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
        var json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        if (json.StartsWith('\uFEFF')) json = json[1..];
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("words", out var words) || words.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("请使用包含 words 数组的 Typeless Switch JSON 词典备份。");
        var source = JsonSerializer.Deserialize<DictionaryExport>(json)
            ?? throw new InvalidDataException("无法读取 JSON 词典备份。");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var lines = new List<string>();
        var empty = 0;
        var duplicates = 0;
        var outputBytes = 0;
        foreach (var word in source.Words)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var term = word?.Term?.Trim();
            if (string.IsNullOrWhiteSpace(term)) { empty++; continue; }
            if (term.Contains('\r') || term.Contains('\n'))
                throw new InvalidDataException("备份含有跨行词条，无法转换成官方要求的每行一个词条的 CSV。原备份未修改。");
            if (!seen.Add(term)) { duplicates++; continue; }
            var line = $"\"{term.Replace("\"", "\"\"")}\"";
            outputBytes += Encoding.UTF8.GetByteCount(line) + 1;
            if (outputBytes > MaximumCsvBytes)
                throw new InvalidDataException("转换后的 CSV 超过官方客户端的 5 MB 文件限制，请拆分词典后重试。");
            lines.Add(line);
        }
        if (lines.Count == 0) throw new InvalidDataException("备份中没有可转换的有效词条。");
        // The official desktop importer expects one column, one term per row, without a header.
        var csv = string.Join('\n', lines) + "\n";
        var directory = Path.GetDirectoryName(destinationPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".typeless-csv-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, csv, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
        return new DictionaryCsvResult(lines.Count, duplicates, empty, destinationPath);
    }
}

public sealed record DictionaryCsvResult(int Total, int Duplicates, int EmptyEntries, string CsvPath);
