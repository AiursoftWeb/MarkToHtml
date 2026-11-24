using System.Net;
using Aiursoft.MarkToHtml.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MarkToHtml.Tests.IntegrationTests;

[TestClass]
public class AdminControllerTests : TestBase
{
    [TestMethod]
    public async Task EditDocument_HidesUploadErrorBannerAndKeepsLayoutPadding()
    {
        await LoginAsAdmin();

        Guid documentId;
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var adminId = (await db.Users.FirstAsync(u => u.Email == "admin@default.com")).Id;
            documentId = Guid.NewGuid();
            db.MarkdownDocuments.Add(new MarkdownDocument
            {
                Id = documentId,
                Title = "Admin edit layout regression",
                Content = "# Test document",
                UserId = adminId,
                CreationTime = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var response = await Http.GetAsync($"/Admin/EditDocument/{documentId}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("id=\"upload-error-banner\" class=\"quick-save-banner d-print-none\""));
        Assert.IsTrue(html.Contains("transform: translateX(-50%) translateY(-120%);"));
        Assert.IsTrue(html.Contains(".quick-save-banner.show"));
        Assert.IsTrue(html.Contains("transform: translateX(-50%) translateY(24px);"));
        Assert.IsFalse(html.Contains("main.content"));
        Assert.IsFalse(html.Contains("padding: 0 !important;"));
    }

    [TestMethod]
    public async Task EditDocument_Post_UpdatesDocument()
    {
        await LoginAsAdmin();

        Guid documentId;
        string adminId;
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            adminId = (await db.Users.FirstAsync(u => u.Email == "admin@default.com")).Id;
            documentId = Guid.NewGuid();
            db.MarkdownDocuments.Add(new MarkdownDocument
            {
                Id = documentId,
                Title = "Before Edit",
                Content = "# Old Content",
                UserId = adminId,
                CreationTime = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var response = await PostForm($"/Admin/EditDocument/{documentId}", new Dictionary<string, string>
        {
            { "DocumentId", documentId.ToString() },
            { "Title", "After Edit" },
            { "InputMarkdown", "# New Content" },
            { "SelectedUserId", adminId }
        });

        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var doc = await db.MarkdownDocuments.FindAsync(documentId);
            Assert.IsNotNull(doc);
            Assert.AreEqual("After Edit", doc.Title);
            Assert.AreEqual("# New Content", doc.Content);
        }
    }

    [TestMethod]
    public async Task DeleteDocument_Lifecycle_Success()
    {
        await LoginAsAdmin();

        Guid documentId;
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var adminId = (await db.Users.FirstAsync(u => u.Email == "admin@default.com")).Id;
            documentId = Guid.NewGuid();
            db.MarkdownDocuments.Add(new MarkdownDocument
            {
                Id = documentId,
                Title = "To Delete",
                Content = "# Delete me",
                UserId = adminId,
                CreationTime = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var getResponse = await Http.GetAsync($"/Admin/DeleteDocument/{documentId}");
        Assert.AreEqual(HttpStatusCode.OK, getResponse.StatusCode);

        var postResponse = await PostForm($"/Admin/DeleteDocument/{documentId}", new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.Found, postResponse.StatusCode);

        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var doc = await db.MarkdownDocuments.FindAsync(documentId);
            Assert.IsNull(doc);
        }
    }

    // Bug: AdminController.AllDocuments uses EF.Functions.Like with unescaped user input.
    // On InMemory DB, EF.Functions.Like throws → 500. After fix (Contains) → 200.
    [TestMethod]
    public async Task AllDocuments_SearchWithPercentSign_ReturnsOkAndOnlyMatchingDocs()
    {
        await LoginAsAdmin();

        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var adminId = (await db.Users.FirstAsync(u => u.Email == "admin@default.com")).Id;

        db.MarkdownDocuments.Add(new MarkdownDocument
        {
            Id = Guid.NewGuid(),
            Title = "50% complete",
            Content = "has percent",
            UserId = adminId,
            CreationTime = DateTime.UtcNow
        });
        db.MarkdownDocuments.Add(new MarkdownDocument
        {
            Id = Guid.NewGuid(),
            Title = "Regular document",
            Content = "no special chars",
            UserId = adminId,
            CreationTime = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var response = await Http.GetAsync("/Admin/AllDocuments?search=%25");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("50% complete"), "Document with % in title must appear in results");
        Assert.IsFalse(html.Contains("Regular document"), "Document without % must NOT appear when searching for %");
    }

    // Bug: AdminController.UserDocuments uses the same vulnerable EF.Functions.Like pattern.
    [TestMethod]
    public async Task UserDocuments_SearchWithPercentSign_ReturnsOkAndOnlyMatchingDocs()
    {
        await LoginAsAdmin();

        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var adminId = (await db.Users.FirstAsync(u => u.Email == "admin@default.com")).Id;

        db.MarkdownDocuments.Add(new MarkdownDocument
        {
            Id = Guid.NewGuid(),
            Title = "75% done",
            Content = "has percent",
            UserId = adminId,
            CreationTime = DateTime.UtcNow
        });
        db.MarkdownDocuments.Add(new MarkdownDocument
        {
            Id = Guid.NewGuid(),
            Title = "Another doc",
            Content = "no special chars",
            UserId = adminId,
            CreationTime = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var response = await Http.GetAsync($"/Admin/UserDocuments/{adminId}?search=%25");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("75% done"), "Document with % in title must appear in results");
        Assert.IsFalse(html.Contains("Another doc"), "Document without % must NOT appear when searching for %");
    }
}
