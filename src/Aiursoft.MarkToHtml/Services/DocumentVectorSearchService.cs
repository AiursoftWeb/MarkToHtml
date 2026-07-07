using System.Text;
using Aiursoft.MarkToHtml.Configuration;
using Aiursoft.MarkToHtml.Entities;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace Aiursoft.MarkToHtml.Services;

/// <summary>
/// Semantic vector search for markdown documents using an Ollama-hosted embedding model (e.g. bge-m3).
/// Computes cosine similarity against an in-memory snapshot of pre-computed document embeddings.
/// Caches query embeddings in the database (LRU circular buffer) to avoid redundant model calls.
/// Falls back to classic keyword search when AI search is unavailable or times out.
/// </summary>
public class DocumentVectorSearchService(
    TemplateDbContext db,
    DocumentEmbeddingCache cache,
    GlobalSettingsService settingsService,
    IHttpClientFactory httpClientFactory,
    ILogger<DocumentVectorSearchService> logger)
{
    private const int EmbedTimeoutSeconds = 10;

    internal static readonly TimeSpan AccessThrottle = TimeSpan.FromHours(1);

    // ── Public API ─────────────────────────────────────────────────────────────

    public async Task<(bool UsedAi, List<MarkdownDocument> Results, int TotalCount)> SearchAsync(
        IQueryable<MarkdownDocument> baseQuery,
        string query,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        if (!await ShouldAttemptVectorSearch())
            return (false, [], 0);

        var snapshot = cache.Snapshot();
        if (snapshot.Count == 0)
            return (false, [], 0);

        float[]? queryVector;
        try
        {
            var expectedDimension = snapshot.Values.First().Length;
            queryVector = await EmbedQueryAsync(query, expectedDimension, ct);
        }
        catch
        {
            return (false, [], 0);
        }

        if (queryVector == null)
            return (false, [], 0);

        var scored = new List<(Guid DocumentId, float Score)>();
        var skippedDimensionMismatch = 0;
        foreach (var kv in snapshot)
        {
            if (kv.Value.Length != queryVector.Length)
            {
                skippedDimensionMismatch++;
                continue;
            }

            var score = CosineSimilarity(queryVector, kv.Value);
            if (score > 0)
            {
                scored.Add((kv.Key, score));
            }
        }

        if (scored.Count == 0 && skippedDimensionMismatch > 0)
        {
            logger.LogWarning(
                "Vector search skipped {Count} document embeddings because their dimensions did not match the query vector.",
                skippedDimensionMismatch);
            return (false, [], 0);
        }

        scored = scored
            .OrderByDescending(x => x.Score)
            .ToList();

        var total = scored.Count;
        var topIds = scored
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => x.DocumentId)
            .ToList();

        if (topIds.Count == 0)
            return (true, [], total);

        var docs = await baseQuery
            .Where(d => topIds.Contains(d.Id))
            .Include(d => d.DocumentShares)
            .ToListAsync(ct);

        var docMap = docs.ToDictionary(d => d.Id);
        var ordered = topIds
            .Select(id => docMap.GetValueOrDefault(id))
            .Where(d => d != null)
            .Cast<MarkdownDocument>()
            .ToList();

        return (true, ordered, total);
    }

    /// <summary>Returns the top <paramref name="take"/> documents most similar to <paramref name="documentId"/>.</summary>
    public async Task<List<MarkdownDocument>> GetSimilarDocumentsAsync(
        IQueryable<MarkdownDocument> baseQuery,
        Guid documentId,
        int take,
        CancellationToken ct = default)
    {
        var snapshot = cache.Snapshot();
        if (!snapshot.TryGetValue(documentId, out var targetVector))
            return [];

        var topIds = snapshot
            .Where(kv => kv.Key != documentId)
            .Where(kv => kv.Value.Length == targetVector.Length)
            .Select(kv => (DocumentId: kv.Key, Score: CosineSimilarity(targetVector, kv.Value)))
            .OrderByDescending(x => x.Score)
            .Take(take)
            .Select(x => x.DocumentId)
            .ToList();

        if (topIds.Count == 0)
            return [];

        var docs = await baseQuery
            .Where(d => topIds.Contains(d.Id))
            .Include(d => d.DocumentShares)
            .ToListAsync(ct);

        var docMap = docs.ToDictionary(d => d.Id);
        return topIds
            .Select(id => docMap.GetValueOrDefault(id))
            .Where(d => d != null)
            .Cast<MarkdownDocument>()
            .ToList();
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private async Task<bool> ShouldAttemptVectorSearch()
    {
        var enabled = await settingsService.GetBoolSettingAsync(SettingsMap.EnableEmbeddingBasedSearch);
        if (!enabled) return false;

        var endpoint = await settingsService.GetEmbeddingEndpointAsync();
        if (string.IsNullOrWhiteSpace(endpoint)) return false;

        var model = await settingsService.GetSettingValueAsync(SettingsMap.EmbeddingModel);
        return !string.IsNullOrWhiteSpace(model);
    }

    private async Task<float[]?> EmbedQueryAsync(string text, int expectedDimension, CancellationToken ct)
    {
        // Truncate to column max length for the cache key (the full text is still sent to Ollama).
        var cacheKey = text.Length > 40 ? text[..40] : text;

        // Check DB cache first.
        var cached = await db.SearchEmbeddings
            .FirstOrDefaultAsync(e => e.QueryText == cacheKey, ct);

        if (cached != null)
        {
            var vector = Deserialize(cached.Embedding);
            if (vector != null && vector.Length == expectedDimension)
            {
                var now = DateTime.UtcNow;
                if (now - cached.LastAccessedAt >= AccessThrottle)
                {
                    cached.LastAccessedAt = now;
                    await db.SaveChangesAsync(ct);
                }

                return vector;
            }

            db.SearchEmbeddings.Remove(cached);
            await db.SaveChangesAsync(ct);
        }

        // Compute via Ollama embedding endpoint.
        var endpoint = await settingsService.GetEmbeddingEndpointAsync();
        var model    = await settingsService.GetSettingValueAsync(SettingsMap.EmbeddingModel);
        var token    = await settingsService.GetEmbeddingTokenAsync();

        var http = httpClientFactory.CreateClient();
        var baseUri = new Uri(endpoint);
        var embedUrl = $"{baseUri.Scheme}://{baseUri.Authority}/api/embed?keep_alive=-1";

        var body = new { model, input = text, options = new { num_gpu = 0 } };
        var content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, embedUrl) { Content = content };

        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(EmbedTimeoutSeconds));
        using var linked     = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        var response = await http.SendAsync(request, linked.Token);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(linked.Token);
        if (result?.Embeddings == null || result.Embeddings.Length == 0)
            return null;

        var embedding = result.Embeddings[0];
        Normalize(embedding);

        try
        {
            var now = DateTime.UtcNow;
            db.SearchEmbeddings.Add(new SearchEmbedding
            {
                QueryText      = cacheKey,
                Embedding      = Serialize(embedding),
                CreatedAt      = now,
                LastAccessedAt = now
            });
            await db.SaveChangesAsync(ct);
            await TrimCacheAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Race: another request already cached this query — ignore.
            logger.LogWarning(ex, "Failed to cache query embedding for '{Query}'. Likely a concurrent duplicate.", text);
        }

        return embedding;
    }

    private async Task TrimCacheAsync(CancellationToken ct)
    {
        var limit = await settingsService.GetIntSettingAsync(SettingsMap.EmbeddingQueryCacheLimit);
        if (limit <= 0) limit = 2000;

        var count = await db.SearchEmbeddings.CountAsync(ct);
        if (count <= limit) return;

        var toDelete = await db.SearchEmbeddings
            .OrderBy(e => e.LastAccessedAt)
            .Take(count - limit)
            .ToListAsync(ct);

        if (toDelete.Count > 0)
        {
            db.SearchEmbeddings.RemoveRange(toDelete);
            await db.SaveChangesAsync(ct);
        }
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        var dot = 0f;
        for (var i = 0; i < a.Length; i++)
            dot += a[i] * b[i];
        return dot;
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

    private static float[]? Deserialize(byte[] bytes)
    {
        if (bytes.Length % 4 != 0) return null;
        var floats = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
        return floats;
    }

    private class OllamaEmbedResponse
    {
        [JsonProperty("embeddings")]
        public float[][]? Embeddings { get; set; }
    }
}
