using Aiursoft.UiStack.Layout;
using Aiursoft.MarkToHtml.Entities;

namespace Aiursoft.MarkToHtml.Models.HomeViewModels;

public class MoveViewModel : UiStackLayoutViewModel
{
    public MoveViewModel()
    {
        PageTitle = "Move Document";
    }

    public Guid DocumentId { get; set; }
    public string? DocumentTitle { get; set; }

    /// <summary>
    /// The folder currently being browsed. Null = root level.
    /// </summary>
    public int? BrowseFolderId { get; set; }

    /// <summary>
    /// The currently browsed folder (for breadcrumb display).
    /// </summary>
    public MarkdownDocumentFolder? BrowseFolder { get; set; }

    /// <summary>
    /// Subfolders at the current browse level.
    /// </summary>
    public List<MarkdownDocumentFolder> SubFolders { get; set; } = new();

    /// <summary>
    /// Breadcrumb path from root to parent of browsed folder.
    /// </summary>
    public List<MarkdownDocumentFolder> Breadcrumb { get; set; } = new();
}
