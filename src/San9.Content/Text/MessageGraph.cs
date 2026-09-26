namespace San9.Content.Text;

public static class MessageGraph
{
    public static IEnumerable<MessageNode> Walk(IEnumerable<MessageNode> nodes) => WalkNodes(nodes, 0);

    private static IEnumerable<MessageNode> WalkNodes(IEnumerable<MessageNode> nodes, int depth)
    {
        if (depth >= 64) throw new InvalidDataException("Message node tree too deep.");
        foreach (var node in nodes)
        {
            yield return node;
            var children = node switch
            {
                MessageStyled s => s.Body.AsEnumerable(),
                MessageConditional c => c.Body.AsEnumerable(),
                MessageChoice c => c.Choices.SelectMany(a => a),
                MessageRandomChoice c => c.Choices.SelectMany(a => a), _ => []
            };
            foreach (var child in WalkNodes(children, depth + 1)) yield return child;
        }
    }

    public static void Validate(IReadOnlyDictionary<string, MessageDefinition> messages)
    {
        foreach (var (key, message) in messages)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidDataException("Invalid message identity.");
            foreach (var node in Walk(message.Nodes))
            {
                if (node is MessageReference reference && (string.IsNullOrWhiteSpace(reference.Key) || !messages.ContainsKey(reference.Key)))
                    throw new InvalidDataException($"Invalid message reference in {key}.");
                if (node is MessageStyled styled) _ = MessageStyles.Get(styled.Preset);
                if (node is MessageLiteral literal) MessageStyles.ValidateMarkup(literal.BbCode);
                if (node is MessageChoice { Choices.Length: 0 } or MessageRandomChoice { Choices.Length: 0 }) throw new InvalidDataException("Empty message choice.");
                MessageExpression? expression = node switch { MessageValue v => v.Expression, MessageConditional c => c.Predicate, MessageChoice c => c.Index, _ => null };
                if (expression is not null) ValidateExpression(expression, 0);
            }
        }
    }

    private static void ValidateExpression(MessageExpression e, int depth)
    {
        if (depth >= 64) throw new InvalidDataException("Message expression too deep.");
        int count = e.Arguments?.Length ?? 0;
        bool valid = e.Operation switch
        {
            "constant" => e.Number.HasValue && count == 0,
            "text" => e.Binding is not null && count == 0,
            "entity" => Enum.TryParse<MessageEntityKind>(e.Binding, out var kind) && Enum.IsDefined(kind) && e.Number is >= 0 and <= int.MaxValue && count == 0,
            "property" => !string.IsNullOrWhiteSpace(e.Binding) && count == 1,
            "binding" => !string.IsNullOrWhiteSpace(e.Binding) && count == 0,
            "query" => !string.IsNullOrWhiteSpace(e.Binding),
            "random" or "chance" => count == 1,
            "+" or "-" or "*" or "/" or "%" or "=" or "!" or "<" or ">" or "<=" or ">=" or "and" or "or" => count == 2,
            _ => false
        };
        if (!valid) throw new InvalidDataException($"Invalid message expression: {e.Operation}");
        foreach (var argument in e.Arguments ?? []) ValidateExpression(argument, depth + 1);
    }

    public static bool ReferencesValid(string key, IReadOnlyDictionary<string, MessageDefinition> messages) =>
        Inspect(key, messages, collectBindings: false).ReferencesValid;

    public static (bool ReferencesValid, string[] Bindings) Requirements(string key, IReadOnlyDictionary<string, MessageDefinition> messages) =>
        Inspect(key, messages, collectBindings: true);

    private static (bool ReferencesValid, string[] Bindings) Inspect(string key,
        IReadOnlyDictionary<string, MessageDefinition> messages, bool collectBindings)
    {
        var active = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var bindings = collectBindings ? new HashSet<string>(StringComparer.Ordinal) : null;
        bool referencesValid = true;
        void Visit(string id)
        {
            if (active.Contains(id)) { referencesValid = false; return; }
            if (!seen.Add(id)) return;
            if (!messages.TryGetValue(id, out var message)) { referencesValid = false; return; }
            if (active.Count >= 64) { referencesValid = false; return; }
            active.Add(id);
            if (bindings is not null) bindings.UnionWith(Bindings(message.Nodes));
            foreach (var reference in Walk(message.Nodes).OfType<MessageReference>()) Visit(reference.Key);
            active.Remove(id);
        }
        Visit(key);
        return (referencesValid, bindings?.Order(StringComparer.Ordinal).ToArray() ?? []);
    }

    // Falling off a branch normally resumes its containing sequence. A return is redundant
    // only when that entire continuation already ends the current message.
    public static MessageNode[] Simplify(MessageNode[] nodes) => Simplify(nodes, endsMessage: true);

    private static MessageNode[] Simplify(MessageNode[] nodes, bool endsMessage)
    {
        int length = nodes.Length;
        for (int i = 0; i < length; i++)
            if (EndsMessage(nodes[i])) { length = i + 1; break; }
        if (endsMessage && length > 0 && nodes[length - 1] is MessageReturn) length--;
        var result = new MessageNode[length];
        for (int i = 0; i < length; i++)
        {
            bool atEnd = endsMessage && i == length - 1;
            result[i] = nodes[i] switch
            {
                MessageStyled s => s with { Body = Simplify(s.Body, atEnd) },
                MessageConditional c => c with { Body = Simplify(c.Body, atEnd) },
                MessageChoice c => c with { Choices = c.Choices.Select(branch => Simplify(branch, atEnd)).ToArray() },
                MessageRandomChoice c => c with { Choices = c.Choices.Select(branch => Simplify(branch, atEnd)).ToArray() },
                _ => nodes[i]
            };
        }
        List<MessageNode> compact = [];
        foreach (var node in result)
        {
            MessageNode value = node is MessageStyled styled && styled.Body.All(n => n is MessageLiteral)
                ? new MessageLiteral(MessageMarkup.Static([styled])) : node;
            if (value is MessageLiteral text && compact.LastOrDefault() is MessageLiteral previous)
                compact[^1] = new MessageLiteral(previous.BbCode + text.BbCode);
            else compact.Add(value);
        }
        return compact.ToArray();
    }

    private static bool EndsMessage(MessageNode node) => node switch
    {
        MessageStyled s => s.Body.Any(EndsMessage),
        MessageReturn or MessageReference { Tail: true } => true,
        MessageChoice c => c.Choices.Length > 0 && c.Choices.All(branch => branch.Any(EndsMessage)),
        MessageRandomChoice c => c.Choices.Length > 0 && c.Choices.All(branch => branch.Any(EndsMessage)),
        _ => false
    };

    public static string[] Bindings(IEnumerable<MessageNode> nodes)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        void Visit(MessageExpression e)
        {
            if (e.Operation == "binding") result.Add(e.Binding!);
            foreach (var child in e.Arguments ?? []) Visit(child);
        }
        foreach (var node in Walk(nodes))
        {
            var expression = node switch { MessageValue v => v.Expression, MessageConditional c => c.Predicate, MessageChoice c => c.Index, _ => null };
            if (expression is not null) Visit(expression);
        }
        return result.Order(StringComparer.Ordinal).ToArray();
    }
}
