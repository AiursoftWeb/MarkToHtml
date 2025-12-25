using Aiursoft.MarkToHtml.Entities;
using Aiursoft.UiStack.Layout;
using Microsoft.AspNetCore.Identity;

namespace Aiursoft.MarkToHtml.Models.HomeViewModels;

public class ManageSharesViewModel : UiStackLayoutViewModel
{
    public ManageSharesViewModel(string pageTitle)
    {
        PageTitle = pageTitle;
    }

    public required Guid DocumentId { get; init; }
    public required string DocumentTitle { get; init; }
    public required bool IsPublic { get; init; }
    public string? PublicLink { get; init; }
    public required List<DocumentShare> ExistingShares { get; init; }
    public required List<IdentityRole> AvailableRoles { get; init; }
}
