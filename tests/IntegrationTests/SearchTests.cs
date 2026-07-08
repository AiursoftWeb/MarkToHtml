using System.Net;
using Aiursoft.MarkToHtml.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Aiursoft.MarkToHtml.Tests.IntegrationTests;

[TestClass]
public class SearchTests : TestBase
{
    [TestMethod]
    public async Task Search_FindsDocumentsByTitle()
    {
        var (email, _) = await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.IsNotNull(user);

        db.MarkdownDocuments.Add(new MarkdownDocument
            { Id = Guid.NewGuid(), Title = "Piano Tutorial", Content = "learn piano", UserId = user.Id });
        db.MarkdownDocuments.Add(new MarkdownDocument
            { Id = Guid.NewGuid(), Title = "Guitar Basics", Content = "learn guitar", UserId = user.Id });
        await db.SaveChangesAsync();

        var response = await Http.GetAsync("/Home/Search?q=Piano");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains("Piano Tutorial"), "Matching document should appear in results.");
        Assert.IsFalse(html.Contains("Guitar Basics"), "Non-matching document should NOT appear.");
    }

    [TestMethod]
    public async Task Search_FindsDocumentsByContent()
    {
        var (email, _) = await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.IsNotNull(user);

        db.MarkdownDocuments.Add(new MarkdownDocument
            { Id = Guid.NewGuid(), Title = "Doc One", Content = "contains embedded systems", UserId = user.Id });
        db.MarkdownDocuments.Add(new MarkdownDocument
            { Id = Guid.NewGuid(), Title = "Doc Two", Content = "unrelated stuff", UserId = user.Id });
        await db.SaveChangesAsync();

        var response = await Http.GetAsync("/Home/Search?q=embedded");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains("Doc One"), "Document with matching content should appear.");
        Assert.IsFalse(html.Contains("Doc Two"), "Document without matching content should NOT appear.");
    }

    [TestMethod]
    public async Task Search_AcrossAllFolders_ReturnsFlattenedResults()
    {
        var (email, _) = await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.IsNotNull(user);

        // Create a folder and put a document inside it.
        var folder = new MarkdownDocumentFolder { Name = "Projects", UserId = user.Id };
        db.MarkdownDocumentFolders.Add(folder);
        await db.SaveChangesAsync();

        db.MarkdownDocuments.Add(new MarkdownDocument
            { Id = Guid.NewGuid(), Title = "Root Doc", Content = "at root level", UserId = user.Id });
        db.MarkdownDocuments.Add(new MarkdownDocument
            { Id = Guid.NewGuid(), Title = "Nested Doc", Content = "inside folder", UserId = user.Id, FolderId = folder.Id });
        await db.SaveChangesAsync();

        // Search should find the nested document even though it's inside a folder.
        var response = await Http.GetAsync("/Home/Search?q=Nested");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains("Nested Doc"), "Document inside subfolder should appear (recursive search).");
        Assert.IsFalse(html.Contains("Root Doc"), "Non-matching root document should NOT appear.");

        // The folder badge should be shown for the nested document.
        Assert.IsTrue(html.Contains("Projects"), "Folder name badge should appear next to the nested document.");
    }

    [TestMethod]
    public async Task Search_EmptyQuery_RedirectsToHistory()
    {
        await RegisterAndLoginAsync();

        var response = await Http.GetAsync("/Home/Search");

        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        Assert.IsTrue(
            response.Headers.Location?.OriginalString.Contains("/Home/History") == true,
            "Empty search query should redirect to History page.");
    }

    [TestMethod]
    public async Task Search_NoResults_ShowsEmptyMessage()
    {
        var (email, _) = await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.IsNotNull(user);

        db.MarkdownDocuments.Add(new MarkdownDocument
            { Id = Guid.NewGuid(), Title = "Hello World", Content = "some content", UserId = user.Id });
        await db.SaveChangesAsync();

        var response = await Http.GetAsync("/Home/Search?q=NonExistentQuery12345");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(
            html.Contains("No documents found") || html.Contains("No documents found for your search query"),
            "Empty state message should appear when no documents match.");
    }

    [TestMethod]
    public async Task Search_Unauthenticated_ReturnsUnauthorized()
    {
        // Don't register/login — just hit the search endpoint as anonymous.
        var response = await Http.GetAsync("/Home/Search?q=test");

        // Should redirect to login (302 Found → /Account/Login) or return 401.
        var isRedirectToLogin = response.StatusCode == HttpStatusCode.Found &&
                                response.Headers.Location?.OriginalString.Contains("/Account/Login") == true;
        Assert.IsTrue(
            isRedirectToLogin || response.StatusCode == HttpStatusCode.Unauthorized,
            $"Anonymous search should redirect to login or return 401. Got {response.StatusCode}.");
    }

    [TestMethod]
    public async Task Search_ShowsFolderBadgeForDocumentsInFolders()
    {
        var (email, _) = await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.IsNotNull(user);

        var folder = new MarkdownDocumentFolder { Name = "Music", UserId = user.Id };
        db.MarkdownDocumentFolders.Add(folder);
        await db.SaveChangesAsync();

        db.MarkdownDocuments.Add(new MarkdownDocument
            { Id = Guid.NewGuid(), Title = "Moonlight Sonata", Content = "sheet music", UserId = user.Id, FolderId = folder.Id });
        await db.SaveChangesAsync();

        var response = await Http.GetAsync("/Home/Search?q=Moonlight");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains("Moonlight Sonata"), "Document should appear in results.");
        Assert.IsTrue(html.Contains("Music"), "Folder badge should show the folder name.");
        // The folder badge links to browse that folder.
        Assert.IsTrue(
            html.Contains($"/Home/History?folderId={folder.Id}"),
            "Folder badge should link to browsing the folder.");
    }
}
