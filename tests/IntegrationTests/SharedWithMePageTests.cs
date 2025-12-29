using System.Net;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using Aiursoft.MarkToHtml.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.MarkToHtml.Tests.IntegrationTests;

[TestClass]
public class SharedWithMePageTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;

    public SharedWithMePageTests()
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

    [TestMethod]
    public async Task SharedWithMe_Page_LoadsAndContainsCorrectLinks()
    {
        // 1. Register and login owner
        var ownerEmail = $"owner-{Guid.NewGuid()}@test.com";
        var password = "Password123!";
        await RegisterAndLogin(ownerEmail, password);
        
        // 2. Create a document as owner
        Guid documentId;
        string ownerId;
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == ownerEmail);
            ownerId = user!.Id;
            
            var doc = new MarkdownDocument
            {
                Id = Guid.NewGuid(),
                Title = "Shared Test Doc",
                Content = "# Content",
                UserId = ownerId,
                CreationTime = DateTime.UtcNow
            };
            db.MarkdownDocuments.Add(doc);
            await db.SaveChangesAsync();
            documentId = doc.Id;
        }

        // 3. Register and login viewer
        var viewerEmail = $"viewer-{Guid.NewGuid()}@test.com";
        await RegisterAndLogin(viewerEmail, password);
        
        string viewerId;
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == viewerEmail);
            viewerId = user!.Id;
        }

        // 4. Share document with viewer as ReadOnly
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var share = new DocumentShare
            {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                SharedWithUserId = viewerId,
                Permission = SharePermission.ReadOnly,
                CreationTime = DateTime.UtcNow
            };
            db.DocumentShares.Add(share);
            await db.SaveChangesAsync();
        }

        // 5. Access SharedWithMe page as viewer
        var response = await _http.GetAsync("/Home/SharedWithMe");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        
        var html = await response.Content.ReadAsStringAsync();
        
        // 6. Verify it contains the correct share link (NOT /view/)
        Assert.IsTrue(html.Contains($"/share/{documentId}"), "Page should contain the correct share link.");
        Assert.IsFalse(html.Contains($"/view/{documentId}"), "Page should NOT contain the outdated /view/ link.");
        
        // 7. Verify the link actually works
        var linkResponse = await _http.GetAsync($"/share/{documentId}");
        Assert.AreEqual(HttpStatusCode.OK, linkResponse.StatusCode);
    }

    private async Task RegisterAndLogin(string email, string password)
    {
        // Get registration page for token
        var regPage = await _http.GetAsync("/Account/Register");
        var regHtml = await regPage.Content.ReadAsStringAsync();
        var token = ExtractToken(regHtml);

        var regContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Email", email },
            { "Password", password },
            { "ConfirmPassword", password },
            { "__RequestVerificationToken", token }
        });
        var regResponse = await _http.PostAsync("/Account/Register", regContent);
        Assert.AreEqual(HttpStatusCode.Found, regResponse.StatusCode);
        
        // Now login
        var loginPage = await _http.GetAsync("/Account/Login");
        var loginHtml = await loginPage.Content.ReadAsStringAsync();
        token = ExtractToken(loginHtml);
        
        var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "EmailOrUserName", email },
            { "Password", password },
            { "__RequestVerificationToken", token }
        });
        var loginResponse = await _http.PostAsync("/Account/Login", loginContent);
        Assert.AreEqual(HttpStatusCode.Found, loginResponse.StatusCode);
    }

    private string ExtractToken(string html)
    {
        var match = System.Text.RegularExpressions.Regex.Match(html,
            @"<input name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)"" />");
        return match.Groups[1].Value;
    }
}
