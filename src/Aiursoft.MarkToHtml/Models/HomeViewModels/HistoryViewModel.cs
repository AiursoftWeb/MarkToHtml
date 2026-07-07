using Aiursoft.UiStack.Layout;
using Aiursoft.MarkToHtml.Entities;

namespace Aiursoft.MarkToHtml.Models.HomeViewModels;

public class HistoryViewModel : UiStackLayoutViewModel
{
    public HistoryViewModel()
    {
        PageTitle = "My Documents History";
    }

    public IEnumerable<MarkdownDocument> MyDocuments { get; set; } = new List<MarkdownDocument>();
    public IEnumerable<MarkdownDocumentFolder> SubFolders { get; set; } = new List<MarkdownDocumentFolder>();
    public string? SearchQuery { get; set; }

    /// <summary>
    /// The current folder being browsed. Null means root level.
    /// </summary>
    public int? FolderId { get; set; }
    public MarkdownDocumentFolder? CurrentFolder { get; set; }

    /// <summary>
    /// Breadcrumb path from root to parent of current folder.
    /// </summary>
    public List<MarkdownDocumentFolder> Breadcrumb { get; set; } = new();

    /// <summary>
    /// Item counts per folder: folderId → (documentCount, subFolderCount).
    /// Used to display "3 docs · 2 folders" in the list.
    /// </summary>
    public Dictionary<int, (int DocumentCount, int SubFolderCount)> FolderItemCounts { get; set; } = new();
    public bool UsedAiSearch { get; set; }
    public bool RateLimited { get; set; }
}
