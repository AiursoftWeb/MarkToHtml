using Aiursoft.MarkToHtml.Services.Agent;
using Aiursoft.MarkToHtml.Services.Tools;

namespace Aiursoft.MarkToHtml.Tests.UnitTests;

[TestClass]
public class DocumentToolsTests
{
    [TestMethod]
    public async Task ReadFullDocument_Empty_ReturnsWarning()
    {
        var current = new CurrentUserService();
        var reader = new DocumentReadTools(current);
        var result = await reader.ReadFullDocument();
        Assert.IsTrue(result.Contains("No document content"));
    }

    [TestMethod]
    public async Task ReadFullDocument_WithContent_ReturnsNumberedLines()
    {
        var current = new CurrentUserService { DocumentContent = "Line 1\nLine 2" };
        var reader = new DocumentReadTools(current);
        var result = await reader.ReadFullDocument();
        Assert.IsTrue(result.Contains("1|Line 1"));
        Assert.IsTrue(result.Contains("2|Line 2"));
    }

    [TestMethod]
    public async Task ReadDocumentLines_WithContent_ReturnsClampedRange()
    {
        var current = new CurrentUserService { DocumentContent = "Line 1\nLine 2\nLine 3" };
        var reader = new DocumentReadTools(current);
        
        var emptyRes = await new DocumentReadTools(new CurrentUserService()).ReadDocumentLines(1, 2);
        Assert.IsTrue(emptyRes.Contains("No document content"));

        var result = await reader.ReadDocumentLines(2, 2);
        Assert.IsTrue(result.Contains("2|Line 2"));
        Assert.IsFalse(result.Contains("1|Line 1"));
    }

    [TestMethod]
    public async Task ReplaceText_Validations()
    {
        var current = new CurrentUserService { DocumentContent = "Hello World! Hello again." };
        var editor = new DocumentEditTools(current);

        // Same string
        var same = await editor.ReplaceText("abc", "abc");
        Assert.IsTrue(same.Contains("Error: old_string and new_string are exactly the same"));

        // No content
        var emptyEditor = new DocumentEditTools(new CurrentUserService());
        var noContent = await emptyEditor.ReplaceText("a", "b");
        Assert.IsTrue(noContent.Contains("No document content available"));

        // Not found
        var notFound = await editor.ReplaceText("foo", "bar");
        Assert.IsTrue(notFound.Contains("old_string not found"));

        // Multiple occurrences
        var multi = await editor.ReplaceText("Hello", "Hi");
        Assert.IsTrue(multi.Contains("appears 2 times"));

        // Success propose
        var success = await editor.ReplaceText("World", "Aiursoft");
        Assert.IsTrue(success.Contains("Edit proposed: replace 5 characters with 8 characters"));
    }
}
