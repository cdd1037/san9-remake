using System.Text.Json.Serialization;

namespace San9.Content.Text;

// Display syntax is Godot BBCode; message control and bindings are ordinary data.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(MessageLiteral), "literal")]
[JsonDerivedType(typeof(MessageValue), "value")]
[JsonDerivedType(typeof(MessageReference), "reference")]
[JsonDerivedType(typeof(MessageConditional), "conditional")]
[JsonDerivedType(typeof(MessageChoice), "choice")]
[JsonDerivedType(typeof(MessageReturn), "return")]
[JsonDerivedType(typeof(MessageFormat), "format")]
public abstract record MessageNode;
public sealed record MessageLiteral(string BbCode, string SourceBbCode) : MessageNode;
public sealed record MessageValue(MessageExpression Expression, string Prefix = "", string Suffix = "", MessageValueFormat? Format = null) : MessageNode;
public sealed record MessageValueFormat(string Kind, int Width = 0, bool PadZero = false, bool AlignLeft = false, int? Precision = null);
public sealed record MessageReference(string Key, bool Tail) : MessageNode;
public sealed record MessageConditional(MessageExpression Predicate, MessageNode[] Body) : MessageNode;
public sealed record MessageChoice(MessageExpression Index, MessageNode[][] Choices) : MessageNode;
public sealed record MessageReturn : MessageNode;
public sealed record MessageFormat(string Open, string Close, string Channel = "foreground") : MessageNode;

public sealed record MessageExpression(string Operation, string? Binding = null, long? Number = null,
    MessageExpression[]? Arguments = null);
public sealed record MessageDefinition(MessageNode[] Nodes, bool SemanticsConfirmed, string[] RequiredBindings)
{
    // Source semantics can be known even when a runtime lowering is unavailable.
    public string[] UnavailableReasons { get; init; } = [];
}

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

    public static string Static(IEnumerable<MessageNode> nodes, bool source = false)
    {
        var result = new System.Text.StringBuilder();
        var formatting = new MessageFormatting();
        foreach (var node in nodes)
        {
            if (node is MessageLiteral text) result.Append(source ? text.SourceBbCode : text.BbCode);
            else if (node is MessageFormat format) { formatting.Change(result, format); }
            else throw new InvalidDataException("Dynamic content requested as static text.");
        }
        return result.Append(formatting.Closing).ToString();
    }
}

/// <summary>Independent BBCode channels with balanced nesting across message control flow.</summary>
public sealed class MessageFormatting
{
    private readonly SortedDictionary<string, MessageFormat> _channels = new(StringComparer.Ordinal);
    public string Closing => string.Concat(_channels.Values.Reverse().Select(f => f.Close));
    public void Change(System.Text.StringBuilder output, MessageFormat format)
    {
        if (format.Channel is not ("foreground" or "effect")) throw new InvalidDataException("Unknown message format channel.");
        output.Append(Closing);
        if (format.Open.Length == 0) _channels.Remove(format.Channel);
        else _channels[format.Channel] = format;
        foreach (var active in _channels.Values) output.Append(active.Open);
    }
}
