using Aiursoft.UiStack.Layout;

namespace Aiursoft.MarkToHtml.Models.FolderViewModels;

public class DeleteFolderViewModel : UiStackLayoutViewModel
{
    public DeleteFolderViewModel()
    {
        PageTitle = "Delete Folder";
    }

    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? ParentFolderId { get; set; }

    /// <summary>
    /// Direct child count (non-recursive).
    /// </summary>
    public int DirectDocumentCount { get; set; }
    public int DirectSubFolderCount { get; set; }

    /// <summary>
    /// Recursive total counts — everything that will be deleted.
    /// </summary>
    public int RecursiveDocumentCount { get; set; }
    public int RecursiveSubFolderCount { get; set; }
}
