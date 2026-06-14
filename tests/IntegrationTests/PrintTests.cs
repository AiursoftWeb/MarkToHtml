using System.Net;
using Aiursoft.MarkToHtml.Entities;
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
        Assert.IsFalse(html.Contains("class=\"print-logo\""));
        Assert.IsFalse(html.Contains("@top-center"));
        Assert.IsFalse(html.Contains("@bottom-center"));
    }

    [TestMethod]
    public async Task Print_CanIncludeLogo_WhenRequested()
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
        var response = await Http.GetAsync($"/share/{documentId}/print?includeLogo=true");

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("class=\"print-logo\""));
        Assert.IsTrue(html.Contains("/logo.svg"));
    }

    [TestMethod]
    public async Task Print_CanUseExpandedLogoSize_WhenRequested()
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
        var response = await Http.GetAsync($"/share/{documentId}/print?includeLogo=true&logoSize=super-large");

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("print-logo-super-large"));
        Assert.IsTrue(html.Contains("<option value=\"super-large\" selected=\"selected\">Super Large</option>"));
        Assert.IsTrue(html.Contains("<option value=\"extra-large\""));
        Assert.IsTrue(html.Contains("<option value=\"tiny\""));
    }

    [TestMethod]
    public async Task Print_AppliesDocumentSettings_WhenRequested()
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
        var response = await Http.GetAsync($"/share/{documentId}/print?includeLogo=true&theme=editorial&pageSize=Letter&orientation=landscape&logoSize=large&logoPosition=right&printHeader=title&printFooter=pageOfTotal");

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("print-theme-editorial"));
        Assert.IsTrue(html.Contains("/print-themes/editorial/theme.css"));
        Assert.IsTrue(html.Contains("print-page-letter"));
        Assert.IsTrue(html.Contains("print-orientation-landscape"));
        Assert.IsTrue(html.Contains("print-logo-large"));
        Assert.IsTrue(html.Contains("print-logo-right"));
        Assert.IsTrue(html.Contains("size: Letter landscape;"));
        Assert.IsTrue(html.Contains("@top-center"));
        Assert.IsTrue(html.Contains("content: \"Public Document\";"));
        Assert.IsTrue(html.Contains("@bottom-center"));
        Assert.IsTrue(html.Contains("counter(page) \" of \" counter(pages)"));
        Assert.IsTrue(html.Contains("<option value=\"title\" selected=\"selected\">Title Header</option>"));
        Assert.IsTrue(html.Contains("<option value=\"pageOfTotal\" selected=\"selected\">Page 1 of 3</option>"));
        Assert.IsTrue(html.Contains("Final pagination, margins, headers, and footers are generated by your browser when you click Print."));
        Assert.IsFalse(html.Contains("apply-print-settings"));
        Assert.IsTrue(html.Contains("const renderingReady = Promise.all(promises)"));
        Assert.IsTrue(html.Contains("renderingReady.finally(() => window.print())"));
        Assert.IsTrue(html.Contains("printNow.disabled = true;"));
        Assert.IsTrue(html.Contains("addEventListener('change', applyPrintSettings)"));
    }

    [TestMethod]
    public async Task Print_CanUseSimplePageNumberFooter_WhenRequested()
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
        var response = await Http.GetAsync($"/share/{documentId}/print?printFooter=number");

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("@bottom-center"));
        Assert.IsTrue(html.Contains("content: counter(page);"));
        Assert.IsTrue(html.Contains("<option value=\"number\" selected=\"selected\">1, 2, 3</option>"));
        Assert.IsTrue(html.Contains("<div class=\"print-preview-footer\">1</div>"));
    }

    [TestMethod]
    public async Task Print_TitleHeaderKeepsUnicodeText_WhenRequested()
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
        var documentId = await CreateDocument(userId, "使用快速为文档打印", "# Content", isPublic: true);

        // Act
        var response = await Http.GetAsync($"/share/{documentId}/print?printHeader=title");

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("content: \"使用快速为文档打印\";"));
        Assert.IsFalse(html.Contains("\\u4F7F"));
        Assert.IsTrue(html.Contains("print-preview-header"));
        Assert.IsTrue(html.Contains("使用快速为文档打印"));
    }

    [TestMethod]
    public async Task Print_TitleHeaderEscapesStyleClosingText_WhenRequested()
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
        var documentId = await CreateDocument(userId, "</style><script>alert(1)</script>", "# Content", isPublic: true);

        // Act
        var response = await Http.GetAsync($"/share/{documentId}/print?printHeader=title");

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("content: \"\\3C /style\\3E \\3C script\\3E alert(1)\\3C /script\\3E \";"));
        Assert.IsFalse(html.Contains("content: \"</style><script>alert(1)</script>\";"));
        Assert.IsFalse(html.Contains("</style><script>alert(1)</script>"));
    }

    [TestMethod]
    public async Task PublicView_DoesNotRenderPrintSettingsBeforePreview()
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
        var response = await Http.GetAsync($"/share/{documentId}");

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsFalse(html.Contains("print-settings-modal"));
        Assert.IsFalse(html.Contains("print-include-logo"));
        Assert.IsFalse(html.Contains("open-print-preview-button"));
    }

    [TestMethod]
    public async Task Print_CanUseModernTheme_WhenRequested()
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
        var response = await Http.GetAsync($"/share/{documentId}/print?theme=modern");

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("print-theme-modern"));
        Assert.IsTrue(html.Contains("/print-themes/modern/theme.css"));
        Assert.IsTrue(html.Contains("<option value=\"modern\" selected=\"selected\">Modern</option>"));
    }

    [TestMethod]
    public async Task Print_FallsBackToDefaultSettings_WhenInvalidValuesRequested()
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
        var response = await Http.GetAsync($"/share/{documentId}/print?theme=unknown&pageSize=Poster&orientation=diagonal&logoSize=huge&logoPosition=floating&printHeader=custom&printFooter=chapter");

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("print-theme-default"));
        Assert.IsTrue(html.Contains("/print-themes/default/theme.css"));
        Assert.IsTrue(html.Contains("print-page-a4"));
        Assert.IsTrue(html.Contains("print-orientation-portrait"));
        Assert.IsTrue(html.Contains("print-logo-medium"));
        Assert.IsTrue(html.Contains("print-logo-left"));
        Assert.IsTrue(html.Contains("size: A4 portrait;"));
        Assert.IsTrue(html.Contains("<option value=\"none\" selected=\"selected\">No Header</option>"));
        Assert.IsTrue(html.Contains("<option value=\"none\" selected=\"selected\">No Footer</option>"));
        Assert.IsFalse(html.Contains("@top-center"));
        Assert.IsFalse(html.Contains("@bottom-center"));
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
