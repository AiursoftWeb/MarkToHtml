using System.Text;
using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.MarkToHtml.Configuration;
using Aiursoft.MarkToHtml.Entities;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace Aiursoft.MarkToHtml.Services.BackgroundJobs;

/// <summary>
/// Generates embedding vectors for markdown documents using the configured Ollama embedding model.
/// Processes documents where <see cref="MarkdownDocument.LastEmbeddedAt"/> is older than
/// <see cref="MarkdownDocument.UpdatedAt"/> (i.e. content changed since last embedding).
/// </summary>
public class GenerateDocumentEmbeddingsJob(
    TemplateDbContext db,
    GlobalSettingsService settingsService,
    IHttpClientFactory httpClientFactory,
    ILogger<GenerateDocumentEmbeddingsJob> logger) : IBackgroundJob
{
    internal const int MaxDocumentsPerRun = 50;
    private static readonly SemaphoreSlim RunLock = new(1, 1);

    public string Name => "Generate Document Embeddings";

    public string Description =>
        "Generates embedding vectors for markdown documents using the configured Ollama embedding model. " +
        "Processes documents where LastEmbeddedAt is older than UpdatedAt " +
        "(content changed since last embedding). " +
        "Embedding vectors are stored as serialized float[] in MarkdownDocument.Embedding.";

    public async Task ExecuteAsync()
    {
        if (!await RunLock.WaitAsync(0))
        {
            logger.LogInformation("GenerateDocumentEmbeddingsJob: previous run is still active. Skipping.");
            return;
        }

        try
        {
            await ExecuteCoreAsync();
        }
        finally
        {
            RunLock.Release();
        }
    }

    private async Task ExecuteCoreAsync()
    {
        if (!await settingsService.IsAiSearchEnabledAsync())
        {
            logger.LogInformation("GenerateDocumentEmbeddingsJob: Embedding endpoint not configured. Skipping.");
            return;
        }

        var enabled = await settingsService.GetBoolSettingAsync(SettingsMap.EnableEmbeddingBasedSearch);
        if (!enabled)
        {
            logger.LogInformation("GenerateDocumentEmbeddingsJob: EnableEmbeddingBasedSearch is disabled. Skipping.");
            return;
        }

        var model = await settingsService.GetSettingValueAsync(SettingsMap.EmbeddingModel);
        if (string.IsNullOrWhiteSpace(model))
        {
            logger.LogInformation("GenerateDocumentEmbeddingsJob: EmbeddingModel not configured. Skipping.");
            return;
        }

        var endpoint = await settingsService.GetEmbeddingEndpointAsync();
        var token    = await settingsService.GetEmbeddingTokenAsync();

        var lastId = Guid.Empty;
        var attempted = 0;
        var succeeded = 0;
        while (true)
        {
            if (attempted >= MaxDocumentsPerRun)
            {
                logger.LogInformation(
                    "GenerateDocumentEmbeddingsJob: attempted {Count} documents, stopping until next run.",
                    attempted);
                break;
            }

            var currentLastId = lastId;
            var take = Math.Min(10, MaxDocumentsPerRun - attempted);
            var pending = await db.MarkdownDocuments
                .Where(d => d.Id.CompareTo(currentLastId) > 0 &&
                            (d.Embedding == null || d.LastEmbeddedAt < d.UpdatedAt))
                .OrderBy(d => d.Id)
                .Take(take)
                .ToListAsync();

            if (pending.Count == 0) break;

            foreach (var doc in pending)
            {
                attempted++;
                try
                {
                    var sourceUpdatedAt = doc.UpdatedAt;
                    var embedding = await CallEmbedApiAsync(endpoint, model, token, doc);
                    if (await TrySaveEmbeddingIfDocumentUnchangedAsync(db, doc, sourceUpdatedAt, embedding))
                    {
                        succeeded++;
                    }
                    else
                    {
                        logger.LogInformation(
                            "GenerateDocumentEmbeddingsJob: document '{Title}' (id={Id}) changed while embedding was running. Skipping stale result.",
                            doc.Title, doc.Id);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "GenerateDocumentEmbeddingsJob: failed for '{Title}' (id={Id}).",
                        doc.Title, doc.Id);
                }
            }

            lastId = pending.Max(d => d.Id);
        }

        logger.LogInformation(
            "GenerateDocumentEmbeddingsJob: done. {Succeeded}/{Attempted} documents processed.",
            succeeded, attempted);
    }

    private async Task<float[]> CallEmbedApiAsync(string endpoint, string model, string token, MarkdownDocument doc)
    {
        var text = BuildDocumentText(doc);
        var http = httpClientFactory.CreateClient();

        var baseUri  = new Uri(endpoint);
        var embedUrl = $"{baseUri.Scheme}://{baseUri.Authority}/api/embed?keep_alive=-1";

        // num_gpu=0 forces CPU-only embedding so it never competes with any LLM for VRAM.
        var body    = new { model, input = text, options = new { num_gpu = 0 } };
        var content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, embedUrl) { Content = content };

        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var response = await http.SendAsync(request, timeoutCts.Token);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>();
        if (result?.Embeddings == null || result.Embeddings.Length == 0)
            throw new InvalidOperationException($"Ollama returned no embeddings for document '{doc.Title}'.");

        var vector = result.Embeddings[0];
        Normalize(vector);
        return vector;
    }

    private static string BuildDocumentText(MarkdownDocument doc)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(doc.Title))   sb.AppendLine(doc.Title);
        if (!string.IsNullOrWhiteSpace(doc.Content)) sb.AppendLine(doc.Content);
        return sb.ToString();
    }

    internal static async Task<bool> TrySaveEmbeddingIfDocumentUnchangedAsync(
        TemplateDbContext db,
        MarkdownDocument doc,
        DateTime sourceUpdatedAt,
        float[] embedding)
    {
        var serialized = Serialize(embedding);
        if (db.Database.IsRelational())
        {
            var updated = await db.MarkdownDocuments
                .Where(d => d.Id == doc.Id && d.UpdatedAt == sourceUpdatedAt)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(d => d.Embedding, serialized)
                    .SetProperty(d => d.LastEmbeddedAt, sourceUpdatedAt));
            return updated == 1;
        }

        await db.Entry(doc).ReloadAsync();
        if (db.Entry(doc).State == EntityState.Detached || doc.UpdatedAt != sourceUpdatedAt)
        {
            return false;
        }

        doc.Embedding      = serialized;
        doc.LastEmbeddedAt = sourceUpdatedAt;
        await db.SaveChangesAsync();
        return true;
    }

    private static void Normalize(float[] v)
    {
        var sumSq = 0f;
        foreach (var x in v) sumSq += x * x;
        var norm = MathF.Sqrt(sumSq);
        if (norm > 0)
            for (var i = 0; i < v.Length; i++)
                v[i] /= norm;
    }

    private static byte[] Serialize(float[] v)
    {
        var bytes = new byte[v.Length * 4];
        Buffer.BlockCopy(v, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private class OllamaEmbedResponse
    {
        [JsonProperty("embeddings")]
        public float[][]? Embeddings { get; set; }
    }
}
