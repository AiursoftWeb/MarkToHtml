using System.ComponentModel.DataAnnotations;

namespace Aiursoft.MarkToHtml.Models.ManageViewModels;

public class SwitchThemeViewModel
{
    [Required]
    public required string Theme { get; set; }
}
