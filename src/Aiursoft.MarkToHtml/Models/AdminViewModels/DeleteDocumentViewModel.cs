using Aiursoft.UiStack.Layout;
using Aiursoft.MarkToHtml.Entities;

namespace Aiursoft.MarkToHtml.Models.AdminViewModels;

public class DeleteDocumentViewModel : UiStackLayoutViewModel
{
    public DeleteDocumentViewModel()
    {
        PageTitle = "Delete Document";
    }

    public required MarkdownDocument Document { get; set; }
}
