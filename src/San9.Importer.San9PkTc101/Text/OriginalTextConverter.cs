using San9.Content.Text;

namespace San9.Importer.San9PkTc101.Text;

/// <summary>Decode, edit the canonical source, then produce display text.</summary>
public sealed class OriginalTextConverter
{
    public string Convert(ReadOnlySpan<byte> bytes, int? messageId = null, string catalog = "messages") =>
        Static(new OriginalMessageParser().Parse(bytes.ToArray(), catalog, terminated: false, messageId: messageId));

    public string Canonicalize(ReadOnlySpan<byte> bytes, int? messageId = null, string catalog = "messages") =>
        Static(new OriginalMessageParser().ParseCanonical(bytes.ToArray(), catalog, terminated: false, messageId: messageId));

    private static string Static(ParsedMessage parsed)
    {
        if (!parsed.FullyParsed || !parsed.SemanticsConfirmed)
            throw new InvalidDataException("Dynamic or invalid message requested as static text: " +
                string.Join("; ", parsed.Issues.Select(issue => issue.Reason)));
        return MessageMarkup.Static(parsed.Definition.Nodes);
    }
}
