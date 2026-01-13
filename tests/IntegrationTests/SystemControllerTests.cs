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
public class SystemControllerTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;

    public SystemControllerTests()
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
    public async Task AccessSystemIndex_WithoutPermission_ReturnsForbidden()
    {
        // 1. Register User (No special permissions)
        await RegisterAndLoginAsync();

        // 2. Try to access /System/Index
        var response = await _http.GetAsync("/System/Index");
        
        // Should be Forbidden (403) or redirect to Unauthorized (Found 302)
        var location = response.Headers.Location?.ToString();
        Assert.IsTrue(
            response.StatusCode == HttpStatusCode.Forbidden || 
            (response.StatusCode == HttpStatusCode.Found && location?.Contains("Unauthorized") == true),
            $"Expected Forbidden or Redirect to Unauthorized. Got {response.StatusCode}. Location: {location}");
    }

    [TestMethod]
    public async Task AccessSystemIndex_WithPermission_ReturnsOk()
    {
        // 1. Register Admin User
        var (email, password, user) = await RegisterAndLoginAsync();
        
        // 2. Grant Permission
        await GrantPermissionAsync(user, AppPermissionNames.CanViewSystemContext);

        // 3. Re-login to refresh claims
        var logOffToken = await GetAntiCsrfToken("/Manage/ChangePassword"); 
        await _http.PostAsync("/Account/LogOff", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", logOffToken }
        }));
        
        var loginToken = await GetAntiCsrfToken("/Account/Login");
        await _http.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "EmailOrUserName", email },
            { "Password", password },
            { "__RequestVerificationToken", loginToken }
        }));

        // 4. Access /System/Index
        var response = await _http.GetAsync("/System/Index");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task Shutdown_WithPermission_ReturnsAccepted()
    {
         // 1. Register Admin User
        var (email, password, user) = await RegisterAndLoginAsync();
        
        // 2. Grant Permission
        await GrantPermissionAsync(user, AppPermissionNames.CanRebootThisApp);

        // 3. Re-login to refresh claims
        var logOffToken = await GetAntiCsrfToken("/Manage/ChangePassword"); 
        await _http.PostAsync("/Account/LogOff", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", logOffToken }
        }));
        
        var loginToken = await GetAntiCsrfToken("/Account/Login");
        await _http.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "EmailOrUserName", email },
            { "Password", password },
            { "__RequestVerificationToken", loginToken }
        }));

        // 4. Call Shutdown (it's a POST)
        // Need to POST. It's an API? No, it's a Controller Action, likely protected by AntiForgery? 
        // The Controller attribute [ValidateAntiForgeryToken] is NOT on the Shutdown method in the snippet I read.
        // Let's check the code snippet again.
        // [HttpPost]
        // [Authorize(Policy = AppPermissionNames.CanRebootThisApp)] 
        // [ProducesResponseType(StatusCodes.Status202Accepted)]
        // public IActionResult Shutdown(...)
        // No [ValidateAntiForgeryToken] visible in the snippet provided earlier.
        
        var response = await _http.PostAsync("/System/Shutdown", null);
        
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
    }
}
