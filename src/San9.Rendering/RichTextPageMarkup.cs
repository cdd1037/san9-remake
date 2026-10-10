using System.Text;
using System.Text.RegularExpressions;

namespace San9.Rendering;

/// <summary>Keep a shaped character range and all style tags from a validated message.</summary>
public static class RichTextPageMarkup
{
    private static readonly Regex Tokens = new(@"\[img[^\]]*\].*?\[/img\]|\[[^\]]+\]|[^\[]+|\[",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    public static string Slice(string markup, int start, int end)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfLessThan(end, start);
        if (start == end) return "";
        var result = new StringBuilder();
        int character = 0;
        void Character(string value)
        {
            if (character >= start && character < end) result.Append(value);
            character++;
        }
        // Styles do not consume characters. Keeping every opening and closing tag
        // preserves their scopes without parsing or rebuilding a second style stack.
        foreach (Match token in Tokens.Matches(markup))
        {
            string value = token.Value;
            if (value.StartsWith("[img", StringComparison.Ordinal) || value is "[lb]" or "[rb]")
                Character(value);
            else if (value.Length > 1 && value[0] == '[')
                result.Append(value);
            else
                foreach (var rune in value.EnumerateRunes())
                    Character(rune.ToString());
        }
        return result.ToString();
    }
}
