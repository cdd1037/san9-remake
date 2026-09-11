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
    public static (Dictionary<string, MessageDefinition> Messages, MessageArchiveAudit Audit) Compile(byte[] bytes, string file, string catalog)
    {
        var table = new San9MessageTable(bytes);
        var parser = new OriginalMessageParser();
        Dictionary<string, MessageDefinition> messages = [];
        List<MessageRecordAudit> records = [];
        for (int i = 0; i < table.Count; i++)
        {
            string key = $"{catalog}.{i}";
            var raw = table.Record(i);
            // TC-101 retains twenty identical CP932 scenario placeholders; this is an explicit byte signature, not an encoding fallback.
            bool japanese = i is >= 40 and <= 59 && (raw.AsSpan().SequenceEqual(Convert.FromHexString("92C789C1835683693F8349050505")) ||
                raw.AsSpan().SequenceEqual(Convert.FromHexString("92C789C183568369838A8349050505")));
            ParsedMessage parsed;
            if (japanese)
            {
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                string value = MessageMarkup.Escape(System.Text.Encoding.GetEncoding(932, System.Text.EncoderFallback.ExceptionFallback, System.Text.DecoderFallback.ExceptionFallback).GetString(raw.AsSpan(0, raw.Length - 3)));
                parsed = new(new([new MessageLiteral(value, value), new MessageReturn()], true, []), raw.Length, true, []);
            }
            else parsed = parser.Parse(raw, catalog);
            messages.Add(key, parsed.Definition);
            records.Add(new(key, japanese ? "cp932" : "cp950", table.Offset(i), raw.Length, parsed.ConsumedBytes,
                parsed.FullyParsed, parsed.Definition.SemanticsConfirmed, true, parsed.Definition.SemanticsConfirmed,
                parsed.Definition.RequiredBindings.Length == 0, parsed.Definition.RequiredBindings,
                Convert.ToHexString(raw), parsed.Issues));
        }
        for (int i = 0; i < records.Count; i++)
        {
            var requirements = MessageGraph.Requirements(records[i].Key,messages);
            records[i] = records[i] with { ReferencesValid = requirements.ReferencesValid, DependenciesConfirmed = requirements.Executable,
                BindingFree = requirements.Bindings.Length == 0, RequiredBindings = requirements.Bindings };
        }
        return (messages, new(file, Convert.ToHexString(SHA256.HashData(bytes)), "cp950", table.TrailerHex, records.ToArray()));
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
