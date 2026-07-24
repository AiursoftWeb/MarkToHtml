using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools.Switchable;
using Aiursoft.Scanner;
using Aiursoft.MarkToHtml.Configuration;
using Aiursoft.WebTools.Abstractions.Models;
using Aiursoft.MarkToHtml.InMemory;
using Aiursoft.MarkToHtml.MySql;
using Aiursoft.MarkToHtml.Services.Authentication;
using Aiursoft.MarkToHtml.Sqlite;
using Aiursoft.UiStack.Layout;
using Aiursoft.UiStack.Navigation;
using Ganss.Xss;
using Markdig;
using Microsoft.AspNetCore.Mvc.Razor;
using Aiursoft.ClickhouseLoggerProvider;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Aiursoft.Canon.TaskQueue;
using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.Canon.ScheduledTasks;

namespace Aiursoft.MarkToHtml;

public class Startup : IWebStartup
{
    public void ConfigureServices(IConfiguration configuration, IWebHostEnvironment environment, IServiceCollection services)
    {
        // AppSettings.
        services.Configure<AppSettings>(configuration.GetSection("AppSettings"));
        services.Configure<TtsSettings>(configuration.GetSection("TtsSettings"));

        // Relational database
        var (connectionString, dbType, allowCache) = configuration.GetDbSettings();
        services.AddSwitchableRelationalDatabase(
            dbType: EntryExtends.IsInUnitTests() ? "InMemory" : dbType,
            connectionString: connectionString,
            supportedDbs:
            [
                new MySqlSupportedDb(allowCache: allowCache, splitQuery: false),
                new SqliteSupportedDb(allowCache: allowCache, splitQuery: true),
                new InMemorySupportedDb()
            ]);

        services.AddLogging(builder =>
        {
            builder.AddClickhouse(options => configuration.GetSection("Logging:Clickhouse").Bind(options));
        });

        // Authentication and Authorization
        services.AddTemplateAuth(configuration);

        // Services
        services.AddMemoryCache();
        services.AddHttpClient();
        services.AddAssemblyDependencies(typeof(Startup).Assembly);
        services.AddSingleton<Services.PrintThemeService>();
        services.AddSingleton<NavigationState<Startup>>();

        // Background job queue
        services.AddTaskQueueEngine();
        services.AddScheduledTaskEngine();
        services.RegisterBackgroundJob<Services.BackgroundJobs.DummyJob>();
        var orphanAvatarCleanupJob = services.RegisterBackgroundJob<Services.BackgroundJobs.OrphanAvatarCleanupJob>();
        services.RegisterScheduledTask(registration: orphanAvatarCleanupJob, period: TimeSpan.FromHours(6), startDelay: TimeSpan.FromMinutes(5));
        var orphanMarkdownImageCleanupJob = services.RegisterBackgroundJob<Services.BackgroundJobs.OrphanMarkdownImageCleanupJob>();
        services.RegisterScheduledTask(registration: orphanMarkdownImageCleanupJob, period: TimeSpan.FromHours(6), startDelay: TimeSpan.FromMinutes(10));

        // AI: embedding cache (singleton — shared by all requests for cosine-similarity search)
        services.AddSingleton<Services.DocumentEmbeddingCache>();
        services.AddSingleton<Services.SearchRateLimiter>();
        services.AddScoped<Services.DocumentVectorSearchService>();

        // AI Agent infrastructure
        services.AddSingleton<Services.Agent.IAgentService, Services.Agent.AgentService>();

        // AI: refresh embedding cache from DB into memory (first, so cache is warm)
        var refreshCacheJob = services.RegisterBackgroundJob<Services.BackgroundJobs.RefreshDocumentEmbeddingCacheJob>();
        services.RegisterScheduledTask(registration: refreshCacheJob, period: TimeSpan.FromMinutes(30), startDelay: TimeSpan.FromMinutes(3));

        // AI: embedding generation job — generates/updates float[] vectors (second, so new embeddings get cached soon)
        var generateEmbeddingsJob = services.RegisterBackgroundJob<Services.BackgroundJobs.GenerateDocumentEmbeddingsJob>();
        services.RegisterScheduledTask(registration: generateEmbeddingsJob, period: TimeSpan.FromMinutes(30), startDelay: TimeSpan.FromMinutes(4));

        // Controllers and localization
        services.AddControllersWithViews()
            .AddNewtonsoftJson(options =>
            {
                options.SerializerSettings.DateTimeZoneHandling = DateTimeZoneHandling.Utc;
                options.SerializerSettings.ContractResolver = new DefaultContractResolver();
            })
            .AddApplicationPart(typeof(Startup).Assembly)
            .AddApplicationPart(typeof(UiStackLayoutViewModel).Assembly)
            .AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix)
            .AddDataAnnotationsLocalization();

        // Add the markdown pipeline and HTML sanitizer
        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
        services.AddSingleton(pipeline);
        services.AddSingleton(_ =>
        {
            var sanitizer = new HtmlSanitizer();
            sanitizer.AllowedTags.Add("br");
            sanitizer.AllowedAttributes.Add("class");
            return sanitizer;
        });
    }

    public void Configure(WebApplication app)
    {
        app.UseExceptionHandler("/Error/Code500");
        app.UseStatusCodePagesWithReExecute("/Error/Code{0}");
        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapDefaultControllerRoute();
    }
}
