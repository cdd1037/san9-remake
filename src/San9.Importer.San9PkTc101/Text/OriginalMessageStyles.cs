using San9.Content.Text;

namespace San9.Importer.San9PkTc101.Text;

public enum OriginalMessagePresentation { Standard, Help }

// This node is consumed before publication; it is never a content contract or serialized node.
internal sealed record OriginalStyleChange(string? Preset) : MessageNode;

/// <summary>Lower source style state into lexical scopes, including the continuations of source calls.</summary>
internal static class OriginalMessageStyles
{
    // RE-0168: display properties emit their own escapes, independently of explicit source style tokens.
    // Only display nodes are decorated; numeric properties and plain name storage remain unchanged.
    public static MessageNode[] DisplayValue(MessageExpression expression, OriginalMessagePresentation presentation)
    {
        var value = new MessageValue(expression);
        if (expression is not { Operation: "binding", Binding: { } binding }) return [value];
        string[] parts = binding.Split('.');
        if (parts.Length != 3) return [value];
        MessageExpression Property(string name) => new("binding", $"{parts[0]}.{parts[1]}.{name}");
        MessageStyled Styled(string color, MessageExpression e) => new(
            presentation == OriginalMessagePresentation.Help ? color : color + "-outline", [new MessageValue(e)]);
        if (parts is ["person", _, "office-and-name"])
            return [new MessageConditional(new(">", Arguments: [Property("office-command-capacity"), new("constant", Number: 10000)]),
                [Styled("green", Property("office-name")), new MessageLiteral("‧")]), Styled("blue", Property("name"))];
        if (parts is ["unit", _, "display-name"])
            return [new MessageChoice(Property("is-embedded"),
                [[Styled("blue", Property("commander-name")), new MessageLiteral("队")],
                 [Styled("green", Property("location-name"))]])];
        string? color = (parts[0], parts[2]) switch
        {
            ("person", "name" or "surname" or "given-name" or "courtesy-name") => "blue",
            ("force", "leader-name" or "direct-leader-name") => "blue",
            ("person", "location-name") => "green",
            ("building", "name-with-type" or "name" or "owner-name") => "green",
            ("force", "name" or "country-name" or "rank-name") => "green",
            ("treasure", "name") => "green",
            ("tactic", "name") => "green",
            _ => null
        };
        return [color is null ? value : Styled(color, expression)];
    }

    public static MessageDefinition Compile(MessageDefinition source,
        IReadOnlyDictionary<string, MessageDefinition>? catalog = null)
    {
        var styled = new Dictionary<string, bool>(StringComparer.Ordinal);
        bool HasStyles(MessageDefinition message, HashSet<string> visiting)
        {
            foreach (var node in MessageGraph.Walk(message.Nodes))
            {
                if (node is OriginalStyleChange) return true;
                if (node is not MessageReference reference || catalog is null || !catalog.TryGetValue(reference.Key, out var child)) continue;
                if (styled.TryGetValue(reference.Key, out bool known)) { if (known) return true; continue; }
                if (!visiting.Add(reference.Key)) continue;
                bool found = HasStyles(child, visiting);
                visiting.Remove(reference.Key);
                // Do not cache negatives from a cyclic traversal; dependency validation owns cycles.
                if (found) { styled[reference.Key] = true; return true; }
            }
            return false;
        }
        if (!HasStyles(source, [])) return source;

        int emitted = 0;
        MessageNode[] Sequence(MessageNode[] nodes, int start, string? preset,
            Func<string?, MessageNode[]> done, Func<string?, MessageNode[]> returned, int depth)
        {
            if (depth >= 64) throw new InvalidDataException("Source style expansion exceeds 64 levels.");
            List<MessageNode> output = [];
            void Emit(MessageNode node)
            {
                if (++emitted > 10000) throw new InvalidDataException("Source style expansion exceeds 10000 nodes.");
                // Merge adjacent runs; dynamic values and references may share one lexical style.
                if (preset is null) output.Add(node);
                else if (output.LastOrDefault() is MessageStyled previous && previous.Preset == preset)
                    output[^1] = previous with { Body = [.. previous.Body, node] };
                else output.Add(new MessageStyled(preset, [node]));
            }
            for (int i = start; i < nodes.Length; i++)
            {
                int next = i + 1;
                MessageNode[] Rest(string? state) => Sequence(nodes, next, state, done, returned, depth + 1);
                switch (nodes[i])
                {
                    case OriginalStyleChange change: preset = change.Preset; break;
                    case MessageReturn:
                        output.AddRange(returned(preset));
                        return output.ToArray();
                    case MessageConditional conditional:
                        // Keep the false continuation flat: a long list of source conditions is not a deep tree.
                        output.Add(new MessageConditional(conditional.Predicate,
                            [.. Sequence(conditional.Body, 0, preset, Rest, returned, depth + 1), new MessageReturn()]));
                        output.AddRange(Rest(preset));
                        return output.ToArray();
                    case MessageChoice choice:
                        output.Add(choice with { Choices = choice.Choices.Select(branch =>
                            Sequence(branch, 0, preset, Rest, returned, depth + 1)).ToArray() });
                        return output.ToArray();
                    case MessageRandomChoice choice:
                        output.Add(choice with { Choices = choice.Choices.Select(branch =>
                            Sequence(branch, 0, preset, Rest, returned, depth + 1)).ToArray() });
                        return output.ToArray();
                    case MessageReference reference when catalog is not null && catalog.TryGetValue(reference.Key, out var child)
                        && HasStyles(child, []):
                        Func<string?, MessageNode[]> continuation = reference.Tail ? returned : Rest;
                        output.AddRange(Sequence(child.Nodes, 0, preset, continuation, continuation, depth + 1));
                        return output.ToArray();
                    case MessageReference reference:
                        // A style-free child inherits the lexical scope and cannot change its caller's style.
                        Emit(reference with { Tail = false });
                        if (reference.Tail) { output.AddRange(returned(preset)); return output.ToArray(); }
                        break;
                    default: Emit(nodes[i]); break;
                }
            }
            output.AddRange(done(preset));
            return output.ToArray();
        }
        return new(Sequence(source.Nodes, 0, null, _ => [], _ => [new MessageReturn()], 0));
    }
}
