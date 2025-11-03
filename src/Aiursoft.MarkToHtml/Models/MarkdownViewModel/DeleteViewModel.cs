using Aiursoft.UiStack.Layout;
using Aiursoft.MarkToHtml.Entities;

namespace Aiursoft.MarkToHtml.Models.MarkdownViewModels;

public class DeleteViewModel : UiStackLayoutViewModel
{
    public DeleteViewModel()
    {
        PageTitle = "Delete Document";
    }

    // This property will hold the document to be deleted, so the view can display its details.
    public Document Document { get; set; } = null!;
}
