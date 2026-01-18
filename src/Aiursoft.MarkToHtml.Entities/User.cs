using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using Newtonsoft.Json;

namespace Aiursoft.MarkToHtml.Entities;

public class User : IdentityUser
{
    public const string DefaultAvatarPath = "avatar/default-avatar.jpg";

    [MaxLength(30)]
    [MinLength(2)]
    public required string DisplayName { get; set; }

    [MaxLength(150)] [MinLength(2)] public string AvatarRelativePath { get; set; } = DefaultAvatarPath;

    public DateTime CreationTime { get; init; } = DateTime.UtcNow;

    [JsonIgnore]
    [InverseProperty(nameof(MarkdownDocument.User))]
    public IEnumerable<MarkdownDocument> CreatedDocuments { get; set; } = new List<MarkdownDocument>();

    [JsonIgnore]
    [InverseProperty(nameof(DocumentShare.SharedWithUser))]
    public IEnumerable<DocumentShare> SharedWithMe { get; init; } = new List<DocumentShare>();
}
