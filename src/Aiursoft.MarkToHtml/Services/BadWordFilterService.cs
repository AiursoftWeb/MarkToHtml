using Aiursoft.Scanner.Abstractions;

namespace Aiursoft.MarkToHtml.Services;

public class BadWordFilterService : ISingletonDependency
{
    // In a real-world application, this list might come from a database or a configuration file.
    private readonly HashSet<string> _badWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "badword",
        "sensitive"
    };

    public bool ContainsBadWord(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return _badWords.Any(badWord => text.Contains(badWord, StringComparison.OrdinalIgnoreCase));
    }
}
