namespace Aiursoft.MarkToHtml.Models.PublicViewModels;

/// <summary>
/// A print theme plugin shown in the print settings UI.
/// </summary>
public class PrintThemeViewModel
{
    /// <summary>
    /// The stable theme plugin ID.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The display name shown to users.
    /// </summary>
    public string Name { get; set; } = string.Empty;
}
