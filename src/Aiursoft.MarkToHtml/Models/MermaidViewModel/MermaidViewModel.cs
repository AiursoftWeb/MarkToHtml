using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;
using Aiursoft.MarkToHtml.Attributes;

namespace Aiursoft.MarkToHtml.Models.MermaidViewModel;

public class MermaidViewModel : UiStackLayoutViewModel
{
    public MermaidViewModel()
    {
        PageTitle = "Mermaid to HTML Converter";
    }

    [Required(ErrorMessage = "Please input your mermaid content!")]
    [NoBadWords(ErrorMessage = "The diagram content contains sensitive words.")]
    public string InputMermaid { get; set; } = """
                                      graph TD
                                      A[Start] --> B{Is it working?}
                                      B -- Yes --> C[Great]
                                      B -- No  --> D[Fix it]
                                      D --> B
                                      C --> E[Finish]
                                      """;

    public string OutputHtml { get; set; } = string.Empty;

    [Required(ErrorMessage = "Something went wrong, please try again later.")]
    public Guid DocumentId { get; set; } = Guid.NewGuid();

    public bool IsEditing { get; init; }

    [MaxLength(100)]
    [NoBadWords(ErrorMessage = "The diagram title contains sensitive words.")]
    public string? Title { get; set; }

    public bool SavedSuccessfully { get; set; }
}


