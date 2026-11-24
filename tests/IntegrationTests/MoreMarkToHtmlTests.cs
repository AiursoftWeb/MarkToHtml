using System.Net;
using Aiursoft.MarkToHtml.Entities;

namespace Aiursoft.MarkToHtml.Tests.IntegrationTests;

[TestClass]
public class MoreMarkToHtmlTests : TestBase
{
    [TestMethod]
    public async Task TestProgramExtends()
    {
        await Server!.CopyAvatarFileAsync();
        await Server!.CopyAvatarFileAsync();
    }

    [TestMethod]
    public async Task TestContractAccessRules()
    {
        var notFound = await Http.GetAsync($"/contract/{Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.NotFound, notFound.StatusCode);

        Guid privateDocId;
        Guid publicDocId;
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var admin = await db.Users.FirstAsync(u => u.Email == "admin@default.com");
            var doc1 = new MarkdownDocument
            {
                Id = Guid.NewGuid(),
                Title = "Private Contract",
                Content = "# Top Secret",
                UserId = admin.Id,
                IsPublic = false,
                CreationTime = DateTime.UtcNow
            };
            var doc2 = new MarkdownDocument
            {
                Id = Guid.NewGuid(),
                Title = "Public Contract",
                Content = "# Public Info",
                UserId = admin.Id,
                IsPublic = true,
                CreationTime = DateTime.UtcNow
            };
            db.MarkdownDocuments.AddRange(doc1, doc2);
            await db.SaveChangesAsync();
            privateDocId = doc1.Id;
            publicDocId = doc2.Id;
        }

        var unauth = await Http.GetAsync($"/contract/{privateDocId}");
        Assert.IsFalse(unauth.IsSuccessStatusCode);

        var pubRes = await Http.GetAsync($"/contract/{publicDocId}");
        Assert.AreEqual(HttpStatusCode.OK, pubRes.StatusCode);
    }
}
