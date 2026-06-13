using System.Net;

namespace Aiursoft.MarkToHtml.Tests.IntegrationTests;

[TestClass]
public class HomeControllerTests : TestBase
{
    [TestMethod]
    public async Task GetIndex()
    {
        var url = "/";
        var response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsFalse(html.Contains("include-print-logo"));
    }

    // Bug 1 (log order) is a pure observability issue with no user-facing behavior change.
    // There is no integration test that can fail because of a wrong log message,
    // so it is fixed directly in the code without a corresponding failing test.

    // Bug 2: SaveUpdate should return 404 when the document does not exist.
    // Currently it silently creates a new document instead — this test will FAIL until fixed.
    [TestMethod]
    public async Task SaveUpdate_WithNonExistentDocumentId_ReturnsNotFound()
    {
        await RegisterAndLoginAsync();
        var nonExistentId = Guid.NewGuid();

        var response = await PostForm("/Home/SaveUpdate", new Dictionary<string, string>
        {
            { "DocumentId", nonExistentId.ToString() },
            { "Title", "Ghost document" },
            { "InputMarkdown", "# Should not be created" }
        });

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }
}
