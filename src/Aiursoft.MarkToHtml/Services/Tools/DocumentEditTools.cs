using System.ComponentModel;
using Aiursoft.MarkToHtml.Services.Agent;
using Aiursoft.Scanner.Abstractions;
using ModelContextProtocol.Server;

namespace Aiursoft.MarkToHtml.Services.Tools;

[McpServerToolType]
public class DocumentEditTools(CurrentUserService currentUser) : IScopedDependency
{
    [McpServerTool]
    [Advice]
    [Description(
        "Performs an exact string replacement in the document.\n" +
        "The edit will FAIL if old_string is not unique in the document.\n" +
        "Either provide a larger string with more surrounding context to make it unique.\n\n" +
        "IMPORTANT:\n" +
        "- old_string must include all whitespace, indentation, and line breaks EXACTLY as it appears in the document\n" +
        "- Copy old_string directly from your Read output, but strip the line number prefixes\n" +
        "- old_string must be UNIQUE in the current document\n" +
        "- new_string must be different from old_string\n" +
        "- Propose ONE focused edit per call")]
    public Task<string> ReplaceText(
        [Description("The exact text to replace. Must match the document exactly including all whitespace. Strip line number prefixes.")]
        string oldString,
        [Description("The text to replace it with (must be different from old_string)")]
        string newString)
    {
        var content = currentUser.DocumentContent;

        // Validate old_string and new_string are different
        if (oldString == newString)
            return Task.FromResult("Error: old_string and new_string are exactly the same. No changes to make.");

        // Check if old_string exists in document
        if (string.IsNullOrEmpty(content))
            return Task.FromResult("Error: No document content available.");

        var count = CountOccurrences(content, oldString);
        if (count == 0)
            return Task.FromResult("Error: old_string not found in the document. Re-read the document and ensure you copy the exact text.");
        if (count > 1)
            return Task.FromResult($"Error: old_string appears {count} times in the document. Provide more surrounding context to make it unique.");

        // The actual edit is applied by AgentService when the advice is approved
        return Task.FromResult(
            $"Edit proposed: replace {oldString.Length} characters with {newString.Length} characters " +
            $"({oldString.Split('\n').Length} lines → {newString.Split('\n').Length} lines). " +
            $"Waiting for user approval.");
    }

    private static int CountOccurrences(string text, string search)
    {
        if (string.IsNullOrEmpty(search)) return 0;
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += search.Length;
        }
        return count;
    }
}
