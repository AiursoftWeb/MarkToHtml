using System.Net;
using Aiursoft.MarkToHtml.Configuration;
using Aiursoft.MarkToHtml.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Aiursoft.MarkToHtml.Services;

namespace Aiursoft.MarkToHtml.Tests.IntegrationTests;

[TestClass]
public class GlobalSettingsTests : TestBase
{
    [TestMethod]
    public async Task TestAllowUserAdjustNicknameSetting()
    {
        // 1. Login as admin
        await LoginAsAdmin();

        // 2. Disable Allow_User_Adjust_Nickname
        using (var scope = Server!.Services.CreateScope())
        {
            var settingsService = scope.ServiceProvider.GetRequiredService<GlobalSettingsService>();
            await settingsService.UpdateSettingAsync(SettingsMap.AllowUserAdjustNickname, "False");
        }

        // 3. Verify that the "Change your profile" link is NOT visible on Manage/Index
        var manageIndexResponse = await Http.GetAsync("/Manage/Index");
        var manageIndexHtml = await manageIndexResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Change your profile", manageIndexHtml);

        // 4. Verify that accessing /Manage/ChangeProfile directly returns BadRequest
        var changeProfileResponse = await Http.GetAsync("/Manage/ChangeProfile");
        Assert.AreEqual(HttpStatusCode.BadRequest, changeProfileResponse.StatusCode);

        // 5. Enable Allow_User_Adjust_Nickname
        using (var scope = Server!.Services.CreateScope())
        {
            var settingsService = scope.ServiceProvider.GetRequiredService<GlobalSettingsService>();
            await settingsService.UpdateSettingAsync(SettingsMap.AllowUserAdjustNickname, "True");
        }

        // 6. Verify that the "Change your profile" link IS visible on Manage/Index
        manageIndexResponse = await Http.GetAsync("/Manage/Index");
        manageIndexHtml = await manageIndexResponse.Content.ReadAsStringAsync();
        Assert.Contains("Change your profile", manageIndexHtml);

        // 7. Verify that accessing /Manage/ChangeProfile directly returns OK
        changeProfileResponse = await Http.GetAsync("/Manage/ChangeProfile");
        Assert.AreEqual(HttpStatusCode.OK, changeProfileResponse.StatusCode);
    }

    [TestMethod]
    public async Task TestAdminManageSettings()
    {
        // 1. Login as admin
        await LoginAsAdmin();

        // 2. Access Global Settings Index
        var settingsResponse = await Http.GetAsync("/GlobalSettings/Index");
        settingsResponse.EnsureSuccessStatusCode();
        var settingsHtml = await settingsResponse.Content.ReadAsStringAsync();
        Assert.Contains("Global Settings", settingsHtml);
        Assert.Contains(SettingsMap.AllowUserAdjustNickname, settingsHtml);

        // 3. Change setting via UI
        var editResponse = await PostForm("/GlobalSettings/Edit", new Dictionary<string, string>
        {
            { "Key", SettingsMap.AllowUserAdjustNickname },
            { "Value", "False" }
        }, tokenUrl: "/GlobalSettings/Index");
        Assert.AreEqual(HttpStatusCode.Found, editResponse.StatusCode);

        // 4. Verify setting changed in DB
        using (var scope = Server!.Services.CreateScope())
        {
            var settingsService = scope.ServiceProvider.GetRequiredService<GlobalSettingsService>();
            var value = await settingsService.GetBoolSettingAsync(SettingsMap.AllowUserAdjustNickname);
            Assert.IsFalse(value);
        }
    }

    [TestMethod]
    public async Task SecretSettingIsHiddenAndCanBeReplaced()
    {
        await LoginAsAdmin();

        using (var scope = Server!.Services.CreateScope())
        {
            var settingsService = scope.ServiceProvider.GetRequiredService<GlobalSettingsService>();
            await settingsService.UpdateSettingAsync(SettingsMap.EmbeddingApiToken, "secret-token");
        }

        var settingsResponse = await Http.GetAsync("/GlobalSettings/Index");
        settingsResponse.EnsureSuccessStatusCode();
        var settingsHtml = await settingsResponse.Content.ReadAsStringAsync();
        Assert.Contains("Configured - enter a new value to replace", settingsHtml);
        Assert.DoesNotContain("secret-token", settingsHtml);

        var replaceResponse = await PostForm("/GlobalSettings/Edit", new Dictionary<string, string>
        {
            { "Key", SettingsMap.EmbeddingApiToken },
            { "Value", "replacement-token" }
        }, tokenUrl: "/GlobalSettings/Index");
        Assert.AreEqual(HttpStatusCode.Found, replaceResponse.StatusCode);

        using (var scope = Server!.Services.CreateScope())
        {
            var settingsService = scope.ServiceProvider.GetRequiredService<GlobalSettingsService>();
            var value = await settingsService.GetEmbeddingTokenAsync();
            Assert.AreEqual("replacement-token", value);
        }
    }

    [TestMethod]
    public async Task SecretSettingCanBeClearedByEmptyPost()
    {
        await LoginAsAdmin();

        using (var scope = Server!.Services.CreateScope())
        {
            var settingsService = scope.ServiceProvider.GetRequiredService<GlobalSettingsService>();
            await settingsService.UpdateSettingAsync(SettingsMap.EmbeddingApiToken, "secret-token");
        }

        var clearResponse = await PostForm("/GlobalSettings/Edit", new Dictionary<string, string>
        {
            { "Key", SettingsMap.EmbeddingApiToken },
            { "Value", "" }
        }, tokenUrl: "/GlobalSettings/Index");
        Assert.AreEqual(HttpStatusCode.Found, clearResponse.StatusCode);

        using (var scope = Server!.Services.CreateScope())
        {
            var settingsService = scope.ServiceProvider.GetRequiredService<GlobalSettingsService>();
            var value = await settingsService.GetEmbeddingTokenAsync();
            Assert.AreEqual(string.Empty, value);
        }
    }

    [TestMethod]
    public async Task LegacySecretsRemainReadableWithoutRewritingStoredValues()
    {
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<GlobalSettingsService>();
        var values = new Dictionary<string, string>
        {
            [SettingsMap.AgentApiToken] = "legacy-agent-token",
            [SettingsMap.EmbeddingApiToken] = "legacy-embedding-token",
            [SettingsMap.Icp] = "protected:v1:ordinary-text"
        };
        foreach (var pair in values)
        {
            var row = await db.GlobalSettings.FirstAsync(s => s.Key == pair.Key);
            row.Value = pair.Value;
        }
        await db.SaveChangesAsync();

        foreach (var pair in values)
        {
            Assert.AreEqual(pair.Value, await service.GetSettingValueAsync(pair.Key));
        }
        Assert.AreEqual("legacy-embedding-token", await service.GetEmbeddingTokenAsync());
        await service.SeedSettingsAsync();
        foreach (var pair in values)
        {
            var storedValue = await db.GlobalSettings.AsNoTracking()
                .Where(s => s.Key == pair.Key).Select(s => s.Value).SingleAsync();
            Assert.AreEqual(pair.Value, storedValue);
        }
    }

    [TestMethod]
    public async Task SavedSecretsAreEncryptedAndBusinessReadersReceiveOriginalTokens()
    {
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<GlobalSettingsService>();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("GlobalSettings/Secrets/v1");
        foreach (var key in new[] { SettingsMap.AgentApiToken, SettingsMap.EmbeddingApiToken })
        {
            var token = $"saved-{key}";
            await service.UpdateSettingAsync(key, token);
            var storedValue = await db.GlobalSettings.AsNoTracking()
                .Where(s => s.Key == key).Select(s => s.Value).SingleAsync();
            Assert.IsNotNull(storedValue);
            Assert.StartsWith("protected:v1:", storedValue);
            Assert.AreEqual(token, protector.Unprotect(storedValue["protected:v1:".Length..]));
            using var readScope = Server.Services.CreateScope();
            var reader = readScope.ServiceProvider.GetRequiredService<GlobalSettingsService>();
            Assert.AreEqual(token, await reader.GetSettingValueAsync(key));
        }
        Assert.AreEqual($"saved-{SettingsMap.EmbeddingApiToken}", await service.GetEmbeddingTokenAsync());
        await service.UpdateSettingAsync(SettingsMap.EmbeddingApiToken, string.Empty);
        Assert.AreEqual($"saved-{SettingsMap.AgentApiToken}", await service.GetEmbeddingTokenAsync());
        Assert.AreEqual(string.Empty, await db.GlobalSettings.AsNoTracking()
            .Where(s => s.Key == SettingsMap.EmbeddingApiToken).Select(s => s.Value).SingleAsync());
    }

    [TestMethod]
    public async Task SecretConfigurationOverrideIsHiddenReadOnlyAndNotSeededIntoDatabase()
    {
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<GlobalSettingsService>();
        var key = SettingsMap.AgentApiToken;
        var configKey = $"GlobalSettings:{key}";
        var original = configuration[configKey];
        configuration[configKey] = "external-agent-token";
        try
        {
            db.GlobalSettings.Remove(await db.GlobalSettings.SingleAsync(s => s.Key == key));
            await db.SaveChangesAsync();
            await service.SeedSettingsAsync();
            Assert.AreEqual(string.Empty, await db.GlobalSettings.AsNoTracking()
                .Where(s => s.Key == key).Select(s => s.Value).SingleAsync());
            Assert.AreEqual("external-agent-token", await service.GetSettingValueAsync(key));
            Assert.IsTrue(service.IsOverriddenByConfig(key));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => service.UpdateSettingAsync(key, "replacement-token"));
            var response = await Http.GetAsync("/GlobalSettings/Index");
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("external-agent-token", html);
            Assert.Contains("Configured", html);
        }
        finally
        {
            configuration[configKey] = original;
        }
    }

    [TestMethod]
    public async Task UnreadableEncryptedSecretReturnsEmptyWithoutOverwritingStoredValue()
    {
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<GlobalSettingsService>();
        var row = await db.GlobalSettings.SingleAsync(s => s.Key == SettingsMap.AgentApiToken);
        row.Value = "protected:v1:invalid-ciphertext";
        await db.SaveChangesAsync();
        Assert.AreEqual(string.Empty, await service.GetSettingValueAsync(SettingsMap.AgentApiToken));
        Assert.AreEqual("protected:v1:invalid-ciphertext", await db.GlobalSettings.AsNoTracking()
            .Where(s => s.Key == SettingsMap.AgentApiToken).Select(s => s.Value).SingleAsync());
    }

    [TestMethod]
    public async Task TestGlobalSettingsServiceValidation()
    {
        using var scope = Server!.Services.CreateScope();
        var settingsService = scope.ServiceProvider.GetRequiredService<GlobalSettingsService>();

        // Test non-defined setting
        try
        {
            await settingsService.UpdateSettingAsync("InvalidKey", "Value");
            Assert.Fail("Should have thrown InvalidOperationException");
        }
        catch (InvalidOperationException) { }

        // Test Bool validation
        try
        {
            await settingsService.UpdateSettingAsync(SettingsMap.AllowUserAdjustNickname, "NotABool");
            Assert.Fail("Should have thrown InvalidOperationException");
        }
        catch (InvalidOperationException) { }
    }
}
