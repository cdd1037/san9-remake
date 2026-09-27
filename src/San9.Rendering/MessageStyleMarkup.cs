using System.Text.RegularExpressions;
using San9.Content.Text;

namespace San9.Rendering;

/// <summary>Translate validated preset scopes into native BBCode at the display boundary.</summary>
public static class MessageStyleMarkup
{
    private static readonly Regex Tags = new(MessageStyles.TagPattern, RegexOptions.CultureInvariant);
    public static (string BbCode, bool HasPresets) Expand(string markup)
    {
        MessageStyles.ValidateMarkup(markup);
        bool found = false;
        var scopes = new Stack<MessageStylePreset>();
        string result = Tags.Replace(markup, match =>
        {
            found = true;
            if (match.Groups["id"].Success)
            {
                var preset = MessageStyles.Get(match.Groups["id"].Value);
                scopes.Push(preset);
                string foreground = preset.Foreground is null ? "" : $"[color=#{preset.Foreground}]";
                return $"{foreground}[outline_size={TextOutlineStyle.Size}][outline_color=#{preset.Outline}{TextOutlineStyle.AlphaByte:X2}]";
            }
            return "[/outline_color][/outline_size]" + (scopes.Pop().Foreground is null ? "" : "[/color]");
        });
        return (result, found);
    }
}
