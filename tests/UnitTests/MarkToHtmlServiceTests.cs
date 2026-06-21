using Aiursoft.MarkToHtml.Services;
using Ganss.Xss;
using Markdig;

namespace Aiursoft.MarkToHtml.Tests.UnitTests;

[TestClass]
public class MarkToHtmlServiceTests
{
    private MarkToHtmlService _service = null!;

    [TestInitialize]
    public void Initialize()
    {
        // Setup dependencies manually or via DI container
        var services = new ServiceCollection();
        
        // Add core services needed
        services.AddSingleton(new MarkdownPipelineBuilder().UseAdvancedExtensions().Build());
        services.AddSingleton(_ =>
        {
            var sanitizer = new HtmlSanitizer();
            sanitizer.AllowedTags.Add("br");
            return sanitizer;
        });
        services.AddTransient<MarkToHtmlService>();

        var provider = services.BuildServiceProvider();
        _service = provider.GetRequiredService<MarkToHtmlService>();
    }

    [TestMethod]
    public void ConvertMarkdownToHtml_BasicMarkdown_ReturnsHtml()
    {
        var markdown = "# Hello World";
        var html = _service.ConvertMarkdownToHtml(markdown);
        
        // Markdig usually produces "\n" at the end.
        StringAssert.Contains(html, "<h1>Hello World</h1>");
    }

    [TestMethod]
    public void ConvertMarkdownToHtml_XSS_SanitizesHtml()
    {
        var markdown = "<script>alert('xss')</script>";
        var html = _service.ConvertMarkdownToHtml(markdown);
        
        Assert.DoesNotContain("<script>", html);
    }
    
    [TestMethod]
    public void ConvertMarkdownToHtml_Table_ReturnsTable()
    {
        var markdown = @"
| Header 1 | Header 2 |
| -------- | -------- |
| Cell 1   | Cell 2   |
";
        var html = _service.ConvertMarkdownToHtml(markdown);
        
        StringAssert.Contains(html, "<table>");
        StringAssert.Contains(html, "<th>Header 1</th>");
    }

    [TestMethod]
    [DataRow("| Header |\n| --- |\n| First<br>Second |")]
    [DataRow("| Header |\n| --- |\n| First<br/>Second |")]
    public void ConvertMarkdownToHtml_TableLineBreakTag_ReturnsLineBreak(string markdown)
    {
        var html = _service.ConvertMarkdownToHtml(markdown);

        StringAssert.Contains(html, "<td>First<br>Second</td>");
    }

    [TestMethod]
    public void ConvertMarkdownToHtml_TrailingSpaces_ReturnsLineBreak()
    {
        var markdown = "First  \nSecond";

        var html = _service.ConvertMarkdownToHtml(markdown);

        StringAssert.Contains(html, "First<br>\nSecond");
    }

    [TestMethod]
    public void ConvertMarkdownToHtml_LineBreakWithScriptAttribute_RemovesAttribute()
    {
        var markdown = "First<br onmouseover=\"alert('xss')\">Second";

        var html = _service.ConvertMarkdownToHtml(markdown);

        StringAssert.Contains(html, "First<br>Second");
        Assert.DoesNotContain("onmouseover", html);
    }
}
