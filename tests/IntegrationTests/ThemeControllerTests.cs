using System.Net;
using System.Text;
using System.Text.Json;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using Aiursoft.MarkToHtml.Entities;
using Microsoft.Extensions.DependencyInjection;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.MarkToHtml.Tests.IntegrationTests;

[TestClass]
public class ThemeControllerTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;

    public ThemeControllerTests()
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
    public async Task SwitchTheme_SetsCookie_Correctly()
    {
        // 1. Switch to Dark
        var darkContent = new StringContent(
            JsonSerializer.Serialize(new { Theme = "dark" }), 
            Encoding.UTF8, 
            "application/json");
            
        var darkResponse = await _http.PostAsync("/api/switch-theme", darkContent);
        darkResponse.EnsureSuccessStatusCode();
        
        Assert.IsTrue(darkResponse.Headers.Contains("Set-Cookie"), "Response should have Set-Cookie header");
        var setCookie = darkResponse.Headers.GetValues("Set-Cookie").FirstOrDefault(c => c.StartsWith("prefer-dark"));
        Assert.IsNotNull(setCookie);
        StringAssert.Contains(setCookie, "prefer-dark=True");

        // 2. Switch to Light
        var lightContent = new StringContent(
            JsonSerializer.Serialize(new { Theme = "light" }), 
            Encoding.UTF8, 
            "application/json");
            
        var lightResponse = await _http.PostAsync("/api/switch-theme", lightContent);
        lightResponse.EnsureSuccessStatusCode();
        
        var setCookieLight = lightResponse.Headers.GetValues("Set-Cookie").FirstOrDefault(c => c.StartsWith("prefer-dark"));
        Assert.IsNotNull(setCookieLight);
        StringAssert.Contains(setCookieLight, "prefer-dark=False");
    }
}
