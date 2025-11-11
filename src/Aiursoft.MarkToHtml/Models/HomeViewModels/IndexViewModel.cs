using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;
using Aiursoft.MarkToHtml.Attributes;

namespace Aiursoft.MarkToHtml.Models.HomeViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "Markdown to HTML Converter";
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

                                                """;

    public string OutputHtml { get; set; } = string.Empty;

    [Required(ErrorMessage = "Something went wrong, please try again later.")]
    public Guid DocumentId { get; set; } = Guid.NewGuid();

    public bool IsEditing { get; init; }

    [MaxLength(100)]
    [NoBadWords(ErrorMessage = "The document title contains sensitive words.")]
    public string? Title { get; set; }

    public bool SavedSuccessfully { get; set; }
}
