using System.Text.RegularExpressions;
using Aiursoft.Scanner.Abstractions;

namespace Aiursoft.MarkToHtml.Services;

public class BadWordFilterService : ISingletonDependency
{
    private readonly Regex _badWordRegex;

    public BadWordFilterService(IWebHostEnvironment env)
    {
        var badWordsPath = Path.Combine(env.ContentRootPath, "badwords.txt");
        var badWords = File.Exists(badWordsPath) 
            ? File.ReadAllLines(badWordsPath)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Trim())
                .ToList()
            : new List<string>();

        // 定义敏感词模式字符串。
        // 注意：原字符串中的 "|" 正好是正则表达式的 "OR" 操作符，所以可以直接作为 Pattern 使用。
        var badWordsPattern = string.Join("|", badWords.Select(Regex.Escape));

        _badWordRegex = new Regex(badWordsPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
    }

    public bool ContainsBadWord(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return _badWordRegex.IsMatch(text);
    }
}