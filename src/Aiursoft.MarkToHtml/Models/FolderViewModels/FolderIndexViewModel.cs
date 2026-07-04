using Aiursoft.UiStack.Layout;
using Aiursoft.MarkToHtml.Entities;

namespace Aiursoft.MarkToHtml.Models.FolderViewModels;

public class FolderIndexViewModel : UiStackLayoutViewModel
{
    public FolderIndexViewModel()
    {
        PageTitle = "My Documents";
    }

    public int? FolderId { get; set; }
    public MarkdownDocumentFolder? CurrentFolder { get; set; }

    /// <summary>
    /// Breadcrumb path from root to parent of current folder (for navigation).
    /// </summary>
    public List<MarkdownDocumentFolder> Breadcrumb { get; set; } = new();

    public IEnumerable<MarkdownDocumentFolder> SubFolders { get; set; } = new List<MarkdownDocumentFolder>();
    public IEnumerable<MarkdownDocument> Documents { get; set; } = new List<MarkdownDocument>();
    public string? SearchQuery { get; set; }
}
