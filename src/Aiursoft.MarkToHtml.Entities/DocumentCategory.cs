using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aiursoft.MarkToHtml.Entities;

/// <summary>
/// Junction table linking documents to categories (many-to-many relationship).
/// </summary>
public class DocumentCategory
{
    public Guid DocumentId { get; set; }

    [ForeignKey(nameof(DocumentId))]
    public MarkdownDocument? Document { get; set; }

    public int CategoryId { get; set; }

    [ForeignKey(nameof(CategoryId))]
    public Category? Category { get; set; }
}

public class DocumentCategoryConfiguration : IEntityTypeConfiguration<DocumentCategory>
{
    public void Configure(EntityTypeBuilder<DocumentCategory> builder)
    {
        builder.HasKey(dc => new { dc.DocumentId, dc.CategoryId });

        builder.HasOne(dc => dc.Document)
            .WithMany(d => d.DocumentCategories)
            .HasForeignKey(dc => dc.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(dc => dc.Category)
            .WithMany(c => c.DocumentCategories)
            .HasForeignKey(dc => dc.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}