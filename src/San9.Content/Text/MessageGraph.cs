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
                MessageConditional c => c.Body.AsEnumerable(),
                MessageChoice c => c.Choices.SelectMany(a => a), _ => []
            };
            foreach (var child in WalkNodes(children, depth + 1)) yield return child;
        }
    }

    public static void Validate(IReadOnlyDictionary<string, MessageDefinition> messages)
    {
        foreach (var (key, message) in messages)
        {
            if (string.IsNullOrWhiteSpace(key) || message.RequiredBindings.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException("Invalid message identity/bindings.");
            foreach (var node in Walk(message.Nodes))
            {
                if (node is MessageReference reference && (string.IsNullOrWhiteSpace(reference.Key) || message.SemanticsConfirmed && !messages.ContainsKey(reference.Key)))
                    throw new InvalidDataException($"Invalid message reference in {key}.");
                if (node is MessageFormat format && format.Channel is not ("foreground" or "effect"))
                    throw new InvalidDataException("Invalid message format channel.");
                if (node is MessageChoice { Choices.Length: 0 }) throw new InvalidDataException("Empty message choice.");
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
            "binding" => !string.IsNullOrWhiteSpace(e.Binding) && count == 0,
            "query" => !string.IsNullOrWhiteSpace(e.Binding),
            "random" or "chance" => count == 1,
            "+" or "-" or "*" or "/" or "%" or "=" or "!" or "<" or ">" or "<=" or ">=" or "and" or "or" => count == 2,
            _ => false
        };
        if (!valid) throw new InvalidDataException($"Invalid message expression: {e.Operation}");
        foreach (var argument in e.Arguments ?? []) ValidateExpression(argument, depth + 1);
    }

    public static (bool ReferencesValid, bool Executable, string[] Bindings) Requirements(string key, IReadOnlyDictionary<string, MessageDefinition> messages)
    {
        var active = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var bindings = new HashSet<string>(StringComparer.Ordinal);
        bool referencesValid = true, executable = true;
        void Visit(string id)
        {
            if (active.Contains(id)) { referencesValid = false; executable = false; return; }
            if (!seen.Add(id)) return;
            if (!messages.TryGetValue(id, out var message)) { referencesValid = false; executable = false; return; }
            if (active.Count >= 64) { referencesValid = false; executable = false; return; }
            active.Add(id); executable &= message.SemanticsConfirmed;
            bindings.UnionWith(message.RequiredBindings);
            foreach (var reference in Walk(message.Nodes).OfType<MessageReference>()) Visit(reference.Key);
            active.Remove(id);
        }
        Visit(key);
        return (referencesValid,executable,bindings.Order(StringComparer.Ordinal).ToArray());
    }
}
