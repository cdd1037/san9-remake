using System.Text;
using San9.Content.Text;

namespace San9.Importer.San9PkTc101.Text;

public sealed record MessageIssue(int Offset, string Reason);
public sealed record ParsedMessage(MessageDefinition Definition, int ConsumedBytes, bool FullyParsed, MessageIssue[] Issues)
{
    public bool SemanticsConfirmed => Issues.Length == 0;
}

/// <summary>Grammar-driven TC-101 decoding. Unknown semantics remain explicit import diagnostics.</summary>
public sealed class OriginalMessageParser
{
    private readonly OriginalTextConverter _text = new();
    private readonly List<MessageIssue> _issues = [];
    private byte[] _bytes = [];
    private int _position;
    private string _catalog = "messages";
    private int _depth;
    private int _expressionDepth;
    private List<MessageNode> _root = [];
    private bool _formatString;
    private int _formatParameter;
    private OriginalMessagePresentation _presentation;
    public static readonly IReadOnlyDictionary<byte, string> Icons = new Dictionary<byte, string>
    {
        [0xCD] = "arrow-up-right", [0xCE] = "arrow-down-right", [0xD0] = "city",
        [0xD1] = "crossed-arrows", [0xD2] = "empty-square", [0xD3] = "question",
        [0xD4] = "turn-arrow", [0xD5] = "book", [0xD6] = "waves", [0xD7] = "circle"
    };

    public ParsedMessage Parse(byte[] bytes, string catalog = "messages", bool terminated = true, bool formatString = false,
        OriginalMessagePresentation presentation = OriginalMessagePresentation.Standard)
    {
        var parsed = ParseSyntax(bytes, catalog, terminated, formatString, presentation);
        try { return parsed with { Definition = OriginalMessageStyles.Compile(parsed.Definition) }; }
        catch (InvalidDataException error)
        {
            return parsed with { Definition = new([]), Issues = [.. parsed.Issues, new(0, error.Message)] };
        }
    }

    internal ParsedMessage ParseSyntax(byte[] bytes, string catalog = "messages", bool terminated = true, bool formatString = false,
        OriginalMessagePresentation presentation = OriginalMessagePresentation.Standard)
    {
        _bytes = bytes; _position = 0; _catalog = catalog; _depth = 0; _expressionDepth = 0;
        _issues.Clear();
        List<MessageNode> nodes = [];
        bool complete = true;
        _root = [];
        _formatString = formatString; _formatParameter = 0;
        _presentation = presentation;
        try
        {
            nodes = Sequence(false);
            if (terminated && !bytes.AsSpan().EndsWith(new byte[] { 5, 5, 5 })) throw new InvalidDataException("Missing terminal delimiter.");
        }
        catch (InvalidDataException e) { complete = false; nodes = _root; Issue(e.Message); }
        return new(new(nodes.ToArray()), _position, complete && _position == bytes.Length, _issues.ToArray());
    }

    private List<MessageNode> Sequence(bool branch)
    {
        if (++_depth > 64) throw new InvalidDataException("Message nesting exceeds 64.");
        List<MessageNode> nodes = [];
        bool ended = false;
        if (_depth == 1) _root = nodes;
        while (_position < _bytes.Length)
        {
            if (Meta(5))
            {
                _position += 3; nodes.Add(new MessageReturn());
                if (branch) { ended = true; break; }
                continue;
            }
            if (Meta(4))
            {
                _position += 3;
                var predicate = Boolean();
                nodes.Add(new MessageConditional(predicate, Sequence(true).ToArray()));
                continue;
            }
            int start = _position;
            byte first = Take();
            if (first == '%' && _formatString)
            {
                if (Peek() == '%') { Take(); nodes.Add(new MessageLiteral("%")); continue; }
                bool left = false, zero = false;
                while (Peek() is (byte)'-' or (byte)'0') { if (Take() == '-') left=true; else zero=true; }
                int width = Peek() is >= (byte)'0' and <= (byte)'9' ? checked((int)Decimal()) : 0;
                int? precision = null;
                if (Peek() == '.') { Take(); precision = checked((int)Decimal()); }
                byte kind=Take();
                string format=kind switch { (byte)'s'=>"string", (byte)'d' or (byte)'i'=>"signed", (byte)'u'=>"unsigned", (byte)'x'=>"hex", (byte)'X'=>"hex-upper",
                    _=>throw new InvalidDataException($"Unsupported native format {kind:X2}.") };
                string key=$"argument.{_formatParameter++}";
                nodes.Add(new MessageValue(new("binding",key),Format:new(format,width,zero,left,precision)));
            }
            else if (first == 1)
            {
                byte macro = Take();
                if (macro is (byte)'J' or (byte)'C')
                {
                    int id = Take() | Take() << 8;
                    nodes.Add(new MessageReference($"{_catalog}.{id}", macro == 'J'));
                }
                else if (macro == 'S')
                {
                    var expression = Arithmetic();
                    if (expression.Operation != "random" || expression.Arguments is not [{ Operation: "constant", Number: > 0 and <= 256 } bound])
                        throw new InvalidDataException("Nonconstant branch selection requires semantic review.");
                    var candidates = new MessageNode[bound.Number!.Value][];
                    for (int i = 0; i < candidates.Length; i++) candidates[i] = Sequence(true).ToArray();
                    nodes.Add(new MessageRandomChoice(candidates));
                }
                else throw new InvalidDataException($"Unsupported macro {macro:X2}.");
            }
            else if (first == 2)
            {
                if (Peek() == 0x8C)
                {
                    Take(); long style = Decimal(); byte suffix = Take();
                    if (suffix != 'b') throw new InvalidDataException($"Unsupported style suffix {suffix:X2}.");
                    AddStyle(nodes, style, suffix, start);
                }
                else
                {
                    _position--;
                    nodes.AddRange(OriginalMessageStyles.DisplayValue(Arithmetic(display: true), _presentation));
                }
            }
            else if (first is 0x1A or 0x1B)
            {
                byte code = Take();
                if (first == 0x1B && code == '[')
                {
                    long? style = Peek() is >= (byte)'0' and <= (byte)'9' ? Decimal() : null;
                    byte suffix = Take();
                    if (suffix is not ((byte)'b' or (byte)'s' or (byte)'m' or (byte)'u')) throw new InvalidDataException("Unknown runtime style suffix.");
                    AddStyle(nodes, style, suffix, start);
                    continue;
                }
                if (code is (byte)'B' or (byte)'C' or (byte)'F') Take();
                Issue($"Presentation flow control {first:X2} {code:X2} requires a host presentation binding.", start);
            }
            else if (first == 0xFA && Icons.TryGetValue(Peek(), out string? icon))
            {
                Take(); string image = $"[img]icons/{icon}[/img]";
                nodes.Add(new MessageLiteral(image));
            }
            else
            {
                _position = start;
                while (_position < _bytes.Length)
                {
                    byte next = Peek();
                    if (next == '%' && _formatString) break;
                    if (next < 32 && next is not (9 or 10 or 13)) break;
                    if (next == 0xFA && _position + 1 < _bytes.Length && Icons.ContainsKey(_bytes[_position + 1])) break;
                    Take(); if (next >= 128) Take();
                }
                if (_position == start) throw new InvalidDataException($"Unsupported control {first:X2}.");
                string text;
                try { text = _text.DecodeCharacters(_bytes.AsSpan(start, _position - start)); }
                catch (InvalidDataException) { _position = start; throw; }
                nodes.Add(new MessageLiteral(MessageMarkup.Escape(text)));
            }
        }
        if (branch && !ended) throw new InvalidDataException("Missing branch delimiter.");
        _depth--;
        return nodes;
    }
    private void AddStyle(List<MessageNode> nodes, long? index, byte suffix, int offset)
    {
        if (suffix == 'u' || index is null or >= 32)
        {
            nodes.Add(new OriginalStyleChange(null));
            return;
        }
        string? color = index switch { 1 => "blue", 4 => "green", 17 => "peach", 14 => "yellow", _ => null };
        if (color is not null && suffix is (byte)'b' or (byte)'s' && _presentation == OriginalMessagePresentation.Help)
        {
            // RE-0167: help/training normalizes b/s to foreground, remaps 1/4, and adds black outline.
            nodes.Add(new OriginalStyleChange(color));
            return;
        }
        if (color is not null && suffix == 'b') nodes.Add(new OriginalStyleChange(color + "-outline"));
        else Issue($"Source style {index}{(char)suffix} has no confirmed message preset for {_presentation}.", offset);
    }
    private MessageExpression Boolean()
    {
        var left = Comparison();
        while (Peek() == 6)
        {
            Take(); byte op = Take();
            left = new(op switch { (byte)'&' => "and", (byte)'|' => "or", _ => throw new InvalidDataException("Unknown boolean operator.") }, Arguments: [left, Comparison()]);
        }
        return left;
    }
    private MessageExpression Comparison()
    {
        var left = Arithmetic();
        if (Peek() != 4) return left;
        Take(); char op = (char)Take();
        if (!"!<=>{}".Contains(op)) throw new InvalidDataException("Unknown comparison operator.");
        return new(op switch { '{' => "<=", '}' => ">=", _ => op.ToString() }, Arguments: [left, Arithmetic()]);
    }
    private MessageExpression Arithmetic(bool display = false)
    {
        var left = Atom(display);
        while (Peek() == 3)
        {
            Take(); char op = (char)Take();
            if (!"+-*/%".Contains(op)) throw new InvalidDataException("Unknown arithmetic operator.");
            left = new(op.ToString(), Arguments: [left, Atom()]);
        }
        return left;
    }
    private MessageExpression Atom(bool display = false)
    {
        if (++_expressionDepth > 64) throw new InvalidDataException("Expression nesting exceeds 64.");
        try { return ReadAtom(display); }
        finally { _expressionDepth--; }
    }

    private MessageExpression ReadAtom(bool display)
    {
        if (Peek() == '%' || Peek() is >= (byte)'0' and <= (byte)'9') return new("constant", Number: Decimal());
        if (Take() != 2) throw new InvalidDataException("Expected expression operand.");
        byte selector = Take();
        (int Start, string Name)[] groups = [(1,"person"),(11,"region"),(21,"treasure"),(31,"force"),(41,"building"),(51,"unit"),(61,"building"),(71,"tactic"),(81,"facility"),(91,"strategy"),(101,"number"),(121,"string")];
        foreach (var (start, category) in groups)
        {
            if (selector < start || selector > start + 8) continue;
            int slot = selector - start;
            if (slot >= 5) Issue($"Argument slot outside bound range: {category}[{slot}].");
            string key = $"{category}.{slot}";
            if (category is not ("number" or "string") && Peek() >= 32)
            {
                byte property = Take();
                string? name = (category, property) switch
                {
                    ("person", 0xDA) => "name", ("person", 0xF7) => "gender-flag", ("person", 0xF6) => "gender-flag-clear",
                    ("person", 0x39) => "biography-resource", ("person", 0x38) => "portrait",
                    ("person", 0x47) => "status", ("person", 0x91) => "personality",
                    ("person", 0x8F) => "righteousness", ("person", 0x90) => "ambition",
                    ("person", 0x4D) => "current-leadership", ("person", 0x4E) => "current-war",
                    ("person", 0x4F) => "current-intelligence", ("person", 0x83) => "current-force-loyalty",
                    ("person", 0x89) => "injury", ("person", 0xE0) => "reverse-location-id",
                    ("person", 0xE3) => "office-rank-order",
                    ("force", 0x32) => "id", ("force", 0x3F) => "peerage-rank",
                    ("force", 0x7C) => "is-alliance-leader", ("force", 0xBA) => "same-root-corps-count",
                    ("force", 0xC7) => "country-title-order", ("force", 0xC8) => "reverse-peerage-rank",
                    ("force", 0xC9) => "reverse-leader-id", ("force", 0xCB) => "reverse-id",
                    ("force", 0xCE) => "reverse-direct-leader-id",
                    ("building", 0x3E) => "reverse-fixed-type", ("building", 0x9E or 0x9F or 0xA7 or 0xA8) => "reverse-id",
                    ("building", 0xA0) => "owner-force-order",
                    ("treasure", 0x36) => "category", ("treasure", 0x43) => "reverse-id",
                    ("tactic", 0x61) => "name", ("tactic", 0x36) => "system-order",
                    ("unit", 0x45) => "command-event-type", ("unit", 0x79) => "related-location", _ => null
                };
                if (display)
                    name = (category, property) switch
                    {
                        ("person", 0xDB) => "surname", ("person", 0xDC) => "given-name", ("person", 0xDD) => "courtesy-name",
                        ("person", 0xE0) => "location-name", ("person", 0xE3) => "office-and-name",
                        ("force", 0xC7) => "country-name", ("force", 0xC8) => "rank-name",
                        ("force", 0xC9) => "leader-name", ("force", 0xCE) => "direct-leader-name", ("force", 0xCB) => "name",
                        ("building", 0x9E or 0x9F) => "name-with-type", ("building", 0xA7 or 0xA8) => "name",
                        ("building", 0x3E) => "type-name", ("building", 0xA0) => "owner-name",
                        ("treasure", 0x43) => "name", ("unit", 0x79) => "display-name",
                        ("tactic", 0x36) => "category-name", _ => name
                    };
                else if (category == "person" && property is 0xDB or 0xDC or 0xDD) return new("constant", Number: -1);
                if (name is null) { Issue($"Unresolved {category} property {property:X2}."); name = "unresolved-property"; }
                key += "." + name;
            }
            return new("binding", key);
        }
        if (selector is 0xC9 or 0xCA) return new(selector == 0xCA ? "random" : "chance", Arguments: [Arithmetic()]);
        if (selector is 0xCB or 0xDB) return new("constant", Number: 0);
        var arguments = new List<MessageExpression>();
        int objects = selector switch { 0xCC or 0xCD or 0xCE or 0xCF or 0xD0 or 0xD1 or 0xD9 or 0xDA => 2, 0xD2 or 0xD3 or 0xDE or 0xDF => 1, _ => 0 };
        for (int i = 0; i < objects; i++)
        {
            if (Take() != 2) throw new InvalidDataException("Expected object argument marker.");
            int slot = Take() - (selector == 0xDA ? 31 : 1);
            if ((uint)slot >= 5) Issue("Object argument outside bound range.");
            string key = $"{(selector == 0xDA ? "force" : "person")}.{slot}";
            arguments.Add(new("binding", key));
        }
        if (selector is 0xD2 or 0xD3 or 0xDD) arguments.Add(Arithmetic());
        string? function = selector switch
        {
            0xCC => "parent-child", 0xCD => "same-parent", 0xCE => "same-bloodline", 0xCF => "senior-kin",
            0xD0 => "junior-kin", 0xD1 => "affinity-distance", 0xD2 => "has-treasure", 0xD3 => "has-treasure-category",
            0xD4 => "scenario", 0xD5 => "year", 0xD6 => "season", 0xD7 => "month", 0xD8 => "day",
            0xD9 => "same-root-force", 0xDA => "force-relation", 0xDC => "scenario-mode", 0xDD => "world-flag",
            0xDE => "person-unit-command-in-38-52", 0xDF => "person-in-internal-unit", _ => null
        };
        if (function is null)
        {
            if (selector is < 0xC9 or > 0xDF) throw new InvalidDataException($"Unknown selector {selector:X2}.");
            Issue($"Unresolved expression function {selector:X2}."); function = "unresolved-function";
        }
        return new("query", function, Arguments: arguments.ToArray());
    }
    private long Decimal()
    {
        if (Peek() == '%') Take();
        int start = _position;
        while (Peek() is >= (byte)'0' and <= (byte)'9') Take();
        if (_position == start || !uint.TryParse(Encoding.ASCII.GetString(_bytes, start, _position - start), out uint value))
            throw new InvalidDataException("Invalid decimal operand.");
        return value;
    }
    private bool Meta(byte kind) => _position + 2 < _bytes.Length && _bytes[_position] == 5 && _bytes[_position + 1] == 5 && _bytes[_position + 2] == kind;
    private byte Peek() => _position < _bytes.Length ? _bytes[_position] : (byte)0;
    private byte Take() => _position < _bytes.Length ? _bytes[_position++] : throw new InvalidDataException("Truncated message operand.");
    private void Issue(string reason, int? offset = null) => _issues.Add(new(offset ?? _position, reason));
}
