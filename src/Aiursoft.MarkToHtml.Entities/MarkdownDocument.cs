using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.MarkToHtml.Entities;

public class MarkdownDocument
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(100)]
    public string? Title { get; set; }

    [MaxLength(262144)]
    public string? Content { get; set; }

    public DateTime CreationTime { get; init; } = DateTime.UtcNow;

    [StringLength(64)]
    public required string UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    [NotNull]
    public User? User { get; set; }

    /// <summary>
    /// Whether the document is public for everyone to view.
    /// </summary>
    public bool IsPublic { get; set; }

    public int? FolderId { get; set; }
    [ForeignKey(nameof(FolderId))]
    public MarkdownDocumentFolder? Folder { get; set; }

    /// <summary>
    /// Updated whenever Title or Content changes. Used to detect stale embeddings.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Serialized float[] embedding vector (4 bytes × N dims, little-endian).
    /// Null until the embedding background job processes this document.
    /// </summary>
    public byte[]? Embedding { get; set; }

    /// <summary>
    /// When the current Embedding was generated. The embedding job re-runs when
    /// UpdatedAt is newer than this value.
    /// </summary>
    public DateTime LastEmbeddedAt { get; set; } = DateTime.MinValue;

    [InverseProperty(nameof(DocumentShare.Document))]
    public IEnumerable<DocumentShare> DocumentShares { get; init; } = new List<DocumentShare>();
}
