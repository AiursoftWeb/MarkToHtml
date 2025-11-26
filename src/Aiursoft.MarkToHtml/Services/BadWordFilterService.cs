using System.Text.RegularExpressions;
using Aiursoft.Scanner.Abstractions;

namespace Aiursoft.MarkToHtml.Services;

public class BadWordFilterService : ISingletonDependency
{
    private readonly Regex _badWordRegex;

    public BadWordFilterService()
    {
        // 定义敏感词模式字符串。
        // 注意：原字符串中的 "|" 正好是正则表达式的 "OR" 操作符，所以可以直接作为 Pattern 使用。
        var badWordsPattern = "习特勒|习近平|习包子|习禁评|刁迈乎|总烂尾师|共党|共匪|六四|天安门|灭门|Freegate|自由门|无界|吸精瓶|放光明|新唐人|人民报|看中国|明慧|中国禁闻|动态网|李洪志|大纪元|法轮|达赖|郭文贵|郝海东|闫丽梦|墙国|Chinazi|中國各地獨立|武漢肺炎|支那牲人|五毛滾|维尼|維尼|坦克人|武漢病毒|中国病毒|武汉病毒|武汉肺炎|中共病毒|中共国|牆國|支那|ChinaLied|萨格尔王";

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