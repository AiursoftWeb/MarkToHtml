using Aiursoft.UiStack.Layout;
using Aiursoft.MarkToHtml.Entities;

namespace Aiursoft.MarkToHtml.Models.AdminViewModels;

public class UserDocumentsViewModel : UiStackLayoutViewModel
{
    public required User User { get; set; }
    public required List<MarkdownDocument> UserDocuments { get; set; }
}
