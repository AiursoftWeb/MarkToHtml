using Aiursoft.MarkToHtml.Services;

namespace Aiursoft.MarkToHtml.Tests.UnitTests;

[TestClass]
public class TtsServiceTests
{
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
        var result = TtsService.ChunkText(text, 300);

        Assert.IsTrue(result.Count > 1);
        // Every chunk except possibly the last should end with a delimiter
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
        var result = TtsService.ChunkText(text, 300);

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
        var result = TtsService.ChunkText(text, 300);

        Assert.IsTrue(result.Count > 1);
    }

    [TestMethod]
    public void ChunkText_NoDelimiter_FallbackSplit()
    {
        // A long string without any delimiters
        var text = new string('A', 500);
        var result = TtsService.ChunkText(text, 100);

        Assert.IsTrue(result.Count > 1);
        // Recombined text should match original (trimmed)
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
