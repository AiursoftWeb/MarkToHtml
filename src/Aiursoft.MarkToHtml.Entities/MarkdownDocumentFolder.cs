using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Aiursoft.MarkToHtml.Entities;

public class MarkdownDocumentFolder
{
    [Key]
    public int Id { get; set; }

    [MaxLength(200)]
    public required string Name { get; set; }

    public int? ParentFolderId { get; set; }
    [ForeignKey(nameof(ParentFolderId))]
    public MarkdownDocumentFolder? ParentFolder { get; set; }

    public ICollection<MarkdownDocumentFolder> SubFolders { get; set; } = new List<MarkdownDocumentFolder>();
    public ICollection<MarkdownDocument> MarkdownDocuments { get; set; } = new List<MarkdownDocument>();

    [StringLength(64)]
    public required string UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    public DateTime CreateTime { get; set; } = DateTime.UtcNow;
}
