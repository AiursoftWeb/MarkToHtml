using System.Net;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using Aiursoft.MarkToHtml.Entities;
using Microsoft.EntityFrameworkCore;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.MarkToHtml.Tests.IntegrationTests;

[TestClass]
public class ContractTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;

    public ContractTests()
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
    public async Task Contract_FillPage_Loads()
    {
        var email = $"test-{Guid.NewGuid()}@test.com";
        var password = "Password123!";
        await RegisterAndLogin(email, password);
        
        Guid documentId;
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
            var doc = new MarkdownDocument
            {
                Id = Guid.NewGuid(),
                Title = "Contract Test Doc",
                Content = "# Clause 1",
                UserId = user!.Id,
                CreationTime = DateTime.UtcNow
            };
            db.MarkdownDocuments.Add(doc);
            await db.SaveChangesAsync();
            documentId = doc.Id;
        }

        var response = await _http.GetAsync($"/contract/{documentId}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Contract Information", html);
    }

    [TestMethod]
    public async Task Contract_Generate_Works()
    {
        var email = $"test-{Guid.NewGuid()}@test.com";
        var password = "Password123!";
        await RegisterAndLogin(email, password);
        
        Guid documentId;
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
            var doc = new MarkdownDocument
            {
                Id = Guid.NewGuid(),
                Title = "Contract Test Doc",
                Content = "# Clause 1",
                UserId = user!.Id,
                CreationTime = DateTime.UtcNow
            };
            db.MarkdownDocuments.Add(doc);
            await db.SaveChangesAsync();
            documentId = doc.Id;
        }

        var fillPageResponse = await _http.GetAsync($"/contract/{documentId}");
        var fillPageHtml = await fillPageResponse.Content.ReadAsStringAsync();
        var token = ExtractToken(fillPageHtml);

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "DocumentId", documentId.ToString() },
            { "ContractNumber", "TEST-001" },
            { "SignDate", "2026-01-20" },
            { "SignLocation", "Suzhou" },
            { "PartyAName", "Party A" },
            { "PartyAAddress", "Address A" },
            { "PartyAContact", "Contact A" },
            { "PartyBName", "Party B" },
            { "PartyBAddress", "Address B" },
            { "PartyBContact", "Contact B" },
            { "__RequestVerificationToken", token }
        });

        var response = await _http.PostAsync($"/contract/{documentId}", content);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("TEST-001", html);
    }

    private async Task RegisterAndLogin(string email, string password)
    {
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
        await _http.PostAsync("/Account/Register", regContent);
        
        var loginPage = await _http.GetAsync("/Account/Login");
        var loginHtml = await loginPage.Content.ReadAsStringAsync();
        token = ExtractToken(loginHtml);
        var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "EmailOrUserName", email },
            { "Password", password },
            { "__RequestVerificationToken", token }
        });
        await _http.PostAsync("/Account/Login", loginContent);
    }

    private string ExtractToken(string html)
    {
        var match = System.Text.RegularExpressions.Regex.Match(html, @"<input name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)"" />");
        return match.Groups[1].Value;
    }
}
