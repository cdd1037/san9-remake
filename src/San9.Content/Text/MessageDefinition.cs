using System.Text.Json.Serialization;

namespace San9.Content.Text;

// Text uses finite preset scopes and inline images; message control and bindings are ordinary data.
[JsonConverter(typeof(MessageNodeJsonConverter))]
public abstract record MessageNode;
public sealed record MessageLiteral(string BbCode) : MessageNode;
public sealed record MessageValue(MessageExpression Expression, MessageValueFormat? Format = null) : MessageNode;
public sealed record MessageValueFormat(string Kind, int Width = 0, bool PadZero = false, bool AlignLeft = false, int? Precision = null);
public sealed record MessageReference(string Key, bool Tail) : MessageNode;
public sealed record MessageConditional(MessageExpression Predicate, MessageNode[] Body) : MessageNode;
public sealed record MessageChoice(MessageExpression Index, MessageNode[][] Choices) : MessageNode;
public sealed record MessageRandomChoice(MessageNode[][] Choices) : MessageNode;
public sealed record MessageReturn : MessageNode;
public sealed record MessageStyled(string Preset, MessageNode[] Body) : MessageNode;

public sealed record MessageExpression(string Operation, string? Binding = null, long? Number = null,
    MessageExpression[]? Arguments = null);
[JsonConverter(typeof(MessageDefinitionJsonConverter))]
public sealed record MessageDefinition(MessageNode[] Nodes);

public enum MessageEntityKind { Person, Force, Settlement, Unit, Treasure, Tactic, Region, Corps }
public sealed record MessageEntity(MessageEntityKind Kind, int Id);
public sealed record MessagePersonProfile(string? SurnameKey, string? GivenNameKey, string? PortraitKey);

// Captured by the producing event, before an arriving unit/task can disappear.
public enum MessageReturnKind { Personnel, Automatic, Ordered }

public static class MessageMarkup
{
    public static IEnumerable<string> IconIds(string bbCode)
    {
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
                     bbCode, @"\[img[^\]]*\](?<path>[^\[]*)\[/img\]"))
        {
            string path = match.Groups["path"].Value;
            if (!path.StartsWith("icons/", StringComparison.Ordinal) || !System.Text.RegularExpressions.Regex.IsMatch(path[6..], "^[a-z0-9-]+$"))
                throw new InvalidDataException($"Invalid package image path: {path}");
            yield return path[6..];
        }
    }

    public static string Escape(string text) => text.Replace("[", "[lb]", StringComparison.Ordinal);

    // A strict accessor, never a lossy tag stripper. Suitable for names and sorting.
    public static string Plain(string bbCode)
    {
        var result = new System.Text.StringBuilder();
        for (int i = 0; i < bbCode.Length; i++)
        {
            if (bbCode[i] != '[') { result.Append(bbCode[i]); continue; }
            if (bbCode.AsSpan(i).StartsWith("[lb]")) result.Append('[');
            else if (bbCode.AsSpan(i).StartsWith("[rb]")) result.Append(']');
            else throw new InvalidDataException("Rich content requested as plain text.");
            i += 3;
        }
        return result.ToString();
    }

    public static string Static(IEnumerable<MessageNode> nodes)
    {
        var result = new System.Text.StringBuilder();
        foreach (var node in nodes)
        {
            if (node is MessageLiteral text) result.Append(text.BbCode);
            else if (node is MessageStyled styled)
                result.Append(MessageStyles.Open(styled.Preset)).Append(Static(styled.Body)).Append(MessageStyles.Close);
            else throw new InvalidDataException("Dynamic content requested as static text.");
        }
        return result.ToString();
    }
}
