using System.Text;
using System.Text.Json;
using TypelessSwitch.Core;
using TypelessSwitch.Core.Models;

namespace TypelessSwitch.Tests;

public sealed class DictionaryBackupServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"typeless-switch-test-{Guid.NewGuid():N}");
    private readonly DictionaryBackupService _service = new();
    private string Input => Path.Combine(_root, "backup.json");
    private string Output => Path.Combine(_root, "official.csv");

    public DictionaryBackupServiceTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task ConvertsOfflineToHeaderlessSingleColumnAndPreservesChinese()
    {
        await WriteWordsAsync("肝纤维化", "Typeless", "肝纤维化", " ", " comma,quote\" ");
        var before = await File.ReadAllBytesAsync(Input);

        var result = await _service.ConvertToOfficialCsvAsync(Input, Output);

        Assert.Equal(3, result.Total);
        Assert.Equal(1, result.Duplicates);
        Assert.Equal(1, result.EmptyEntries);
        Assert.Equal("\"肝纤维化\"\n\"Typeless\"\n\"comma,quote\"\"\"\n", await File.ReadAllTextAsync(Output, Encoding.UTF8));
        var bytes = await File.ReadAllBytesAsync(Output);
        Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
        Assert.Equal(before, await File.ReadAllBytesAsync(Input));
        Assert.Equal(2, Directory.GetFiles(_root).Length);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"words\":null}")]
    [InlineData("{\"words\":[]}")]
    [InlineData("{\"words\":[{\"term\":\"line1\\nline2\"}]}")]
    public async Task InvalidBackupLeavesExistingCsvUntouched(string json)
    {
        await File.WriteAllTextAsync(Input, json, new UTF8Encoding(false));
        await File.WriteAllTextAsync(Output, "existing-csv", new UTF8Encoding(false));
        await Assert.ThrowsAsync<InvalidDataException>(() => _service.ConvertToOfficialCsvAsync(Input, Output));
        Assert.Equal("existing-csv", await File.ReadAllTextAsync(Output, Encoding.UTF8));
        Assert.Equal(2, Directory.GetFiles(_root).Length);
    }

    [Fact]
    public async Task CannotOverwriteSourceBackup()
    {
        await WriteWordsAsync("中文");
        var before = await File.ReadAllBytesAsync(Input);
        await Assert.ThrowsAsync<InvalidDataException>(() => _service.ConvertToOfficialCsvAsync(Input, Input));
        Assert.Equal(before, await File.ReadAllBytesAsync(Input));
    }

    [Fact]
    public async Task CancellationDoesNotCreateOutput()
    {
        await WriteWordsAsync("中文");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.ConvertToOfficialCsvAsync(Input, Output, cancellation.Token));
        Assert.False(File.Exists(Output));
        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task RejectsFilesExceedingOfficialFiveMegabyteLimit()
    {
        await WriteWordsAsync(new string('中', 2 * 1024 * 1024));
        await Assert.ThrowsAsync<InvalidDataException>(() => _service.ConvertToOfficialCsvAsync(Input, Output));
        Assert.False(File.Exists(Output));
    }

    [Fact]
    public async Task RejectsOversizedInputBeforeParsing()
    {
        await using (var stream = File.Create(Input)) stream.SetLength(32 * 1024 * 1024 + 1);
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => _service.ConvertToOfficialCsvAsync(Input, Output));
        Assert.Contains("32 MB", exception.Message);
        Assert.False(File.Exists(Output));
    }

    [Fact]
    public async Task AcceptsUtf8BomButRejectsInvalidUtf8()
    {
        await File.WriteAllTextAsync(Input, "{\"words\":[{\"term\":\"中文\"}]}", new UTF8Encoding(true));
        await _service.ConvertToOfficialCsvAsync(Input, Output);
        Assert.Equal("\"中文\"\n", await File.ReadAllTextAsync(Output, Encoding.UTF8));
        await File.WriteAllBytesAsync(Input, [0xff, 0xfe, 0xff]);
        await Assert.ThrowsAsync<DecoderFallbackException>(() => _service.ConvertToOfficialCsvAsync(Input, Output));
        Assert.Equal("\"中文\"\n", await File.ReadAllTextAsync(Output, Encoding.UTF8));
    }

    private Task WriteWordsAsync(params string[] terms) => File.WriteAllTextAsync(Input,
        JsonSerializer.Serialize(new DictionaryExport { Words = terms.Select(term => new DictionaryWord { Term = term }).ToArray() }),
        new UTF8Encoding(false));
}
