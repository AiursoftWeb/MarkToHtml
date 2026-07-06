using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;
using Aiursoft.MarkToHtml.Entities;

namespace Aiursoft.MarkToHtml.Models.FolderViewModels;

public class EditFolderViewModel : UiStackLayoutViewModel
{
    public EditFolderViewModel()
    {
        PageTitle = "Edit Folder";
    }

    [Required]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    [Display(Name = "Folder Name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The parent folder currently being browsed. Null = root level.
    /// This becomes the new parent when "Move Here" is clicked.
    /// </summary>
    public int? BrowseParentFolderId { get; set; }

    /// <summary>
    /// The folder currently being browsed (for breadcrumb display).
    /// </summary>
    public MarkdownDocumentFolder? BrowseFolder { get; set; }

    /// <summary>
    /// Subfolders at the current browse level (for drill-down).
    /// </summary>
    public List<MarkdownDocumentFolder> SubFolders { get; set; } = new();

    /// <summary>
    /// Breadcrumb path from root to the currently browsed folder.
    /// </summary>
    public List<MarkdownDocumentFolder> Breadcrumb { get; set; } = new();
}
