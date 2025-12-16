using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;
using Aiursoft.MarkToHtml.Attributes;

namespace Aiursoft.MarkToHtml.Models.HomeViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    // ReSharper disable once UnusedMember.Global
    [Obsolete("This constructor is only used for framework!", true)]
    public IndexViewModel()
    {
        PageTitle = "Markdown to HTML Converter";
    }

    public IndexViewModel(string? articleTitle = null)
    {
        if (string.IsNullOrWhiteSpace(articleTitle))
        {
            articleTitle = "Untitled Document";
        }
        PageTitle = $"{articleTitle} - Markdown to HTML Converter";
    }

    [Required(ErrorMessage = "Please input your markdown content!")]
    [NoBadWords(ErrorMessage = "The document content contains sensitive words.")]
    public string InputMarkdown { get; set; } = """
                                                # Hello world!

                                                > Quote

                                                [Link](https://www.aiursoft.com/)

                                                | Month    | Savings |
                                                | -------- | ------- |
                                                | January  | $250    |
                                                | February | $80     |
                                                | March    | $420    |

                                                ```mermaid
                                                graph TD
                                                A[Start] --> B{Is it working?}
                                                B -- Yes --> C[Great]
                                                B -- No  --> D[Fix it]
                                                D --> B
                                                C --> E[Finish]
                                                ```

                                                * **Level 3（底层公理）：** $F = G \frac{m_1 m_2}{r^2}$。

                                                """;

    public string OutputHtml { get; set; } = string.Empty;

    [Required(ErrorMessage = "Something went wrong, please try again later.")]
    public Guid DocumentId { get; set; } = Guid.NewGuid();

    public bool IsEditing { get; init; }

    [MaxLength(100)]
    [NoBadWords(ErrorMessage = "The document title contains sensitive words.")]
    public string? Title { get; set; }

    /// <summary>
    /// Public ID for sharing the document. Generated when the document is made public.
    /// </summary>
    public Guid? PublicId { get; set; }

    /// <summary>
    /// Indicates whether the document is publicly accessible.
    /// </summary>
    public bool IsPublic => PublicId.HasValue;

    /// <summary>
    /// The public link to view this document.
    /// </summary>
    public string? PublicLink { get; set; }


    public bool SavedSuccessfully { get; set; }
}
