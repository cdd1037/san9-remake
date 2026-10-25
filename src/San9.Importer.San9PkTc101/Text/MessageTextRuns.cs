using System.Text;
using System.Text.RegularExpressions;
using San9.Content.Text;

namespace San9.Importer.San9PkTc101.Text;

internal static class MessageTextRuns
{
    public static MessageDefinition Map(MessageDefinition message, Func<string, string> transform) => new(MapNodes(message.Nodes, transform));

    private static MessageNode[] MapNodes(MessageNode[] nodes, Func<string, string> transform) => nodes.Select(node => node switch
    {
        MessageLiteral text => text with { BbCode = transform(text.BbCode) },
        MessageConditional branch => branch with { Body = MapNodes(branch.Body, transform) },
        MessageChoice choice => choice with { Choices = choice.Choices.Select(body => MapNodes(body, transform)).ToArray() },
        MessageRandomChoice choice => choice with { Choices = choice.Choices.Select(body => MapNodes(body, transform)).ToArray() },
        MessageStyled style => style with { Body = MapNodes(style.Body, transform) },
        _ => node
    }).ToArray();
}

// Match lexical terms across source layout newlines, without removing their displayed breaks.
internal static class SourceTextTerms
{
    public static Regex Pattern(string term) => new(string.Join("[\\r\\n]*", term.EnumerateRunes().Select(r => Regex.Escape(r.ToString()))), RegexOptions.CultureInvariant);

    public static string Replace(string text, Regex pattern, string replacement, ref int count)
    {
        int matches = 0;
        string result = pattern.Replace(text, match =>
        {
            matches++;
            var runes = replacement.EnumerateRunes().ToArray();
            var output = new StringBuilder();
            int index = 0;
            foreach (var rune in match.Value.EnumerateRunes())
            {
                if (rune.Value is 10 or 13) output.Append(rune.ToString());
                else if (index < runes.Length) output.Append(runes[index++].ToString());
            }
            while (index < runes.Length) output.Append(runes[index++].ToString());
            return output.ToString();
        });
        count += matches;
        return result;
    }
}
