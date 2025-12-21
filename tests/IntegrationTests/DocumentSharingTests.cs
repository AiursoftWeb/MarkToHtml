using System.Net;
using System.Text.RegularExpressions;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using Aiursoft.MarkToHtml.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.MarkToHtml.Tests.IntegrationTests;

[TestClass]
public class DocumentSharingTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;

    public DocumentSharingTests()
    {
        var cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            AllowAutoRedirect = false
        };
        _port = Network.GetAvailablePort();
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri($"http://localhost:{_port}")
        };
    }

    [TestInitialize]
    public async Task CreateServer()
    {
        _server = await AppAsync<Startup>([], port: _port);
        await _server.UpdateDbAsync<TemplateDbContext>();
        await _server.SeedAsync();
        await _server.StartAsync();
    }

    [TestCleanup]
    public async Task CleanServer()
    {
        if (_server == null) return;
        await _server.StopAsync();
        _server.Dispose();
    }

    private async Task<string> GetAntiCsrfToken(string url)
    {
        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(html,
            @"<input name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)"" />");
        if (!match.Success)
        {
            throw new InvalidOperationException($"Could not find anti-CSRF token on page: {url}");
        }

        return match.Groups[1].Value;
    }

    private async Task<string> RegisterAndLoginUser(string email, string password)
    {
        // Register
        var registerToken = await GetAntiCsrfToken("/Account/Register");
        var registerContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Email", email },
            { "Password", password },
            { "ConfirmPassword", password },
            { "__RequestVerificationToken", registerToken }
        });
        var registerResponse = await _http.PostAsync("/Account/Register", registerContent);
        Assert.AreEqual(HttpStatusCode.Found, registerResponse.StatusCode);

        // Get user ID
        using var scope = _server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.IsNotNull(user);
        return user.Id;
    }

    private async Task<Guid> CreateDocument(string userId, string title, string content)
    {
        using var scope = _server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        
        var document = new MarkdownDocument
        {
            Id = Guid.NewGuid(),
            Title = title,
            Content = content,
            UserId = userId,
            CreationTime = DateTime.UtcNow
        };
        
        db.MarkdownDocuments.Add(document);
        await db.SaveChangesAsync();
        
        return document.Id;
    }

    private async Task CreateShare(Guid documentId, string? userId, string? roleId, SharePermission permission)
    {
        using var scope = _server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        
        var share = new DocumentShare
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            SharedWithUserId = userId,
            SharedWithRoleId = roleId,
            Permission = permission,
            CreationTime = DateTime.UtcNow
        };
        
        db.DocumentShares.Add(share);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> MakeDocumentPublic(Guid documentId)
    {
        using var scope = _server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        
        var document = await db.MarkdownDocuments.FindAsync(documentId);
        Assert.IsNotNull(document);
        
        document.PublicId = Guid.NewGuid();
        await db.SaveChangesAsync();
        
        return document.PublicId.Value;
    }

    private async Task Logout()
    {
        var logOffToken = await GetAntiCsrfToken("/Manage/ChangePassword");
        var logOffContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", logOffToken }
        });
        await _http.PostAsync("/Account/LogOff", logOffContent);
    }

    [TestMethod]
    public async Task Owner_CanEdit_TheirOwnDocument()
    {
        // Arrange
        var ownerId = await RegisterAndLoginUser($"owner-{Guid.NewGuid()}@test.com", "Password123!");
        var documentId = await CreateDocument(ownerId, "Owner's Document", "# Test Content");

        // Act
        var editResponse = await _http.GetAsync($"/Home/Edit/{documentId}");

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, editResponse.StatusCode);
    }

    [TestMethod]
    public async Task NonOwner_WithoutShare_CannotView_Document()
    {
        // Arrange
        var ownerId = await RegisterAndLoginUser($"owner-{Guid.NewGuid()}@test.com", "Password123!");
        var documentId = await CreateDocument(ownerId, "Private Document", "# Secret");
        await Logout();
        
        await RegisterAndLoginUser($"viewer-{Guid.NewGuid()}@test.com", "Password123!");

        // Act
        var viewResponse = await _http.GetAsync($"/view/{documentId}");

        // Assert
        Assert.AreEqual(HttpStatusCode.Forbidden, viewResponse.StatusCode);
    }

    [TestMethod]
    public async Task NonOwner_WithoutShare_CannotEdit_Document()
    {
        // Arrange
        var ownerId = await RegisterAndLoginUser($"owner-{Guid.NewGuid()}@test.com", "Password123!");
        var documentId = await CreateDocument(ownerId, "Private Document", "# Secret");
        await Logout();
        
        await RegisterAndLoginUser($"editor-{Guid.NewGuid()}@test.com", "Password123!");

        // Act
        var editResponse = await _http.GetAsync($"/Home/Edit/{documentId}");

        // Assert
        Assert.AreEqual(HttpStatusCode.Forbidden, editResponse.StatusCode);
    }

    [TestMethod]
    public async Task User_WithReadOnlyShare_CanView_ButCannotEdit()
    {
        // Arrange
        var ownerId = await RegisterAndLoginUser($"owner-{Guid.NewGuid()}@test.com", "Password123!");
        var documentId = await CreateDocument(ownerId, "Shared Document", "# Shared Content");
        await Logout();
        
        var viewerId = await RegisterAndLoginUser($"viewer-{Guid.NewGuid()}@test.com", "Password123!");
        await CreateShare(documentId, viewerId, null, SharePermission.ReadOnly);

        // Act - Can view
        var viewResponse = await _http.GetAsync($"/view/{documentId}");
        Assert.AreEqual(HttpStatusCode.OK, viewResponse.StatusCode);

        // Act - Cannot edit
        var editResponse = await _http.GetAsync($"/Home/Edit/{documentId}");
        Assert.AreEqual(HttpStatusCode.Forbidden, editResponse.StatusCode);
    }

    [TestMethod]
    public async Task User_WithEditableShare_CanView_AndEdit()
    {
        // Arrange
        var ownerId = await RegisterAndLoginUser($"owner-{Guid.NewGuid()}@test.com", "Password123!");
        var documentId = await CreateDocument(ownerId, "Editable Document", "# Content");
        await Logout();
        
        var editorId = await RegisterAndLoginUser($"editor-{Guid.NewGuid()}@test.com", "Password123!");
        await CreateShare(documentId, editorId, null, SharePermission.Editable);

        // Act - Can view
        var viewResponse = await _http.GetAsync($"/view/{documentId}");
        Assert.AreEqual(HttpStatusCode.OK, viewResponse.StatusCode);

        // Act - Can edit
        var editResponse = await _http.GetAsync($"/Home/Edit/{documentId}");
        Assert.AreEqual(HttpStatusCode.OK, editResponse.StatusCode);
    }

    [TestMethod]
    public async Task AnonymousUser_CanView_PublicDocument()
    {
        // Arrange
        var ownerId = await RegisterAndLoginUser($"owner-{Guid.NewGuid()}@test.com", "Password123!");
        var documentId = await CreateDocument(ownerId, "Public Document", "# Public Content");
        var publicId = await MakeDocumentPublic(documentId);
        await Logout();

        // Act
        var viewResponse = await _http.GetAsync($"/public/{publicId}");

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, viewResponse.StatusCode);
    }

    [TestMethod]
    public async Task AnonymousUser_CannotView_DocumentByIdEvenIfPublic()
    {
        // Arrange
        var ownerId = await RegisterAndLoginUser($"owner-{Guid.NewGuid()}@test.com", "Password123!");
        var documentId = await CreateDocument(ownerId, "Public Document", "# Public Content");
        await MakeDocumentPublic(documentId);
        await Logout();

        // Act
        var viewResponse = await _http.GetAsync($"/view/{documentId}");

        // Assert - Should redirect to login (Challenge)
        Assert.AreEqual(HttpStatusCode.Redirect, viewResponse.StatusCode);
    }

    [TestMethod]
    public async Task Document_BothPublicAndShared_BothMethodsWork()
    {
        // Arrange
        var ownerId = await RegisterAndLoginUser($"owner-{Guid.NewGuid()}@test.com", "Password123!");
        var documentId = await CreateDocument(ownerId, "Public and Shared", "# Content");
        var publicId = await MakeDocumentPublic(documentId);
        await Logout();
        
        var sharedUserId = await RegisterAndLoginUser($"shared-{Guid.NewGuid()}@test.com", "Password123!");
        await CreateShare(documentId, sharedUserId, null, SharePermission.Editable);

        // Act - Can view via public link (logout first)
        await Logout();
        var publicViewResponse = await _http.GetAsync($"/public/{publicId}");
        Assert.AreEqual(HttpStatusCode.OK, publicViewResponse.StatusCode);

        // Act - Shared user can view and edit via document ID
        await RegisterAndLoginUser($"shared-{Guid.NewGuid()}@test.com", "Password123!");
        var viewResponse = await _http.GetAsync($"/view/{documentId}");
        Assert.AreEqual(HttpStatusCode.OK, viewResponse.StatusCode);
        
        var editResponse = await _http.GetAsync($"/Home/Edit/{documentId}");
        Assert.AreEqual(HttpStatusCode.OK, editResponse.StatusCode);
    }
}
