using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Aiursoft.MarkToHtml.Models.AdminViewModels;

public class EditDocumentViewModel : UiStackLayoutViewModel
{
    public Guid DocumentId { get; set; }

    [MaxLength(100)]
    public string? Title { get; set; }

    [Required(ErrorMessage = "Please input your markdown content!")]
    [MaxLength(65535)]
    [Display(Name = "Markdown Content")]
    public string InputMarkdown { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please select the owner of this document!")]
    [Display(Name = "Owner")]
    public string SelectedUserId { get; set; } = string.Empty;

    public List<SelectListItem> AllUsers { get; set; } = new();

    public bool SavedSuccessfully { get; set; }
}
