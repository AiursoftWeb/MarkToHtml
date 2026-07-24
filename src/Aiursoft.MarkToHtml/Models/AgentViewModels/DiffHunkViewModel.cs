namespace Aiursoft.MarkToHtml.Models.AgentViewModels;

/// <summary>
/// A single diff hunk, mirroring the structuredPatch format from the diff library.
/// Each line in Lines is prefixed with ' ' (context), '-' (removed), or '+' (added).
/// </summary>
public class DiffHunkViewModel
{
    public int OldStart { get; set; }
    public int OldLines { get; set; }
    public int NewStart { get; set; }
    public int NewLines { get; set; }
    public List<string> Lines { get; set; } = [];
}
