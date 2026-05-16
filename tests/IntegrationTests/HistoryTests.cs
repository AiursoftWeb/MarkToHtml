using System.Net;
using Aiursoft.MarkToHtml.Entities;
using Microsoft.AspNetCore.Identity;

namespace Aiursoft.MarkToHtml.Tests.IntegrationTests;

[TestClass]
public class HistoryTests : TestBase
{
    [TestMethod]
    public async Task GetHistory()
    {
        await RegisterAndLoginAsync();
        var url = "/Home/History";
        
        var response = await Http.GetAsync(url);
        
        response.EnsureSuccessStatusCode();
    }

    // Bug 3: Searching with a "%" character should only match documents whose title/content
    // literally contains "%". The old code used EF.Functions.Like with an un-escaped pattern,
    // causing "%" to act as a SQL wildcard (matching everything).
    // On InMemory DB (used in tests), EF.Functions.Like throws an exception → 500.
    // After fix (using .Contains()), this test must return 200 and show only the matching document.
    [TestMethod]
    public async Task History_SearchWithPercentSign_OnlyReturnsDocumentsContainingLiteralPercent()
    {
        var (email, _) = await RegisterAndLoginAsync();

        // Get the user's ID so we can insert documents directly into the DB
        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();

        var user = await userManager.FindByEmailAsync(email);
        Assert.IsNotNull(user);

        db.MarkdownDocuments.Add(new MarkdownDocument
        {
            Id = Guid.NewGuid(),
            Title = "50% complete",
            Content = "has percent",
            UserId = user.Id,
            CreationTime = DateTime.UtcNow
        });
        db.MarkdownDocuments.Add(new MarkdownDocument
        {
            Id = Guid.NewGuid(),
            Title = "Regular document",
            Content = "no special chars",
            UserId = user.Id,
            CreationTime = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // Search for "%" — should only match "50% complete", not "Regular document"
        var response = await Http.GetAsync("/Home/History?search=%25");

        // Bug 3 (before fix): EF.Functions.Like throws on InMemory → 500
        // Bug 3 (after fix): Contains works → 200
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("50% complete"), "Document with % in title must appear in results");
        Assert.IsFalse(html.Contains("Regular document"), "Document without % must NOT appear when searching for %");
    }
}
