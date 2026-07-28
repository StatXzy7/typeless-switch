using System.Net;
using System.Text;
using System.Text.Json;
using TypelessSwitch.Core;
using TypelessSwitch.Core.Models;

namespace TypelessSwitch.Tests;

public sealed class DictionaryServiceTests
{
    [Fact]
    public async Task List_UnauthorizedResponsePreservesStatusForSafeRetry()
    {
        var service = new DictionaryService(new HttpClient(new UnauthorizedHandler()));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => service.ListAsync("expired"));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.DoesNotContain("private-detail", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BulkImport_ChunksAndRunsRequestsConcurrently()
    {
        var handler = new RecordingHandler();
        var service = new DictionaryService(new HttpClient(handler));
        var root = Path.Combine(Path.GetTempPath(), $"typeless-switch-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var input = Path.Combine(root, "dictionary.json");
        var export = new DictionaryExport
        {
            TotalCount = 450,
            Words = Enumerable.Range(1, 450).Select(index => new DictionaryWord { Term = $"term-{index}" }).ToArray()
        };
        await File.WriteAllTextAsync(input, JsonSerializer.Serialize(export));

        try
        {
            var result = await service.ImportAsync("token", input, DictionaryImportMode.Bulk, concurrency: 8);

            Assert.Equal(450, result.Imported);
            Assert.Equal(3, handler.BulkRequestCount);
            Assert.True(handler.MaximumConcurrentRequests > 1);
            Assert.Equal(new[] { 50, 200, 200 }, handler.BulkSizes.Order().ToArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FullImport_PreservesMetadataAndHonorsConcurrencyLimit()
    {
        var handler = new FullImportHandler();
        var service = new DictionaryService(new HttpClient(handler));
        var root = Path.Combine(Path.GetTempPath(), $"typeless-switch-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var input = Path.Combine(root, "dictionary.json");
        var replacement = JsonDocument.Parse("{\"value\":\"replacement\"}").RootElement.Clone();
        var words = new List<DictionaryWord> { new() { Term = "existing", Language = "en" } };
        words.AddRange(Enumerable.Range(1, 18).Select(index => new DictionaryWord
        {
            Term = $"term-{index}",
            Language = "en",
            Category = "custom",
            Auto = false,
            Replace = true,
            ReplaceTargets = [replacement]
        }));
        await File.WriteAllTextAsync(input, JsonSerializer.Serialize(new DictionaryExport { TotalCount = words.Count, Words = words }));

        try
        {
            var result = await service.ImportAsync("token", input, DictionaryImportMode.Full, concurrency: 4);

            Assert.Equal(18, result.Imported);
            Assert.Equal(1, result.Skipped);
            Assert.InRange(handler.MaximumConcurrentRequests, 2, 4);
            Assert.Equal(18, handler.RequestBodies.Count);
            foreach (var body in handler.RequestBodies)
            {
                using var document = JsonDocument.Parse(body);
                Assert.Equal("en", document.RootElement.GetProperty("lang").GetString());
                Assert.Equal("custom", document.RootElement.GetProperty("category").GetString());
                Assert.False(document.RootElement.GetProperty("auto").GetBoolean());
                Assert.True(document.RootElement.GetProperty("replace").GetBoolean());
                Assert.Single(document.RootElement.GetProperty("replace_targets").EnumerateArray());
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Export_WritesJsonTextAndCsvTogether()
    {
        var handler = new ExportHandler();
        var service = new DictionaryService(new HttpClient(handler));
        var session = new TypelessSession
        {
            Email = "source@example.com",
            UserId = "source-user",
            AccessToken = "token",
            RefreshToken = "refresh",
            LoginTime = 0
        };
        var root = Path.Combine(Path.GetTempPath(), $"typeless-switch-test-{Guid.NewGuid():N}");
        try
        {
            var result = await service.ExportAsync(session, root);

            Assert.Equal(2, result.Total);
            Assert.True(File.Exists(result.JsonPath));
            Assert.Equal(new[] { "hello", "comma,quote\"" }, await File.ReadAllLinesAsync(result.TextPath));
            var csv = await File.ReadAllTextAsync(result.CsvPath);
            Assert.Contains("\"comma,quote\"\"\"", csv);
            var exported = JsonSerializer.Deserialize<DictionaryExport>(await File.ReadAllTextAsync(result.JsonPath));
            Assert.Equal("source@example.com", exported!.Account.Email);
            Assert.Equal(2, exported.Words.Count);
            Assert.Equal("refresh", handler.AuthorizationToken);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private int _activeRequests;
        private int _maximumConcurrentRequests;
        private int _bulkRequestCount;
        public int MaximumConcurrentRequests => _maximumConcurrentRequests;
        public int BulkRequestCount => _bulkRequestCount;
        public List<int> BulkSizes { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
                return Json("{\"status\":\"OK\",\"data\":{\"words\":[]}}");

            Interlocked.Increment(ref _bulkRequestCount);
            var active = Interlocked.Increment(ref _activeRequests);
            int observed;
            while (active > (observed = Volatile.Read(ref _maximumConcurrentRequests)))
                Interlocked.CompareExchange(ref _maximumConcurrentRequests, active, observed);

            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using (var document = JsonDocument.Parse(body))
            {
                var content = document.RootElement.GetProperty("content").GetString() ?? string.Empty;
                lock (BulkSizes) BulkSizes.Add(content.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
            }
            await Task.Delay(60, cancellationToken);
            Interlocked.Decrement(ref _activeRequests);
            return Json("{\"status\":\"OK\"}");
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class FullImportHandler : HttpMessageHandler
    {
        private int _active;
        private int _maximum;
        public int MaximumConcurrentRequests => _maximum;
        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
                return Json("{\"status\":\"OK\",\"data\":{\"words\":[{\"term\":\"existing\",\"lang\":\"en\"}]}}");

            var active = Interlocked.Increment(ref _active);
            int observed;
            while (active > (observed = Volatile.Read(ref _maximum)))
                Interlocked.CompareExchange(ref _maximum, active, observed);
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            lock (RequestBodies) RequestBodies.Add(body);
            await Task.Delay(35, cancellationToken);
            Interlocked.Decrement(ref _active);
            return Json("{\"status\":\"OK\"}");
        }
    }

    private sealed class ExportHandler : HttpMessageHandler
    {
        public string? AuthorizationToken { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            AuthorizationToken = request.Headers.Authorization?.Parameter;
            return Task.FromResult(Json("{\"status\":\"OK\",\"data\":{\"words\":["
                + "{\"term\":\"hello\",\"lang\":\"en\",\"category\":\"custom\",\"auto\":true,\"replace\":false},"
                + "{\"term\":\"comma,quote\\\"\",\"lang\":\"en\",\"category\":\"custom\",\"auto\":false,\"replace\":true}"
                + "]}}"));
        }
    }

    private sealed class UnauthorizedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"detail\":\"private-detail\"}", Encoding.UTF8, "application/json")
            });
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
}
