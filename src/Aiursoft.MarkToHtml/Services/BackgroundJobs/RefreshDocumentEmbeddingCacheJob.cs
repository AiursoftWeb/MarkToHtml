using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.MarkToHtml.Configuration;

namespace Aiursoft.MarkToHtml.Services.BackgroundJobs;

/// <summary>
/// Periodically reloads the in-memory <see cref="DocumentEmbeddingCache"/> from the database.
/// This job only reads — it never calls the embedding model.
/// Decoupled from <see cref="GenerateDocumentEmbeddingsJob"/> so that newly-generated
/// embeddings become visible to search without waiting for the cache schedule.
/// </summary>
public class RefreshDocumentEmbeddingCacheJob(
    Entities.TemplateDbContext db,
    DocumentEmbeddingCache cache,
    GlobalSettingsService settingsService,
    ILogger<RefreshDocumentEmbeddingCacheJob> logger) : IBackgroundJob
{
    public string Name => "Refresh Document Embedding Cache";

    public string Description =>
        "Reloads the in-memory document embedding cache from the database.";

    public async Task ExecuteAsync()
    {
        var enabled = await settingsService.GetBoolSettingAsync(SettingsMap.EnableEmbeddingBasedSearch);
        if (!enabled)
        {
            logger.LogInformation("RefreshDocumentEmbeddingCacheJob: EnableEmbeddingBasedSearch is disabled. Skipping.");
            return;
        }

        await cache.LoadAsync(db);
        logger.LogInformation("RefreshDocumentEmbeddingCacheJob: Cache refreshed. {Count} embeddings loaded.", cache.Count);
    }
}
