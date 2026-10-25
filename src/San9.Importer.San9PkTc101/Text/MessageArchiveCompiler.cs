using System.Security.Cryptography;
using System.Text.Json;
using San9.Content.Text;
using San9.Content.Packages;

namespace San9.Importer.San9PkTc101.Text;

public sealed record MessageRecordAudit(string Key, string Encoding, int Offset, int Length, int ConsumedBytes,
    bool FullyParsed, bool SemanticsConfirmed, bool ReferencesValid, bool DependenciesConfirmed, bool BindingFree, string[] RequiredBindings,
    string RawHex, MessageIssue[] Issues);
public sealed record MessageArchiveAudit(string File, string Sha256, string Encoding, string TrailerHex, MessageRecordAudit[] Records);

public static class MessageArchiveCompiler
{
    public static (Dictionary<string, MessageDefinition> Messages, MessageArchiveAudit Audit) Compile(byte[] bytes, string file, string catalog,
        OriginalMessagePresentation presentation = OriginalMessagePresentation.Standard)
    {
        var table = new San9MessageTable(bytes);
        Dictionary<string, MessageDefinition> messages = [];
        List<MessageRecordAudit> records = [];
        foreach (var (key, raw, encoding, offset, parsed) in ReadRecords(table, catalog, presentation))
        {
            messages.Add(key, parsed.Definition);
            records.Add(new(key, encoding, offset, raw.Length, parsed.ConsumedBytes,
                parsed.FullyParsed, parsed.SemanticsConfirmed, true, parsed.SemanticsConfirmed,
                false, [],
                Convert.ToHexString(raw), parsed.Issues));
        }
        var source = new Dictionary<string, MessageDefinition>(messages);
        for (int i = 0; i < records.Count; i++)
        {
            var record = records[i];
            try { messages[record.Key] = OriginalMessageStyles.Compile(source[record.Key], source); }
            catch (InvalidDataException error)
            {
                messages[record.Key] = new([]);
                records[i] = record with { SemanticsConfirmed = false, DependenciesConfirmed = false,
                    Issues = [.. record.Issues, new(0, error.Message)] };
            }
        }
        var confirmed = records.Where(r => r.FullyParsed && r.SemanticsConfirmed)
            .ToDictionary(r => r.Key, r => source[r.Key]);
        for (int i = 0; i < records.Count; i++)
        {
            // Source dependencies remain authoritative even when style-bearing calls were expanded.
            var requirements = MessageGraph.Requirements(records[i].Key,source);
            records[i] = records[i] with { ReferencesValid = requirements.ReferencesValid, DependenciesConfirmed = MessageGraph.ReferencesValid(records[i].Key, confirmed),
                BindingFree = requirements.Bindings.Length == 0, RequiredBindings = requirements.Bindings };
        }
        return (messages, new(file, Convert.ToHexString(SHA256.HashData(bytes)), "cp950", table.TrailerHex, records.ToArray()));
    }

    public static (Dictionary<string, MessageDefinition> Messages, MessageContentAdaptation[] Adaptations) CompileForPublication(
        byte[] bytes, string file, string catalog, OriginalMessagePresentation presentation = OriginalMessagePresentation.Standard)
    {
        var table = new San9MessageTable(bytes);
        return CompileRecordsForPublication(ReadRecords(table, catalog, presentation).Select(row => (row.Key, row.Raw, row.Parsed)),
            file, catalog, presentation);
    }

    internal static (Dictionary<string, MessageDefinition> Messages, MessageContentAdaptation[] Adaptations) CompileRecordsForPublication(
        IEnumerable<(string Key, byte[] Raw, ParsedMessage Parsed)> records, string file, string catalog,
        OriginalMessagePresentation presentation = OriginalMessagePresentation.Standard)
    {
        List<MessageContentAdaptation> adaptations = [];
        Dictionary<string, MessageDefinition> source = [];
        foreach (var (key, raw, parsed) in records)
        {
            source.Add(key, parsed.Definition);
            if (MessageContentPolicy.Apply(key, raw, catalog, source, presentation) is { } adaptation)
                adaptations.Add(adaptation);
            else if (!parsed.FullyParsed || !parsed.SemanticsConfirmed)
                throw new InvalidDataException($"Message {key} in {file} cannot be published: " +
                    string.Join("; ", parsed.Issues.Select(issue => $"byte {issue.Offset}: {issue.Reason}")));
        }
        // Apply all reviewed content decisions and validate source calls before expanding styles.
        foreach (string key in source.Keys)
            if (!MessageGraph.ReferencesValid(key, source))
                throw new InvalidDataException($"Invalid or cyclic message references: {key}.");
        Dictionary<string, MessageDefinition> messages = [];
        foreach (var (key, definition) in source)
        {
            try { messages.Add(key, OriginalMessageStyles.Compile(definition, source)); }
            catch (InvalidDataException error)
            { throw new InvalidDataException($"Message {key} style conversion failed: {error.Message}", error); }
        }
        MessageGraph.Validate(messages);
        return (messages, adaptations.ToArray());
    }

    private static IEnumerable<(string Key, byte[] Raw, string Encoding, int Offset, ParsedMessage Parsed)> ReadRecords(
        San9MessageTable table, string catalog, OriginalMessagePresentation presentation)
    {
        var parser = new OriginalMessageParser();
        for (int i = 0; i < table.Count; i++)
        {
            var raw = table.Record(i);
            // TC-101 retains twenty CP932 scenario placeholders, identified by exact byte signatures.
            bool japanese = i is >= 40 and <= 59 && (raw.AsSpan().SequenceEqual(Convert.FromHexString("92C789C1835683693F8349050505")) ||
                raw.AsSpan().SequenceEqual(Convert.FromHexString("92C789C183568369838A8349050505")));
            ParsedMessage parsed;
            if (japanese)
            {
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                string value = MessageMarkup.Escape(System.Text.Encoding.GetEncoding(932, System.Text.EncoderFallback.ExceptionFallback,
                    System.Text.DecoderFallback.ExceptionFallback).GetString(raw.AsSpan(0, raw.Length - 3)));
                parsed = new(new([new MessageLiteral(value), new MessageReturn()]), raw.Length, true, []);
            }
            else parsed = parser.ParseSyntax(raw, catalog, presentation: presentation, messageId: i);
            yield return ($"{catalog}.{i}", raw, japanese ? "cp932" : "cp950", table.Offset(i), parsed);
        }
    }

    public static void Export(string dataDirectory, string output, Action<string>? progress = null)
    {
        Directory.CreateDirectory(output);
        foreach (var (file, catalog) in new[] { ("M_MsgPK.s9", "messages"), ("M_RtdnPK.s9", "biographies"), ("M_Msg.s9", "base-messages"), ("M_Rtdn.s9", "base-biographies") })
        {
            var (messages, audit) = Compile(File.ReadAllBytes(Path.Combine(dataDirectory, file)), file, catalog);
            File.WriteAllText(Path.Combine(output, catalog + ".json"), JsonSerializer.Serialize(messages, PackageFiles.JsonOptions), System.Text.Encoding.UTF8);
            File.WriteAllText(Path.Combine(output, catalog + "-audit.json"), JsonSerializer.Serialize(audit, PackageFiles.JsonOptions), System.Text.Encoding.UTF8);
            progress?.Invoke($"{file}: {audit.Records.Length} records; {audit.Records.Count(r => r.FullyParsed)} fully consumed; {audit.Records.Count(r => r.SemanticsConfirmed)} confirmed; {audit.Records.Count(r => r.BindingFree && r.DependenciesConfirmed)} binding-free.");
        }
    }
}
