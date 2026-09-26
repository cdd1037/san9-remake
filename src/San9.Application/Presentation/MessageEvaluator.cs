using System.Globalization;
using System.Text;
using San9.Content.Text;

namespace San9.Application.Presentation;

public sealed record MessageBinding(long Number, string? Text = null)
{
    public MessageEntity? Entity { get; init; }
    public static MessageBinding Return(MessageReturnKind kind) => Enum.IsDefined(kind)
        ? new(0, kind.ToString()) : throw new ArgumentOutOfRangeException(nameof(kind));
    public static MessageBinding Reference(MessageEntityKind kind, int id) => id >= 0 && Enum.IsDefined(kind)
        ? new(0) { Entity = new(kind, id) } : throw new ArgumentOutOfRangeException(nameof(id));
}
public sealed record MessageContext(IReadOnlyDictionary<string, MessageBinding> Values,
    Func<string, IReadOnlyList<MessageBinding>, MessageBinding> Query, Func<int, int> RandomIndex);

/// <summary>Evaluate once when a message is produced; views retain the returned BBCode.</summary>
public sealed class MessageEvaluator(IReadOnlyDictionary<string, MessageDefinition> messages)
{
    public string Evaluate(string key, MessageContext context, bool source = false)
    {
        if (!MessageGraph.Requirements(key, messages).Executable)
            throw new InvalidDataException($"Message dependencies are not executable: {key}");
        var output = new StringBuilder();
        var active = new HashSet<string>(StringComparer.Ordinal);
        int steps = 0;
        var formatting = new MessageFormatting();
        void Render(string id)
        {
            if (active.Count >= 64 || !active.Add(id)) throw new InvalidDataException($"Cyclic/deep message reference: {id}");
            try
            {
                if (!messages.TryGetValue(id, out var message) || !message.SemanticsConfirmed)
                    throw new InvalidDataException($"Message is not executable: {id}");
                Walk(message.Nodes);
            }
            finally { active.Remove(id); }
        }
        bool Walk(IEnumerable<MessageNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (++steps > 10000 || output.Length > 1_000_000) throw new InvalidDataException("Message evaluation limit exceeded.");
                switch (node)
                {
                    case MessageLiteral literal: output.Append(source ? literal.SourceBbCode : literal.BbCode); break;
                    case MessageFormat format: formatting.Change(output, format); break;
                    case MessageValue value:
                        var resolved = Value(value.Expression);
                        output.Append(value.Prefix).Append(MessageMarkup.Escape(Format(resolved,value.Format))).Append(value.Suffix); break;
                    case MessageReference reference: Render(reference.Key); if (reference.Tail) return true; break;
                    case MessageConditional conditional:
                        if (Numeric(Value(conditional.Predicate)) != 0 && Walk(conditional.Body)) return true;
                        break;
                    case MessageChoice choice:
                        long index = Numeric(Value(choice.Index));
                        if (index < 0 || index >= choice.Choices.Length) throw new InvalidDataException("Message choice outside candidate range.");
                        if (Walk(choice.Choices[index])) return true;
                        break;
                    case MessageReturn: return true;
                    default: throw new InvalidDataException("Unsupported message node.");
                }
            }
            return false;
        }
        MessageBinding Value(MessageExpression e)
        {
            if (++steps > 10000) throw new InvalidDataException("Message expression limit exceeded.");
            if (e.Operation == "constant") return new(e.Number ?? throw new InvalidDataException("Missing constant."));
            if (e.Operation == "text") return new(0, e.Binding!);
            if (e.Operation == "entity") return MessageBinding.Reference(Enum.Parse<MessageEntityKind>(e.Binding!), checked((int)e.Number!.Value));
            if (e.Operation == "binding") return context.Values.TryGetValue(e.Binding!, out var value)
                ? value : throw new InvalidDataException($"Missing message binding: {e.Binding}");
            var args = (e.Arguments ?? []).Select(Value).ToArray();
            long a = args.Length > 0 ? args[0].Number : 0, b = args.Length > 1 ? args[1].Number : 0;
            if (e.Operation == "query") return context.Query(e.Binding!, args);
            if (e.Operation == "property") return context.Query(e.Binding!, args);
            if (args.Any(v => v.Entity is not null || v.Text is not null))
            {
                if (e.Operation is not ("=" or "!")) throw new InvalidDataException("Arithmetic requires numeric message values.");
                if ((args[0].Entity is null) != (args[1].Entity is null) || (args[0].Text is null) != (args[1].Text is null))
                    throw new InvalidDataException("Message comparison requires values of the same type.");
                bool equal = args[0].Entity is { } entity ? entity == args[1].Entity : args[0].Text == args[1].Text;
                return new((e.Operation == "=" ? equal : !equal) ? 1 : 0);
            }
            if (e.Operation is "random" or "chance")
            {
                int bound = e.Operation == "chance" ? 100 : checked((int)a);
                if (bound <= 0) throw new InvalidDataException("Invalid random bound.");
                int draw = context.RandomIndex(bound);
                if ((uint)draw >= bound) throw new InvalidDataException("Random provider returned invalid index.");
                return new(e.Operation == "chance" ? draw < a ? 1 : 0 : draw);
            }
            return new(e.Operation switch
            {
                "+" => unchecked((uint)a + (uint)b), "-" => unchecked((uint)a - (uint)b),
                "*" => unchecked((uint)a * (uint)b), "/" => b != 0 ? (uint)a / (uint)b : a,
                "%" => b != 0 ? (uint)a % (uint)b : a,
                "=" => a == b ? 1 : 0, "!" => a != b ? 1 : 0,
                "<" => (uint)a < (uint)b ? 1 : 0, ">" => (uint)a > (uint)b ? 1 : 0,
                "<=" => (uint)a <= (uint)b ? 1 : 0, ">=" => (uint)a >= (uint)b ? 1 : 0,
                "and" => a != 0 && b != 0 ? 1 : 0, "or" => a != 0 || b != 0 ? 1 : 0,
                _ => throw new InvalidDataException($"Unknown message operation: {e.Operation}")
            });
        }
        Render(key);
        output.Append(formatting.Closing);
        if (output.Length > 1_000_000) throw new InvalidDataException("Message output limit exceeded.");
        return output.ToString();
    }

    private static long Numeric(MessageBinding value) => value.Entity is null && value.Text is null ? value.Number :
        throw new InvalidDataException("A numeric message value is required.");

    private static string Format(MessageBinding value, MessageValueFormat? format)
    {
        if (value.Entity is not null) throw new InvalidDataException("An entity requires an explicit display property.");
        if (format is null) return value.Text ?? value.Number.ToString(CultureInfo.InvariantCulture);
        if (format.Width is < 0 or > 4096 || format.Precision is < 0 or > 4096) throw new InvalidDataException("Invalid message field width.");
        if (format.Kind != "string") _ = Numeric(value);
        string text = format.Kind switch
        {
            "string" => value.Text ?? throw new InvalidDataException("String message parameter requires text."),
            "signed" => unchecked((int)value.Number).ToString(CultureInfo.InvariantCulture),
            "unsigned" => unchecked((uint)value.Number).ToString(CultureInfo.InvariantCulture),
            "hex" => unchecked((uint)value.Number).ToString("x",CultureInfo.InvariantCulture),
            "hex-upper" => unchecked((uint)value.Number).ToString("X",CultureInfo.InvariantCulture),
            _ => throw new InvalidDataException("Unsupported value format.")
        };
        if (format.Precision is int precision)
        {
            if (format.Kind == "string") text = string.Concat(text.EnumerateRunes().Take(precision).Select(r => r.ToString()));
            else if (value.Number == 0 && precision == 0) text = "";
            else text = text.StartsWith('-') ? "-" + text[1..].PadLeft(precision, '0') : text.PadLeft(precision, '0');
        }
        if (format.AlignLeft) return text.PadRight(format.Width);
        bool zero = format.PadZero && format.Precision is null && format.Kind != "string";
        if (zero && text.StartsWith('-')) return "-"+text[1..].PadLeft(Math.Max(0,format.Width-1),'0');
        return text.PadLeft(format.Width,zero?'0':' ');
    }
}
