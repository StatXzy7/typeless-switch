using System.Text.Json;
using System.Text.Json.Serialization;

namespace TypelessSwitch.Core.Models;

public sealed record DictionaryWord
{
    [JsonPropertyName("term")]
    public required string Term { get; init; }

    [JsonPropertyName("lang")]
    public string? Language { get; init; }

    [JsonPropertyName("category")]
    public string? Category { get; init; }

    [JsonPropertyName("auto")]
    public bool Auto { get; init; } = true;

    [JsonPropertyName("replace")]
    public bool Replace { get; init; }

    [JsonPropertyName("replace_targets")]
    public JsonElement[] ReplaceTargets { get; init; } = [];
}

public sealed record DictionaryExport
{
    [JsonPropertyName("exported_at")]
    public DateTimeOffset ExportedAt { get; init; }

    [JsonPropertyName("source")]
    public ExportSource Source { get; init; } = new();

    [JsonPropertyName("account")]
    public ExportAccount Account { get; init; } = new();

    [JsonPropertyName("total_count")]
    public int TotalCount { get; init; }

    [JsonPropertyName("words")]
    public IReadOnlyList<DictionaryWord> Words { get; init; } = [];
}

public sealed record ExportSource
{
    [JsonPropertyName("app_name")]
    public string AppName { get; init; } = "Typeless";

    [JsonPropertyName("api_url")]
    public string ApiUrl { get; init; } = DictionaryService.ListEndpoint.ToString();
}

public sealed record ExportAccount
{
    [JsonPropertyName("user_id")]
    public string? UserId { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }
}

public enum DictionaryImportMode
{
    Bulk,
    Full
}

public sealed record DictionaryProgress(int Completed, int Total, int Succeeded, int Failed, string Message);

public sealed record DictionaryImportResult(int Imported, int Failed, int Skipped, int SourceTotal);

public sealed record DictionaryExportResult(int Total, string JsonPath, string TextPath, string CsvPath);
