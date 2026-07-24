namespace Aiursoft.MarkToHtml.Models.AgentViewModels;

public class AdviceViewModel
{
    public Guid AdviceId { get; set; }
    public string ToolDisplayName { get; set; } = string.Empty;
    public string ParameterDisplay { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<DiffHunkViewModel>? DiffHunks { get; set; }
    public string? OldString { get; set; }
    public string? NewString { get; set; }
    public List<ParameterItemViewModel> Parameters { get; set; } = [];
}

public class ParameterItemViewModel
{
    public string Key { get; set; } = string.Empty;
    public string DisplayKey { get; set; } = string.Empty;
    public string? Value { get; set; }
}
