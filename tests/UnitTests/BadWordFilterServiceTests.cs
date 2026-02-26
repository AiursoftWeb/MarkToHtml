using Aiursoft.MarkToHtml.Services;

namespace Aiursoft.MarkToHtml.Tests.UnitTests;

[TestClass]
public class BadWordFilterServiceTests
{
    private readonly BadWordFilterService _service = new();

    [TestMethod]
    [DataRow("This is a clean text.")]
    [DataRow("Hello World!")]
    [DataRow(null)]
    [DataRow("")]
    public void ContainsBadWord_CleanText_ReturnsFalse(string text)
    {
        var result = _service.ContainsBadWord(text);
        Assert.IsFalse(result);
    }

    [TestMethod]
    [DataRow("Some content with 法轮 in it.")]
    [DataRow("Text containing 六四.")]
    [DataRow("Mixed case FREEGATE inside.")]
    public void ContainsBadWord_BadText_ReturnsTrue(string text)
    {
        var result = _service.ContainsBadWord(text);
        Assert.IsTrue(result);
    }
}
