using Aiursoft.UiStack.Layout;
using Aiursoft.MarkToHtml.Entities;

namespace Aiursoft.MarkToHtml.Models.AdminViewModels;

public class AllDocumentsViewModel : UiStackLayoutViewModel
{
    public required List<Document> AllDocuments { get; set; }
}
