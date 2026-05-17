using System.Net;
using Aiursoft.MarkToHtml.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MarkToHtml.Tests.IntegrationTests;

[TestClass]
public class AdminControllerTests : TestBase
{
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
