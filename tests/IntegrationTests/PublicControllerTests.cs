using System.Net;
using Aiursoft.MarkToHtml.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MarkToHtml.Tests.IntegrationTests;

[TestClass]
public class PublicControllerTests : TestBase
{
    [TestMethod]
    public async Task View_NotFound_Returns404()
    {
        var response = await Http.GetAsync($"/share/{Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task View_PrivateDocument_Anonymous_ReturnsUnauthorizedOrRedirect()
    {
        Guid docId;
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var adminId = (await db.Users.FirstAsync(u => u.Email == "admin@default.com")).Id;
            docId = Guid.NewGuid();
            db.MarkdownDocuments.Add(new MarkdownDocument
            {
                Id = docId,
                Title = "Secret Document",
                Content = "# Top Secret",
                UserId = adminId,
                IsPublic = false,
                CreationTime = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var response = await Http.GetAsync($"/share/{docId}");
        Assert.IsTrue(response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Redirect);
    }

    [TestMethod]
    public async Task View_PublicDocument_Anonymous_Returns200()
    {
        Guid docId;
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var adminId = (await db.Users.FirstAsync(u => u.Email == "admin@default.com")).Id;
            docId = Guid.NewGuid();
            db.MarkdownDocuments.Add(new MarkdownDocument
            {
                Id = docId,
                Title = "Public Doc",
                Content = "# Hello Public World",
                UserId = adminId,
                IsPublic = true,
                CreationTime = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var response = await Http.GetAsync($"/share/{docId}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(content.Contains("Hello Public World"));

        // Test Print
        var printResponse = await Http.GetAsync($"/share/{docId}/print?includeLogo=true&theme=default");
        Assert.AreEqual(HttpStatusCode.OK, printResponse.StatusCode);

        // Test Download
        var downloadResponse = await Http.GetAsync($"/share/{docId}/download");
        Assert.AreEqual(HttpStatusCode.OK, downloadResponse.StatusCode);
        var downloadedText = await downloadResponse.Content.ReadAsStringAsync();
        Assert.AreEqual("# Hello Public World", downloadedText);

        // Test Raw
        var rawResponse = await Http.GetAsync($"/share/{docId}/raw");
        Assert.AreEqual(HttpStatusCode.OK, rawResponse.StatusCode);
        var rawText = await rawResponse.Content.ReadAsStringAsync();
        Assert.AreEqual("# Hello Public World", rawText);
    }
}
