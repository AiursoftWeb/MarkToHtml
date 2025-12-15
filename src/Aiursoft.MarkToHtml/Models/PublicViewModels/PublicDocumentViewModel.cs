using Aiursoft.UiStack.Layout;

namespace Aiursoft.MarkToHtml.Models.PublicViewModels;

/// <summary>
/// View model for displaying publicly shared documents.
/// </summary>
public class PublicDocumentViewModel : UiStackLayoutViewModel
{
    public PublicDocumentViewModel(string articleTitle)
    {
        PageTitle = $"{articleTitle} - Shared Document";
    }

    /// <summary>
    /// The title of the document.
    /// </summary>
    public string DocumentTitle { get; set; } = "Untitled Document";

    /// <summary>
    /// The rendered HTML content of the document.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// The raw Markdown content of the document.
    /// </summary>
    public string MarkdownContent { get; set; } = string.Empty;

    /// <summary>
    /// The name of the author who created and shared this document.
    /// </summary>
    public string AuthorName { get; set; } = "Unknown Author";

    /// <summary>
    /// The time when the document was created.
    /// </summary>
    public DateTime CreationTime { get; set; }
}
