using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;
using Aiursoft.MarkToHtml.Attributes;

namespace Aiursoft.MarkToHtml.Models.HomeViewModels;

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
}


