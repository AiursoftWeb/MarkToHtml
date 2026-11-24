using System.Net;
using System.Text;
using Aiursoft.MarkToHtml.Configuration;
using Aiursoft.MarkToHtml.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aiursoft.MarkToHtml.Tests.UnitTests;

[TestClass]
public class TtsServiceTests
{
    private class FakeHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(response);
        }
    }

    private class FakeHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    [TestMethod]
    public async Task GetVoicesAsync_ReturnsVoices()
    {
        var jsonResponse = "[{\"name\":\"alloy\",\"filename\":\"alloy.wav\",\"size_kb\":123.4}]";
        var fakeResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        };
        var handler = new FakeHttpMessageHandler(fakeResponse);
        var httpClient = new HttpClient(handler);
        var factory = new FakeHttpClientFactory(httpClient);
        var settings = Options.Create(new TtsSettings { BaseUrl = "http://tts.local", ApiToken = "token" });

        var service = new TtsService(factory, settings, NullLogger<TtsService>.Instance);
        var voices = await service.GetVoicesAsync();

        Assert.AreEqual(1, voices.Count);
        Assert.AreEqual("alloy", voices[0].Name);
    }

    [TestMethod]
    public async Task GenerateSpeechAsync_ReturnsStreamAndContentType()
    {
        var fakeResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1, 2, 3, 4])
        };
        var handler = new FakeHttpMessageHandler(fakeResponse);
        var httpClient = new HttpClient(handler);
        var factory = new FakeHttpClientFactory(httpClient);
        var settings = Options.Create(new TtsSettings { BaseUrl = "http://tts.local", ApiToken = "token" });

        var service = new TtsService(factory, settings, NullLogger<TtsService>.Instance);
        var (stream, contentType) = await service.GenerateSpeechAsync("hello", format: "mp3");

        Assert.IsNotNull(stream);
        Assert.AreEqual("audio/mpeg", contentType);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void ChunkText_EmptyOrNull_ReturnsEmptyList(string? text)
    {
        var result = TtsService.ChunkText(text!);
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void ChunkText_ShortText_ReturnsSingleChunk()
    {
        var result = TtsService.ChunkText("Hello world.");
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("Hello world.", result[0]);
    }

    [TestMethod]
    public void ChunkText_SplitsAtPeriod()
    {
        var text = string.Join(" ", Enumerable.Repeat("This is a sentence.", 20));
        var result = TtsService.ChunkText(text);

        Assert.IsTrue(result.Count > 1);
        for (var i = 0; i < result.Count - 1; i++)
        {
            var endsWithDelimiter = result[i].EndsWith('.') || result[i].EndsWith('。') ||
                                     result[i].EndsWith(',') || result[i].EndsWith('，') ||
                                     result[i].EndsWith('!') || result[i].EndsWith('！') ||
                                     result[i].EndsWith('?') || result[i].EndsWith('？') ||
                                     result[i].EndsWith(';') || result[i].EndsWith('；');
            Assert.IsTrue(endsWithDelimiter, $"Chunk {i} does not end with a delimiter: '{result[i][^10..]}'");
        }
    }

    [TestMethod]
    public void ChunkText_SplitsAtChinesePunctuation()
    {
        var text = string.Join("", Enumerable.Repeat("这是一句话。", 100));
        var result = TtsService.ChunkText(text);

        Assert.IsTrue(result.Count > 1);
        foreach (var chunk in result.SkipLast(1))
        {
            Assert.IsTrue(chunk.EndsWith('。'), $"Chunk should end with '。': '{chunk[^5..]}'");
        }
    }

    [TestMethod]
    public void ChunkText_SplitsAtNewline()
    {
        var text = string.Join("\n", Enumerable.Repeat("Line of text here.", 30));
        var result = TtsService.ChunkText(text);

        Assert.IsTrue(result.Count > 1);
    }

    [TestMethod]
    public void ChunkText_NoDelimiter_FallbackSplit()
    {
        var text = new string('A', 500);
        var result = TtsService.ChunkText(text, 100);

        Assert.IsTrue(result.Count > 1);
        var recombined = string.Join("", result);
        Assert.AreEqual(text, recombined);
    }

    [TestMethod]
    public void ChunkText_NoChunkExceedsMax()
    {
        var text = string.Join(". ", Enumerable.Repeat("This is a sentence", 50)) + ".";
        const int max = 200;
        var result = TtsService.ChunkText(text, max);

        foreach (var chunk in result)
        {
            Assert.IsTrue(chunk.Length <= max,
                $"Chunk length {chunk.Length} exceeds max {max}: '{chunk[..Math.Min(50, chunk.Length)]}...'");
        }
    }

    [TestMethod]
    public void ChunkText_PreservesContent()
    {
        var sentences = new[] {
            "第一句话。",
            "第二句话！",
            "第三句话？",
            "Fourth sentence.",
            "Fifth sentence!"
        };
        var text = string.Join("", sentences);
        var result = TtsService.ChunkText(text);

        Assert.IsTrue(result.Count > 0);
        var recombined = string.Join("", result);
        Assert.AreEqual(text, recombined);
    }
}
