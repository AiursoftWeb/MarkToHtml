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
        var response = await Http.GetAsync("/Home/Search?q=%25");

        // Bug 3 (before fix): EF.Functions.Like throws on InMemory → 500
        // Bug 3 (after fix): Contains works → 200
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("50% complete"), "Document with % in title must appear in results");
        Assert.IsFalse(html.Contains("Regular document"), "Document without % must NOT appear when searching for %");
    }

    [TestMethod]
    public async Task History_BrowseRoot_ShowsFoldersAndDocuments()
    {
        var (email, _) = await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.IsNotNull(user);

        var folder = new MarkdownDocumentFolder { Name = "MyFolder", UserId = user.Id };
        db.MarkdownDocumentFolders.Add(folder);
        await db.SaveChangesAsync();

        db.MarkdownDocuments.Add(new MarkdownDocument
            { Id = Guid.NewGuid(), Title = "Root Document", Content = "hello", UserId = user.Id });
        await db.SaveChangesAsync();

        var response = await Http.GetAsync("/Home/History");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        // Both folders and documents should be visible when browsing.
        Assert.IsTrue(html.Contains("MyFolder"), "Subfolder should appear in the browse view.");
        Assert.IsTrue(html.Contains("Root Document"), "Document in root should appear in the browse view.");
        Assert.IsTrue(html.Contains("Create Folder"), "Create Folder button should be visible.");
    }

    [TestMethod]
    public async Task History_BrowseSubfolder_ShowsBreadcrumbAndContents()
    {
        var (email, _) = await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.IsNotNull(user);

        var folder = new MarkdownDocumentFolder { Name = "Projects", UserId = user.Id };
        db.MarkdownDocumentFolders.Add(folder);
        await db.SaveChangesAsync();

        db.MarkdownDocuments.Add(new MarkdownDocument
            { Id = Guid.NewGuid(), Title = "Project Alpha", Content = "alpha content", UserId = user.Id, FolderId = folder.Id });
        db.MarkdownDocuments.Add(new MarkdownDocument
            { Id = Guid.NewGuid(), Title = "Root Doc", Content = "root content", UserId = user.Id });
        await db.SaveChangesAsync();

        var response = await Http.GetAsync($"/Home/History?folderId={folder.Id}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        // Only documents in the current folder should be shown.
        Assert.IsTrue(html.Contains("Project Alpha"), "Document in the browsed folder should appear.");
        Assert.IsFalse(html.Contains("Root Doc"), "Document outside the browsed folder should NOT appear.");

        // Breadcrumb should show the folder name.
        Assert.IsTrue(html.Contains("Projects"), "Current folder name should appear in breadcrumb.");
        // Breadcrumb should show "My Documents" as the root.
        Assert.IsTrue(html.Contains("My Documents"), "Breadcrumb should contain root link.");
    }

    [TestMethod]
    public async Task History_SearchParamIgnored_ShowsAllDocuments()
    {
        var (email, _) = await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.IsNotNull(user);

        db.MarkdownDocuments.Add(new MarkdownDocument
            { Id = Guid.NewGuid(), Title = "Alpha", Content = "first", UserId = user.Id });
        db.MarkdownDocuments.Add(new MarkdownDocument
            { Id = Guid.NewGuid(), Title = "Beta", Content = "second", UserId = user.Id });
        await db.SaveChangesAsync();

        // The old `search` query parameter is now ignored by the History action.
        // Both documents should appear since History is pure browse mode.
        var response = await Http.GetAsync("/Home/History?search=Alpha");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains("Alpha"), "All documents should appear — search param is ignored.");
        Assert.IsTrue(html.Contains("Beta"), "All documents should appear — search param is ignored.");
    }
}
