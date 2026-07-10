using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aiursoft.MarkToHtml.Entities;

/// <summary>
/// Junction table linking documents to tags (many-to-many relationship).
/// </summary>
public class DocumentTag
{
    public Guid DocumentId { get; set; }

    [ForeignKey(nameof(DocumentId))]
    public MarkdownDocument? Document { get; set; }

    public int TagId { get; set; }

    [ForeignKey(nameof(TagId))]
    public Tag? Tag { get; set; }
}

public class DocumentTagConfiguration : IEntityTypeConfiguration<DocumentTag>
{
    public void Configure(EntityTypeBuilder<DocumentTag> builder)
    {
        builder.HasKey(dt => new { dt.DocumentId, dt.TagId });

        builder.HasOne(dt => dt.Document)
            .WithMany(d => d.DocumentTags)
            .HasForeignKey(dt => dt.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(dt => dt.Tag)
            .WithMany(t => t.DocumentTags)
            .HasForeignKey(dt => dt.TagId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}