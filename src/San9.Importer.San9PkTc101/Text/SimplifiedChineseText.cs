using System.Text;
using System.Text.RegularExpressions;
using OpenccNetLib;
using San9.Content.Text;

namespace San9.Importer.San9PkTc101.Text;

/// <summary>Target-script rules; the canonical source remains unchanged.</summary>
internal static class SimplifiedChineseText
{
    private static readonly ThreadLocal<Opencc> Converter = new(() => new(OpenccConfig.T2S));
    private static readonly Regex PreservedNameCharacters = new("[綝顗勑紞璝騄儁濬寗乾昇麴鍾]", RegexOptions.CultureInvariant);
    private static readonly Regex PreservedNames = new(string.Join("|", new[]
    {
        "孫綝", "樂綝", "荀顗", "馮紞", "劉璝", "馬雲騄", "子勑", "朱儁", "儁乂",
        "王濬", "潘濬", "陶濬", "濬沖", "寗隨", "高昇", "孫乾", "麴義", "鍾繇"
    }.Select(name => SourceTextTerms.Pattern(name).ToString())), RegexOptions.CultureInvariant);
    private static readonly Regex NamesAndAuthorship = WithAuthorship("所著");
    private static readonly Regex NamesAndWork = WithAuthorship("著作");
    private static readonly HashSet<int> AuthorshipMessages =
        [7671, 7676, 7677, 7679, 7681, 7683, 7684, 7685, 7686, 7689, 7701, 7706, 7707, 7709, 7711, 7716, 7719];

    public static MessageDefinition Convert(MessageDefinition canonical, string catalog, int? messageId)
    {
        int start = catalog switch { "messages" => 1678, "base-messages" => 1610, _ => -1 };
        bool personField = start >= 0 && messageId is int id && id >= start && id < start + 2800 && (id - start) % 4 < 3;
        // The separate source name pool supplies authored/new officer name characters.
        personField |= catalog == "messages" && messageId is >= 11455 and <= 12612;
        Regex? authorship = (catalog, messageId) switch
        {
            ("messages", int message) when AuthorshipMessages.Contains(message) => NamesAndAuthorship,
            ("messages", 7704) => NamesAndWork,
            ("biographies", 497) => NamesAndAuthorship,
            _ => null
        };
        Regex protection = authorship ?? (personField ? PreservedNameCharacters : PreservedNames);
        int foundAuthorship = 0;
        var result = MessageTextRuns.Map(canonical, text => ConvertProtected(text, protection, match =>
        {
            if (match.Groups["authorship"].Success)
            {
                foundAuthorship++;
                return match.Value;
            }
            return personField ? NameCharacter(match.Value) : ConvertName(match.Value);
        }));
        if (authorship is not null && foundAuthorship != 1)
            throw new InvalidDataException($"Authorship source mismatch: {catalog}.{messageId}.");
        return result;
    }

    private static string NameCharacter(string source) => source switch { "麴" => "麹", "鍾" => "钟", _ => source };

    private static Regex WithAuthorship(string term) =>
        new($"(?<authorship>{SourceTextTerms.Pattern(term)})|{PreservedNames}", RegexOptions.CultureInvariant);

    private static string ConvertName(string name) =>
        ConvertProtected(name, PreservedNameCharacters, match => NameCharacter(match.Value));

    private static string ConvertProtected(string text, Regex protection, Func<Match, string> preserve)
    {
        var output = new StringBuilder();
        int position = 0;
        foreach (Match match in protection.Matches(text))
        {
            output.Append(Converter.Value!.Convert(text[position..match.Index])).Append(preserve(match));
            position = match.Index + match.Length;
        }
        return output.Append(Converter.Value!.Convert(text[position..])).ToString();
    }
}
