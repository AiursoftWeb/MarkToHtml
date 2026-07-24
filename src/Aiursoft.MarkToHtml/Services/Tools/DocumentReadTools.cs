using System.ComponentModel;
using System.Text;
using Aiursoft.MarkToHtml.Services.Agent;
using Aiursoft.Scanner.Abstractions;
using ModelContextProtocol.Server;

namespace Aiursoft.MarkToHtml.Services.Tools;

[McpServerToolType]
public class DocumentReadTools(CurrentUserService currentUser) : IScopedDependency
{
    [McpServerTool]
    [Description("Read the full content of the document with line numbers. " +
                 "Output format: each line is prefixed with 'LINE_NUMBER|' like '     1|# Title'. " +
                 "Use this to see the entire document before making edits. " +
                 "Strip the line number prefix (e.g., '     1|') when copying text for ReplaceText.")]
    public Task<string> ReadFullDocument()
    {
        var content = currentUser.DocumentContent;
        if (string.IsNullOrEmpty(content))
            return Task.FromResult("No document content available. The document may not have been loaded yet.");

        return Task.FromResult(FormatWithLineNumbers(content));
    }

    [McpServerTool]
    [Description("Read a specific line range of the document with line numbers. " +
                 "startLine is 1-based (first line), inclusive. endLine is 1-based, inclusive. " +
                 "Output format: same as ReadFullDocument but limited to the requested range.")]
    public Task<string> ReadDocumentLines(
        [Description("Starting line number (1-based, inclusive)")] int startLine,
        [Description("Ending line number (1-based, inclusive)")] int endLine)
    {
        var content = currentUser.DocumentContent;
        if (string.IsNullOrEmpty(content))
            return Task.FromResult("No document content available.");

        var lines = content.Split('\n');

        // Clamp to valid range
        startLine = Math.Max(1, Math.Min(startLine, lines.Length));
        endLine = Math.Max(startLine, Math.Min(endLine, lines.Length));

        var sb = new StringBuilder();
        var width = lines.Length.ToString().Length;
        for (var i = startLine - 1; i < endLine; i++)
        {
            sb.Append((i + 1).ToString().PadLeft(width));
            sb.Append('|');
            sb.AppendLine(lines[i]);
        }

        return Task.FromResult(sb.ToString().TrimEnd('\n'));
    }

    private static string FormatWithLineNumbers(string content)
    {
        var lines = content.Split('\n');
        var sb = new StringBuilder();
        var width = lines.Length.ToString().Length;
        for (var i = 0; i < lines.Length; i++)
        {
            sb.Append((i + 1).ToString().PadLeft(width));
            sb.Append('|');
            sb.AppendLine(lines[i]);
        }
        return sb.ToString().TrimEnd('\n');
    }
}
