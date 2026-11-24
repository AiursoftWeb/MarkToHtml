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

    [TestMethod]
    public async Task SelfHost_ReturnsOk()
    {
        var response = await Http.GetAsync("/Home/SelfHost");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task SaveNew_AnonymousPost_ReturnsRenderedHtml()
    {
        var response = await PostForm("/Home/SaveNew", new Dictionary<string, string>
        {
            { "InputMarkdown", "# Anonymous Heading" }
        });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("Anonymous Heading"));
    }

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

    [TestMethod]
    public async Task DocumentLifecycle_CreateEditDownloadVisibilityDelete()
    {
        var (_, _) = await RegisterAndLoginAsync();

        // 1. SaveNew
        var createResponse = await PostForm("/Home/SaveNew", new Dictionary<string, string>
        {
            { "Title", "My Test Doc" },
            { "InputMarkdown", "# Title 1\nSome test markdown" }
        });
        Assert.AreEqual(HttpStatusCode.Found, createResponse.StatusCode);
        var location = createResponse.Headers.Location?.ToString() ?? "";
        Assert.IsTrue(location.Contains("/Home/Edit/"));

        // Extract doc id
        var docIdStr = location.Split('/').Last().Split('?').First();
        var docId = Guid.Parse(docIdStr);

        // 2. Edit GET
        var editResponse = await Http.GetAsync($"/Home/Edit/{docId}");
        Assert.AreEqual(HttpStatusCode.OK, editResponse.StatusCode);

        // 3. SaveUpdate (AJAX)
        var updateResponse = await PostForm("/Home/SaveUpdate", new Dictionary<string, string>
        {
            { "DocumentId", docId.ToString() },
            { "Title", "My Updated Doc" },
            { "InputMarkdown", "# Updated Content" }
        });
        Assert.AreEqual(HttpStatusCode.OK, updateResponse.StatusCode);

        // 4. Download
        var downloadResponse = await Http.GetAsync($"/Home/Download/{docId}");
        Assert.AreEqual(HttpStatusCode.OK, downloadResponse.StatusCode);
        var downloaded = await downloadResponse.Content.ReadAsStringAsync();
        Assert.AreEqual("# Updated Content", downloaded);

        // 5. UpdateVisibility
        var visResponse = await PostForm($"/Home/UpdateVisibility/{docId}", new Dictionary<string, string>
        {
            { "publicAccess", "true" }
        });
        Assert.AreEqual(HttpStatusCode.Found, visResponse.StatusCode);

        // 6. Delete GET
        var deleteGet = await Http.GetAsync($"/Home/Delete/{docId}");
        Assert.AreEqual(HttpStatusCode.OK, deleteGet.StatusCode);

        // 7. Delete POST
        var deletePost = await PostForm($"/Home/Delete/{docId}", new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.Found, deletePost.StatusCode);

        // Verify document is deleted
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Aiursoft.MarkToHtml.Entities.TemplateDbContext>();
        var doc = await db.MarkdownDocuments.FindAsync(docId);
        Assert.IsNull(doc);
    }
}
