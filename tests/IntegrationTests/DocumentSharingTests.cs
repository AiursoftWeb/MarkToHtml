using System.Net;
using System.Text.RegularExpressions;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using Aiursoft.MarkToHtml.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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

    [TestMethod]
    public async Task SharedUser_CannotManageShares_EvenWithEditablePermission()
    {
        // Arrange
        var ownerId = await RegisterAndLoginUser($"owner-{Guid.NewGuid()}@test.com", "Password123!");
        var documentId = await CreateDocument(ownerId, "Shared Document", "# Content");
        await Logout();
        
        var editorId = await RegisterAndLoginUser($"editor-{Guid.NewGuid()}@test.com", "Password123!");
        await CreateShare(documentId, editorId, null, SharePermission.Editable);

        // Act - Try to access ManageShares
        var manageSharesResponse = await _http.GetAsync($"/Home/ManageShares/{documentId}");

        // Assert - Should be forbidden
        Assert.AreEqual(HttpStatusCode.NotFound, manageSharesResponse.StatusCode);
    }

    [TestMethod]
    public async Task SharedUser_CannotMakeDocumentPublic_EvenWithEditablePermission()
    {
        // Arrange
        var ownerId = await RegisterAndLoginUser($"owner-{Guid.NewGuid()}@test.com", "Password123!");
        var documentId = await CreateDocument(ownerId, "Private Document", "# Content");
        await Logout();
        
        var editorId = await RegisterAndLoginUser($"editor-{Guid.NewGuid()}@test.com", "Password123!");
        await CreateShare(documentId, editorId, null, SharePermission.Editable);

        // Act - Try to make document public
        var token = await GetAntiCsrfToken($"/Home/Edit/{documentId}");
        var makePublicResponse = await _http.PostAsync($"/Home/MakePublic/{documentId}", 
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "__RequestVerificationToken", token }
            }));

        // Assert - Should be forbidden or not found
        Assert.IsTrue(
            makePublicResponse.StatusCode == HttpStatusCode.NotFound || 
            makePublicResponse.StatusCode == HttpStatusCode.Forbidden);
    }

    [TestMethod]
    public async Task SharedUser_CannotMakeDocumentPrivate_EvenWithEditablePermission()
    {
        // Arrange
        var ownerId = await RegisterAndLoginUser($"owner-{Guid.NewGuid()}@test.com", "Password123!");
        var documentId = await CreateDocument(ownerId, "Public Document", "# Content");
        _ = await MakeDocumentPublic(documentId);
        await Logout();
        
        var editorId = await RegisterAndLoginUser($"editor-{Guid.NewGuid()}@test.com", "Password123!");
        await CreateShare(documentId, editorId, null, SharePermission.Editable);

        // Act - Try to make document private
        var token = await GetAntiCsrfToken($"/Home/Edit/{documentId}");
        var makePrivateResponse = await _http.PostAsync($"/Home/MakePrivate/{documentId}", 
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "__RequestVerificationToken", token }
            }));

        // Assert - Should be forbidden or not found
        Assert.IsTrue(
            makePrivateResponse.StatusCode == HttpStatusCode.NotFound || 
            makePrivateResponse.StatusCode == HttpStatusCode.Forbidden);
    }

    [TestMethod]
    public async Task SharedUser_CannotDeleteDocument_EvenWithEditablePermission()
    {
        // Arrange
        var ownerId = await RegisterAndLoginUser($"owner-{Guid.NewGuid()}@test.com", "Password123!");
        var documentId = await CreateDocument(ownerId, "Document to Delete", "# Content");
        await Logout();
        
        var editorId = await RegisterAndLoginUser($"editor-{Guid.NewGuid()}@test.com", "Password123!");
        await CreateShare(documentId, editorId, null, SharePermission.Editable);

        // Act - Try to access delete page
        var deletePageResponse = await _http.GetAsync($"/Home/Delete/{documentId}");
        Assert.AreEqual(HttpStatusCode.NotFound, deletePageResponse.StatusCode);

        // Act - Try to delete document directly
        var token = await GetAntiCsrfToken($"/Home/Edit/{documentId}");
        var deleteResponse = await _http.PostAsync($"/Home/Delete/{documentId}", 
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "__RequestVerificationToken", token }
            }));

        // Assert - Should be forbidden or not found
        Assert.IsTrue(
            deleteResponse.StatusCode == HttpStatusCode.NotFound || 
            deleteResponse.StatusCode == HttpStatusCode.Forbidden);
    }

    [TestMethod]
    public async Task SharedUser_CannotAddMoreShares_ToDocument()
    {
        // Arrange
        var ownerId = await RegisterAndLoginUser($"owner-{Guid.NewGuid()}@test.com", "Password123!");
        var documentId = await CreateDocument(ownerId, "Shared Document", "# Content");
        await Logout();
        
        var editor1Id = await RegisterAndLoginUser($"editor1-{Guid.NewGuid()}@test.com", "Password123!");
        await CreateShare(documentId, editor1Id, null, SharePermission.Editable);
        
        // Create another user to share with
        await Logout();
        var editor2Id = await RegisterAndLoginUser($"editor2-{Guid.NewGuid()}@test.com", "Password123!");
        await Logout();
        
        // Login as editor1 (who has Editable permission but is not the owner)
        await RegisterAndLoginUser($"editor1-{Guid.NewGuid()}@test.com", "Password123!");

        // Act - Try to add a new share
        var token = await GetAntiCsrfToken($"/Home/Edit/{documentId}");
        var addShareResponse = await _http.PostAsync($"/Home/AddShare/{documentId}", 
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "TargetUserId", editor2Id },
                { "Permission", "1" }, // Editable
                { "__RequestVerificationToken", token }
            }));

        // Assert - Should be forbidden or not found (can't access ManageShares)
        Assert.IsTrue(
            addShareResponse.StatusCode == HttpStatusCode.NotFound || 
            addShareResponse.StatusCode == HttpStatusCode.Forbidden);
    }

    [TestMethod]
    public async Task Owner_CanManageAll_SharingAndPrivacySettings()
    {
        // Arrange
        var ownerId = await RegisterAndLoginUser($"owner-{Guid.NewGuid()}@test.com", "Password123!");
        var documentId = await CreateDocument(ownerId, "Owner's Document", "# Content");
        _ = await RegisterAndLoginUser($"viewer-{Guid.NewGuid()}@test.com", "Password123!");
        await Logout();
        
        // Login as owner
        await RegisterAndLoginUser($"owner-{Guid.NewGuid()}@test.com", "Password123!");

        // Act & Assert - Can access ManageShares
        var manageSharesResponse = await _http.GetAsync($"/Home/ManageShares/{documentId}");
        Assert.AreEqual(HttpStatusCode.OK, manageSharesResponse.StatusCode);

        // Act & Assert - Can make public
        var token1 = await GetAntiCsrfToken($"/Home/ManageShares/{documentId}");
        var makePublicResponse = await _http.PostAsync($"/Home/MakePublic/{documentId}", 
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "__RequestVerificationToken", token1 }
            }));
        Assert.AreEqual(HttpStatusCode.OK, makePublicResponse.StatusCode);

        // Act & Assert - Can make private
        var token2 = await GetAntiCsrfToken($"/Home/ManageShares/{documentId}");
        var makePrivateResponse = await _http.PostAsync($"/Home/MakePrivate/{documentId}", 
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "__RequestVerificationToken", token2 }
            }));
        Assert.AreEqual(HttpStatusCode.OK, makePrivateResponse.StatusCode);

        // Act & Assert - Can delete
        var deletePageResponse = await _http.GetAsync($"/Home/Delete/{documentId}");
        Assert.AreEqual(HttpStatusCode.OK, deletePageResponse.StatusCode);
    }

    [TestMethod]
    public async Task ComplexPermissionScenario_RoleAndDirectShare_PermissionPriority()
    {
        // Arrange: Create owner and document
        var ownerId = await RegisterAndLoginUser($"owner-{Guid.NewGuid()}@test.com", "Password123!");
        var documentId = await CreateDocument(ownerId, "Complex Permission Test", "# Content");
        
        // Create user who will be in a role
        await Logout();
        var userId = await RegisterAndLoginUser($"user-{Guid.NewGuid()}@test.com", "Password123!");
        
        // Create role and add user to it
        string roleId;
        using (var scope = _server!.Services.CreateScope())
        {
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            
            var role = new IdentityRole { Id = Guid.NewGuid().ToString(), Name = $"TestRole-{Guid.NewGuid()}" };
            await roleManager.CreateAsync(role);
            roleId = role.Id;
            
            var user = await userManager.FindByIdAsync(userId);
            await userManager.AddToRoleAsync(user!, role.Name!);
        }

        // Share document: Direct share (Editable) + Role share (ReadOnly)
        await CreateShare(documentId, userId, null, SharePermission.Editable); // Direct: Editable
        await CreateShare(documentId, null, roleId, SharePermission.ReadOnly); // Role: ReadOnly

        // Test 1: User can edit (has Editable permission from direct share, even though role is ReadOnly)
        var editResponse1 = await _http.GetAsync($"/Home/Edit/{documentId}");
        Assert.AreEqual(HttpStatusCode.OK, editResponse1.StatusCode, "User should be able to edit with direct Editable permission");

        // Test 2: Remove user from role
        using (var scope = _server!.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var user = await userManager.FindByIdAsync(userId);
            var role = await roleManager.FindByIdAsync(roleId);
            await userManager.RemoveFromRoleAsync(user!, role!.Name!);
        }

        // User should still be able to edit (still has direct Editable share)
        var editResponse2 = await _http.GetAsync($"/Home/Edit/{documentId}");
        Assert.AreEqual(HttpStatusCode.OK, editResponse2.StatusCode, "User should still be able to edit with direct share after leaving role");

        // Test 3: Add user back to role
        using (var scope = _server!.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var user = await userManager.FindByIdAsync(userId);
            var role = await roleManager.FindByIdAsync(roleId);
            await userManager.AddToRoleAsync(user!, role!.Name!);
        }

        // User should still be able to edit
        var editResponse3 = await _http.GetAsync($"/Home/Edit/{documentId}");
        Assert.AreEqual(HttpStatusCode.OK, editResponse3.StatusCode, "User should be able to edit after rejoining role");

        // Test 4: Remove direct share (keep only role share which is ReadOnly)
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var directShare = await db.DocumentShares
                .FirstOrDefaultAsync(s => s.DocumentId == documentId && s.SharedWithUserId == userId);
            if (directShare != null)
            {
                db.DocumentShares.Remove(directShare);
                await db.SaveChangesAsync();
            }
        }

        // User should now only be able to view (lost Editable permission, only has ReadOnly from role)
        var viewResponse = await _http.GetAsync($"/view/{documentId}");
        Assert.AreEqual(HttpStatusCode.OK, viewResponse.StatusCode, "User should be able to view with role ReadOnly permission");

        var editResponse4 = await _http.GetAsync($"/Home/Edit/{documentId}");
        Assert.AreEqual(HttpStatusCode.Forbidden, editResponse4.StatusCode, "User should NOT be able to edit with only role ReadOnly permission");
    }
}
