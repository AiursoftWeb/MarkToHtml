using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Aiursoft.MarkToHtml.Entities;

/// <summary>
/// A category for organizing markdown documents. Categories are hierarchical (like folders).
/// </summary>
public class Category
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public required string Name { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Parent category for hierarchy. Null means root level.
    /// </summary>
    public int? ParentCategoryId { get; set; }

    [ForeignKey(nameof(ParentCategoryId))]
    public Category? ParentCategory { get; set; }

    public ICollection<Category> SubCategories { get; set; } = new List<Category>();

    [InverseProperty(nameof(DocumentCategory.Category))]
    public ICollection<DocumentCategory> DocumentCategories { get; set; } = new List<DocumentCategory>();

    [StringLength(64)]
    public required string UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    public DateTime CreateTime { get; set; } = DateTime.UtcNow;
}