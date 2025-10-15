using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.MarkToHtml.Entities;

public enum DocumentType
{
    Markdown = 0,
    Mermaid = 1,
    LaTeX = 2
}

public class Document
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(100)]
    public string? Title { get; set; }

    [MaxLength(65535)]
    public string? Content { get; set; }

    public DocumentType DocumentType { get; set; } = DocumentType.Markdown;

    public DateTime CreationTime { get; init; } = DateTime.UtcNow;

    [StringLength(64)]
    public required string UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    [NotNull]
    public User? User { get; set; }
}
