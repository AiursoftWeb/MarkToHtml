using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Aiursoft.MarkToHtml.Entities;
using Aiursoft.MarkToHtml.Util;

namespace Aiursoft.MarkToHtml.Services;

/// <summary>
/// In-memory cache of document embedding vectors for fast cosine-similarity search.
/// Loaded at startup and refreshed periodically via <see cref="BackgroundJobs.RefreshDocumentEmbeddingCacheJob"/>.
/// Registered as a singleton — thread-safe via an atomic snapshot swap.
/// </summary>
[ExcludeFromCodeCoverage]
public class DocumentEmbeddingCache(ILogger<DocumentEmbeddingCache> logger)
{
    internal const int MaxCachedDocumentEmbeddings = 10000;
    private Dictionary<Guid, float[]> _cache = [];
    private readonly Lock _lock = new();

    public int Count
    {
        get { lock (_lock) return _cache.Count; }
    }

    /// <summary>Returns an immutable snapshot of the current cache for a single search run.</summary>
    public Dictionary<Guid, float[]> Snapshot()
    {
        lock (_lock) return new Dictionary<Guid, float[]>(_cache);
    }

    public async Task LoadAsync(TemplateDbContext db)
    {
        var embeddings = await db.MarkdownDocuments
            .AsNoTracking()
            .Where(d => d.Embedding != null)
            .OrderByDescending(d => d.LastEmbeddedAt)
            .Select(d => new { d.Id, d.Embedding })
            .Take(MaxCachedDocumentEmbeddings + 1)
            .ToListAsync();

        if (embeddings.Count > MaxCachedDocumentEmbeddings)
        {
            logger.LogWarning(
                "Embedding cache reached the safety limit of {Limit}. Only the newest embeddings are loaded.",
                MaxCachedDocumentEmbeddings);
            embeddings.RemoveAt(embeddings.Count - 1);
        }

        var newCache = new Dictionary<Guid, float[]>();
        foreach (var item in embeddings)
        {
            var vector = EmbeddingHelper.Deserialize(item.Embedding!);
            if (vector != null)
            {
                newCache[item.Id] = vector;
            }
            else
            {
                logger.LogWarning("Failed to deserialize embedding for document {DocumentId}: byte length {Length} is not a multiple of 4.",
                    item.Id, item.Embedding!.Length);
            }
        }

        lock (_lock)
        {
            _cache = newCache;
        }
    }

}
