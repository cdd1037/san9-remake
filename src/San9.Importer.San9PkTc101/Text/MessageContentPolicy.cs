using System.Security.Cryptography;
using San9.Content.Text;

namespace San9.Importer.San9PkTc101.Text;

public sealed record MessageContentAdaptation(string Key, string SourceSha256, string Decision, string DiscardedHex);

/// <summary>Reviewed remake content decisions; strict source decoding remains unchanged.</summary>
public static class MessageContentPolicy
{
    public static MessageContentAdaptation? Apply(string key, byte[] raw, string catalog,
        IDictionary<string, MessageDefinition> messages, OriginalMessagePresentation presentation = OriginalMessagePresentation.Standard)
    {
        MessageDefinition replacement;
        string decision, discarded;
        if (key == "messages.8677")
        {
            // The screenshot confirms the appearance glossary's fixed counts and yellow emphasis,
            // not the standalone meaning of ESC H. Only this verified source record is replaced.
            if (Convert.ToHexString(SHA256.HashData(raw)) != "8EB8981015ABEDF88CE1A287A5F9F2F10F01529809025BD83B903626004891D6")
                throw new InvalidDataException($"Appearance help source differs from the reviewed record: {key}.");
            replacement = new([new MessageLiteral("男性容貌有"),
                new MessageStyled("yellow", [new MessageLiteral("70")]), new MessageLiteral("种，\n女性容貌有"),
                new MessageStyled("yellow", [new MessageLiteral("30")]), new MessageLiteral("种。"), new MessageReturn()]);
            decision = "Appearance glossary: fixed counts 70/30 with yellow foreground emphasis; replace source presentation controls.";
            discarded = "1B481B48";
        }
        else if (key is "messages.12629" or "messages.12630" or "messages.12631" or "messages.12712")
        {
            if (!raw.AsSpan().EndsWith(new byte[] { 5, 5, 5, 1, (byte)'J' }))
                throw new InvalidDataException($"Message tail differs from the reviewed terminal-plus-incomplete-jump pattern: {key}.");
            var parsed = new OriginalMessageParser().ParseSyntax(raw[..^2], catalog, presentation: presentation);
            if (!parsed.FullyParsed || !parsed.SemanticsConfirmed)
                throw new InvalidDataException($"Message has an unsupported body before its reviewed tail: {key}.");
            replacement = parsed.Definition;
            decision = "End at the complete terminal delimiter; discard the trailing jump without a target, preserving body and branches.";
            discarded = "014A";
        }
        else return null;
        messages[key] = replacement;
        return new(key, Convert.ToHexString(SHA256.HashData(raw)), decision, discarded);
    }
}
