using System.Net;
using System.Text.RegularExpressions;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using Aiursoft.MarkToHtml.Authorization;
using Aiursoft.MarkToHtml.Entities;
using Microsoft.AspNetCore.Identity;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.MarkToHtml.Tests.IntegrationTests;

[TestClass]
public class UsersManagementTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;

    public UsersManagementTests()
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

    private async Task<(string email, string password, User user)> RegisterAndLoginAsync()
    {
        var email = $"admin-{Guid.NewGuid()}@aiursoft.com";
        var password = "Test-Password-123";

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

        using var scope = _server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var user = db.Users.First(u => u.Email == email);

        return (email, password, user);
    }

    private async Task GrantPermissionAsync(User user, string permissionName)
    {
        using var scope = _server!.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        
        var roleName = $"RoleFor-{permissionName}";
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            var role = new IdentityRole(roleName);
            await roleManager.CreateAsync(role);
            await roleManager.AddClaimAsync(role, new System.Security.Claims.Claim(AppPermissions.Type, permissionName));
        }

        var dbUser = await userManager.FindByIdAsync(user.Id);
        await userManager.AddToRoleAsync(dbUser!, roleName);
    }

    [TestMethod]
    public async Task CrudUsersTest()
    {
        // 1. Register Admin User
        var (adminEmail, _, adminUser) = await RegisterAndLoginAsync();

        // 2. Grant Permissions
        await GrantPermissionAsync(adminUser, AppPermissionNames.CanReadUsers);
        await GrantPermissionAsync(adminUser, AppPermissionNames.CanAddUsers);
        await GrantPermissionAsync(adminUser, AppPermissionNames.CanEditUsers);
        await GrantPermissionAsync(adminUser, AppPermissionNames.CanDeleteUsers);
        await GrantPermissionAsync(adminUser, AppPermissionNames.CanAssignRoleToUser);
        
        // Re-login to refresh claims (claims are loaded on login)
        var logOffToken = await GetAntiCsrfToken("/Manage/ChangePassword"); 
        await _http.PostAsync("/Account/LogOff", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", logOffToken }
        }));
        
        var loginToken = await GetAntiCsrfToken("/Account/Login");
        await _http.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "EmailOrUserName", adminEmail },
            { "Password", "Test-Password-123" },
            { "__RequestVerificationToken", loginToken }
        }));

        // 3. Create a new user via UI
        var newUserEmail = $"user-{Guid.NewGuid()}@example.com";
        var newUserPassword = "New-User-Password-123";
        var newUserName = $"user{Guid.NewGuid().ToString().Replace("-", "")}"; // Simple username
        var createToken = await GetAntiCsrfToken("/Users/Create");
        
        var createContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Email", newUserEmail },
            { "UserName", newUserName },
            { "DisplayName", "Test User" },
            { "Password", newUserPassword },
            { "__RequestVerificationToken", createToken }
        });
        
        var createResponse = await _http.PostAsync("/Users/Create", createContent);
        Assert.AreEqual(HttpStatusCode.Found, createResponse.StatusCode);
        
        // 4. Verify user list
        var indexResponse = await _http.GetAsync("/Users/Index");
        indexResponse.EnsureSuccessStatusCode();
        var indexHtml = await indexResponse.Content.ReadAsStringAsync();
        StringAssert.Contains(indexHtml, newUserEmail);

        // Get the new user ID
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var newUser = db.Users.First(u => u.Email == newUserEmail);
            var newUserId = newUser.Id;

            // 5. Edit user
            var editToken = await GetAntiCsrfToken($"/Users/Edit/{newUserId}");
            var editContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "Id", newUserId },
                { "Email", newUserEmail },
                { "UserName", "updatedusername" },
                { "DisplayName", "Updated Display Name" },
                { "Password", "you-cant-read-it" }, // Keep password
                { "AvatarUrl", "Workspace/avatar/default-avatar.jpg" },
                { "__RequestVerificationToken", editToken }
            });

            var editResponse = await _http.PostAsync($"/Users/Edit/{newUserId}", editContent);
            if (editResponse.StatusCode != HttpStatusCode.Found)
            {
                var errorHtml = await editResponse.Content.ReadAsStringAsync();
                Console.WriteLine($"Edit failed. Response: {errorHtml}");
            }
            Assert.AreEqual(HttpStatusCode.Found, editResponse.StatusCode);

            // 6. View Details
            var detailsResponse = await _http.GetAsync($"/Users/Details/{newUserId}");
            detailsResponse.EnsureSuccessStatusCode();
            var detailsHtml = await detailsResponse.Content.ReadAsStringAsync();
            StringAssert.Contains(detailsHtml, "Updated Display Name");
            
            // 7. Search API
            var searchResponse = await _http.GetAsync($"/api/users/search?query=updatedusername");
            searchResponse.EnsureSuccessStatusCode();
            var searchJson = await searchResponse.Content.ReadAsStringAsync();
            StringAssert.Contains(searchJson, "updatedusername");

            // 8. Delete User
            var deleteToken = await GetAntiCsrfToken($"/Users/Delete/{newUserId}"); // Get token from delete page
            var deleteConfirmContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "__RequestVerificationToken", deleteToken }
            });
            var deleteResponse = await _http.PostAsync($"/Users/Delete/{newUserId}", deleteConfirmContent);
            Assert.AreEqual(HttpStatusCode.Found, deleteResponse.StatusCode);
            
            // Verify deletion
            using var scope2 = _server.Services.CreateScope();
            var db2 = scope2.ServiceProvider.GetRequiredService<TemplateDbContext>();
            Assert.IsFalse(db2.Users.Any(u => u.Id == newUserId));
        }
    }
}
