using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Aiursoft.MarkToHtml.Entities;

/// <summary>
/// A tag for labeling markdown documents. Tags are simple labels without hierarchy.
/// </summary>
public class Tag
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public required string Name { get; set; }

    /// <summary>
    /// Optional color for display purposes (e.g., "red", "blue", "#ff5722").
    /// </summary>
    [MaxLength(20)]
    public string? Color { get; set; }

    [InverseProperty(nameof(DocumentTag.Tag))]
    public ICollection<DocumentTag> DocumentTags { get; set; } = new List<DocumentTag>();

    [StringLength(64)]
    public required string UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    public DateTime CreateTime { get; set; } = DateTime.UtcNow;
}