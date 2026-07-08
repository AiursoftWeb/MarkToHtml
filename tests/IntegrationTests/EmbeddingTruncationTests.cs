using System.Net;
using System.Text;
using Aiursoft.MarkToHtml.Entities;
using Aiursoft.MarkToHtml.Services.BackgroundJobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Aiursoft.MarkToHtml.Tests.IntegrationTests;

[TestClass]
public class EmbeddingTruncationTests
{
    /// <summary>
    /// Fake <see cref="HttpMessageHandler"/> that delegates to a factory so each call
    /// can return a different response (or the same one). Also captures every request
    /// body so tests can assert that the binary-search fallback is sending progressively
    /// shorter text.
    /// </summary>
    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<int, HttpResponseMessage> _factory;
        public List<string> SentInputs { get; } = [];

        public FakeHttpMessageHandler(Func<int, HttpResponseMessage> factory)
        {
            _factory = factory;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var parsed = JsonConvert.DeserializeAnonymousType(body, new { input = "" });
            SentInputs.Add(parsed!.input);

            var response = _factory(SentInputs.Count);

            // Clone content so it can be consumed across multiple requests when
            // the factory returns the same HttpResponseMessage instance.
            var clone = new HttpResponseMessage(response.StatusCode);
            var content = await response.Content!.ReadAsStringAsync(cancellationToken);
            clone.Content = new StringContent(content, Encoding.UTF8, "application/json");
            return clone;
        }
    }

    private class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static HttpResponseMessage OllamaSuccess(float[][] embeddings)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonConvert.SerializeObject(new { embeddings }),
                Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage OllamaError(string error)
    {
        return new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(
                JsonConvert.SerializeObject(new { error }),
                Encoding.UTF8, "application/json")
        };
    }

    private static GenerateDocumentEmbeddingsJob CreateJob(
        IHttpClientFactory httpClientFactory)
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        return new GenerateDocumentEmbeddingsJob(
            null!,
            null!,
            httpClientFactory,
            loggerFactory.CreateLogger<GenerateDocumentEmbeddingsJob>());
    }

    private static MarkdownDocument CreateDoc(string title, string content)
    {
        return new MarkdownDocument
        {
            Id = Guid.NewGuid(),
            Title = title,
            Content = content,
            UserId = "test-user"
        };
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // TruncateForEmbedding — pure truncation logic
    // ══════════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void TruncateForEmbedding_ShortText_ReturnsUnchanged()
    {
        var text = "Hello world";
        var result = GenerateDocumentEmbeddingsJob.TruncateForEmbedding(text, 8000);
        Assert.AreEqual(text, result);
    }

    [TestMethod]
    public void TruncateForEmbedding_ExactlyMaxChars_ReturnsUnchanged()
    {
        var text = new string('x', 8000);
        var result = GenerateDocumentEmbeddingsJob.TruncateForEmbedding(text, 8000);
        Assert.AreEqual(text, result);
        Assert.AreEqual(8000, result.Length);
    }

    [TestMethod]
    public void TruncateForEmbedding_ExceedsMax_UsesHeadTailStrategy()
    {
        // Build text where the tail portion is entirely 'B' so we can verify ends-with.
        // 8000 'A' + 2000 'B' = 10000 chars. After head+tail truncation to 8000:
        //   head = 6000 'A', tail = 1995 'B' (since last 1995 of original are all 'B').
        var text = new string('A', 8000) + new string('B', 2000);
        var result = GenerateDocumentEmbeddingsJob.TruncateForEmbedding(text, 8000);

        Assert.AreEqual(8000, result.Length);
        // Head: first 6000 chars (75% of 8000) should be 'A'
        Assert.IsTrue(result.StartsWith(new string('A', 6000)));
        // Separator
        Assert.IsTrue(result.Contains("\n...\n"));
        // Tail: last 1995 chars (8000 - 6000 - 5) are all 'B'
        Assert.IsTrue(result.EndsWith(new string('B', 1995)));
    }

    [TestMethod]
    public void TruncateForEmbedding_MaxTooShortForHeadTail_SimpleHardTruncation()
    {
        var text = new string('x', 100);
        // 10 is too short for head+tail (75% of 10 = 7, tail = 10-7-5 = -2)
        var result = GenerateDocumentEmbeddingsJob.TruncateForEmbedding(text, 10);
        Assert.AreEqual(10, result.Length);
        Assert.IsFalse(result.Contains("\n...\n"));
        Assert.AreEqual(new string('x', 10), result);
    }

    [TestMethod]
    public void TruncateForEmbedding_AtMinimum_StillWorks()
    {
        // Text longer than 500 so truncation actually kicks in.
        var text = new string('H', 400) + "\nConclusion\n" + new string('T', 400);
        var result = GenerateDocumentEmbeddingsJob.TruncateForEmbedding(text, 500);

        Assert.AreEqual(500, result.Length);
        Assert.IsTrue(result.Contains("\n...\n"));
        // head = 375 (75% of 500), tail = 500 - 375 - 5 = 120
        Assert.IsTrue(result.StartsWith(new string('H', 375)));
        Assert.IsTrue(result.EndsWith(new string('T', 120)));
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Binary-search retry — CallEmbedApiAsync
    // ══════════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task CallEmbedApiAsync_FirstAttemptSucceeds_NoTruncation()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            OllamaSuccess([[0.1f, 0.2f, 0.3f]]));
        var factory = new FakeHttpClientFactory(handler);
        var job = CreateJob(factory);

        var doc = CreateDoc("Short", "Short content");
        var result = await job.CallEmbedApiAsync(
            "http://localhost:11434", "bge-m3:latest", "", doc);

        Assert.AreEqual(3, result.Length);
        Assert.AreEqual(1, handler.SentInputs.Count);
        Assert.IsTrue(handler.SentInputs[0].Contains("Short"));
    }

    [TestMethod]
    public async Task CallEmbedApiAsync_ContextLengthError_RetriesWithBinaryFallback()
    {
        var handler = new FakeHttpMessageHandler(call =>
        {
            // First call: context-length error. Second call: success.
            if (call == 1)
                return OllamaError("input context length exceeded maximum (8192 tokens)");
            return OllamaSuccess([[0.5f, -0.3f]]);
        });
        var factory = new FakeHttpClientFactory(handler);
        var job = CreateJob(factory);

        var doc = CreateDoc("Long", new string('X', 10000)); // well over 8000 chars

        var result = await job.CallEmbedApiAsync(
            "http://localhost:11434", "bge-m3:latest", "", doc);

        // Should succeed on second attempt with halved text
        Assert.AreEqual(2, result.Length);
        Assert.AreEqual(2, handler.SentInputs.Count);
        Assert.AreEqual(8000, handler.SentInputs[0].Length);
        Assert.AreEqual(4000, handler.SentInputs[1].Length);
        Assert.IsTrue(handler.SentInputs[1].Contains("\n...\n"));
    }

    [TestMethod]
    public async Task CallEmbedApiAsync_ContextLengthError_PersistsUntilMinimum()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            OllamaError("maximum context length exceeded"));

        var factory = new FakeHttpClientFactory(handler);
        var job = CreateJob(factory);

        var doc = CreateDoc("Huge", new string('X', 20000));

        HttpRequestException? ex = null;
        try
        {
            await job.CallEmbedApiAsync("http://localhost:11434", "bge-m3:latest", "", doc);
        }
        catch (HttpRequestException e)
        {
            ex = e;
        }

        Assert.IsNotNull(ex);
        Assert.IsTrue(ex!.Message.Contains("'Huge'"));
        // Binary search: 8000 → 4000 → 2000 → 1000 → 500 (5 attempts, then 500 <= 500 throws)
        Assert.AreEqual(5, handler.SentInputs.Count);
        Assert.AreEqual(8000, handler.SentInputs[0].Length);
        Assert.AreEqual(4000, handler.SentInputs[1].Length);
        Assert.AreEqual(2000, handler.SentInputs[2].Length);
        Assert.AreEqual(1000, handler.SentInputs[3].Length);
        Assert.AreEqual(500, handler.SentInputs[4].Length);
    }

    [TestMethod]
    public async Task CallEmbedApiAsync_ShortTextAtMinimum_StillFailsOnce()
    {
        // Even a very short document can hit the context limit if the model is
        // configured with a tiny context window. The binary search should bottom
        // out gracefully.
        var handler = new FakeHttpMessageHandler(_ =>
            OllamaError("context length limit reached"));

        var factory = new FakeHttpClientFactory(handler);
        var job = CreateJob(factory);

        var doc = CreateDoc("Tiny", "Hi");

        HttpRequestException? ex = null;
        try
        {
            await job.CallEmbedApiAsync("http://localhost:11434", "bge-m3:latest", "", doc);
        }
        catch (HttpRequestException e)
        {
            ex = e;
        }

        Assert.IsNotNull(ex);
        Assert.IsTrue(ex!.Message.Contains("'Tiny'"));
        // Text is short, so length is unchanged across retries, but binary search
        // still halves maxChars each time: 8000 → 4000 → 2000 → 1000 → 500 → throw
        Assert.AreEqual(5, handler.SentInputs.Count);
    }

    [TestMethod]
    public async Task CallEmbedApiAsync_NonContextError_DoesNotRetry()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            OllamaError("service unavailable"));

        var factory = new FakeHttpClientFactory(handler);
        var job = CreateJob(factory);

        var doc = CreateDoc("Doc", "Hello");

        HttpRequestException? ex = null;
        try
        {
            await job.CallEmbedApiAsync("http://localhost:11434", "bge-m3:latest", "", doc);
        }
        catch (HttpRequestException e)
        {
            ex = e;
        }

        Assert.IsNotNull(ex);
        Assert.IsTrue(ex!.Message.Contains("service unavailable"));
        // Only ONE attempt — the error is not context-length-related.
        Assert.AreEqual(1, handler.SentInputs.Count);
    }
}
