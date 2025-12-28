using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using Aiursoft.MarkToHtml.Authorization;
using Aiursoft.MarkToHtml.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.MarkToHtml.Tests.IntegrationTests;

[TestClass]
public class AdminShareManagementTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;

    public AdminShareManagementTests()
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

    private async Task GrantPermissionToUser(string userId, string permissionName)
    {
        using var scope = _server!.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        
        var roleName = "AdminRole";
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            var role = new IdentityRole(roleName);
            await roleManager.CreateAsync(role);
        }
        
        var roleObj = await roleManager.FindByNameAsync(roleName);
        var claims = await roleManager.GetClaimsAsync(roleObj!);
        if (!claims.Any(c => c.Type == AppPermissions.Type && c.Value == permissionName))
        {
            await roleManager.AddClaimAsync(roleObj!, new Claim(AppPermissions.Type, permissionName));
        }
        
        var user = await userManager.FindByIdAsync(userId);
        if (!await userManager.IsInRoleAsync(user!, roleName))
        {
            await userManager.AddToRoleAsync(user!, roleName);
        }
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
    public async Task Admin_CanManageShares_OfOtherUsersDocument()
    {
        // Arrange
        var ownerId = await RegisterAndLoginUser($"owner-{Guid.NewGuid()}@test.com", "Password123!");
        var documentId = await CreateDocument(ownerId, "Owner's Document", "# Private Content");
        await Logout();
        
        var adminEmail = $"admin-{Guid.NewGuid()}@test.com";
        var adminPassword = "Password123!";
        var adminId = await RegisterAndLoginUser(adminEmail, adminPassword);
        
        // Grant permission
        await GrantPermissionToUser(adminId, AppPermissionNames.CanManageAnyShare);
        
        // Re-login to refresh claims
        await Logout();
        var loginToken = await GetAntiCsrfToken("/Account/Login");
        var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "EmailOrUserName", adminEmail },
            { "Password", adminPassword },
            { "__RequestVerificationToken", loginToken }
        });
        var loginResponse = await _http.PostAsync("/Account/Login", loginContent);
        Assert.AreEqual(HttpStatusCode.Found, loginResponse.StatusCode);

        // Act 1: Access ManageShares page
        var manageSharesResponse = await _http.GetAsync($"/Home/ManageShares/{documentId}");
        Assert.AreEqual(HttpStatusCode.OK, manageSharesResponse.StatusCode, "Admin should be able to access ManageShares page");

        // Act 2: Make Public
        var token1 = await GetAntiCsrfToken($"/Home/ManageShares/{documentId}");
        var makePublicResponse = await _http.PostAsync($"/Home/MakePublic/{documentId}", 
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "__RequestVerificationToken", token1 }
            }));
        Assert.AreEqual(HttpStatusCode.OK, makePublicResponse.StatusCode, "Admin should be able to make document public");

        // Verify it is public
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var doc = await db.MarkdownDocuments.FindAsync(documentId);
            Assert.IsTrue(doc!.IsPublic);
        }

        // Act 3: Make Private
        var token2 = await GetAntiCsrfToken($"/Home/ManageShares/{documentId}");
        var makePrivateResponse = await _http.PostAsync($"/Home/MakePrivate/{documentId}", 
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "__RequestVerificationToken", token2 }
            }));
        Assert.AreEqual(HttpStatusCode.OK, makePrivateResponse.StatusCode, "Admin should be able to make document private");

        // Verify it is private
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var doc = await db.MarkdownDocuments.FindAsync(documentId);
            Assert.IsFalse(doc!.IsPublic);
        }
    }

    [TestMethod]
    public async Task Admin_CanAddAndRemoveShares_OfOtherUsersDocument()
    {
        // Arrange
        var ownerId = await RegisterAndLoginUser($"owner-{Guid.NewGuid()}@test.com", "Password123!");
        var documentId = await CreateDocument(ownerId, "Owner's Document", "# Private Content");
        await Logout();
        
        var adminEmail = $"admin-{Guid.NewGuid()}@test.com";
        var adminPassword = "Password123!";
        var adminId = await RegisterAndLoginUser(adminEmail, adminPassword);
        
        // Target user to share with
        await Logout();
        var targetUserId = await RegisterAndLoginUser($"target-{Guid.NewGuid()}@test.com", "Password123!");
        
        // Grant permission to admin
        await GrantPermissionToUser(adminId, AppPermissionNames.CanManageAnyShare);
        
        // Re-login as admin
        await Logout();
        var loginToken = await GetAntiCsrfToken("/Account/Login");
        var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "EmailOrUserName", adminEmail },
            { "Password", adminPassword },
            { "__RequestVerificationToken", loginToken }
        });
        var loginResponse = await _http.PostAsync("/Account/Login", loginContent);
        Assert.AreEqual(HttpStatusCode.Found, loginResponse.StatusCode);

        // Act 1: Add Share
        var token1 = await GetAntiCsrfToken($"/Home/ManageShares/{documentId}");
        var addShareResponse = await _http.PostAsync($"/Home/AddShare/{documentId}", 
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "TargetUserId", targetUserId },
                { "Permission", "1" }, // Editable
                { "__RequestVerificationToken", token1 }
            }));
        // Redirect indicates success (to ManageShares)
        Assert.AreEqual(HttpStatusCode.Found, addShareResponse.StatusCode);

        // Verify share exists
        Guid shareId;
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var share = await db.DocumentShares.FirstOrDefaultAsync(s => s.DocumentId == documentId && s.SharedWithUserId == targetUserId);
            Assert.IsNotNull(share);
            shareId = share.Id;
        }

        // Act 2: Remove Share
        var token2 = await GetAntiCsrfToken($"/Home/ManageShares/{documentId}");
        var removeShareResponse = await _http.PostAsync($"/Home/RemoveShare/{shareId}", 
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "__RequestVerificationToken", token2 }
            }));
        // Redirect indicates success
        Assert.AreEqual(HttpStatusCode.Found, removeShareResponse.StatusCode);

        // Verify share is gone
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var share = await db.DocumentShares.FirstOrDefaultAsync(s => s.Id == shareId);
            Assert.IsNull(share);
        }
    }
}
