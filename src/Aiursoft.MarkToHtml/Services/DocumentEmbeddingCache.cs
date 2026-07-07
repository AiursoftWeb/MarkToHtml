using Microsoft.EntityFrameworkCore;
using Aiursoft.MarkToHtml.Entities;

namespace Aiursoft.MarkToHtml.Services;

/// <summary>
/// In-memory cache of document embedding vectors for fast cosine-similarity search.
/// Loaded at startup and refreshed periodically via <see cref="BackgroundJobs.RefreshDocumentEmbeddingCacheJob"/>.
/// Registered as a singleton — thread-safe via an atomic snapshot swap.
/// </summary>
public class DocumentEmbeddingCache
{
    private Dictionary<Guid, float[]> _cache = [];
    private readonly object _lock = new();

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
            .Select(d => new { d.Id, d.Embedding })
            .ToListAsync();

        var newCache = new Dictionary<Guid, float[]>();
        foreach (var item in embeddings)
        {
            var vector = Deserialize(item.Embedding!);
            if (vector != null)
                newCache[item.Id] = vector;
        }

        lock (_lock)
        {
            _cache = newCache;
        }
    }

    private static float[]? Deserialize(byte[] bytes)
    {
        if (bytes.Length % 4 != 0) return null;
        var floats = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
        return floats;
    }
}
