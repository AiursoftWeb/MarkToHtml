using System.Net;
using Aiursoft.MarkToHtml.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.AspNetCore.Identity;

namespace Aiursoft.MarkToHtml.Tests.IntegrationTests;

[TestClass]
public class PrintTests : TestBase
{
    private async Task<Guid> CreateDocument(string userId, string title, string content, bool isPublic = false)
    {
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        
        var document = new MarkdownDocument
        {
            Id = Guid.NewGuid(),
            Title = title,
            Content = content,
            UserId = userId,
            IsPublic = isPublic,
            CreationTime = DateTime.UtcNow
        };
        
        db.MarkdownDocuments.Add(document);
        await db.SaveChangesAsync();
        
        return document.Id;
    }

    [TestMethod]
    public async Task AnonymousUser_CanAccessPrint_ForPublicDocument()
    {
        // Arrange
        var (email, _) = await RegisterAndLoginAsync();
        string userId;
        using (var scope = Server!.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = await userManager.FindByEmailAsync(email);
            userId = user!.Id;
        }
        var documentId = await CreateDocument(userId, "Public Document", "# Content", isPublic: true);

        // Act
        // Log out to be anonymous
        var logOffToken = await GetAntiCsrfToken("/");
        await Http.PostAsync("/Account/LogOff", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", logOffToken }
        }));

        var response = await Http.GetAsync($"/share/{documentId}/print");

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("# Content") || html.Contains("<h1>Content</h1>"));
    }

    [TestMethod]
    public async Task AnonymousUser_CannotAccessPrint_ForPrivateDocument()
    {
        // Arrange
        var (email, _) = await RegisterAndLoginAsync();
        string userId;
        using (var scope = Server!.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = await userManager.FindByEmailAsync(email);
            userId = user!.Id;
        }
        var documentId = await CreateDocument(userId, "Private Document", "# Content", isPublic: false);

        // Act
        // Log out to be anonymous
        var logOffToken = await GetAntiCsrfToken("/");
        await Http.PostAsync("/Account/LogOff", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", logOffToken }
        }));

        var response = await Http.GetAsync($"/share/{documentId}/print");

        // Assert
        // Redirect to login (Found)
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
    }
}