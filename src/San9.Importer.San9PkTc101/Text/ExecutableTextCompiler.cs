using System.Globalization;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using San9.Content.Packages;

namespace San9.Importer.San9PkTc101.Text;

public static class ExecutableTextCompiler
{
    // IDA supplies evidence locators only. All decoding uses the product parser.
    public static void Export(string executable, string evidence, string output)
    {
        byte[] bytes = File.ReadAllBytes(executable);
        using var pe = new PEReader(new MemoryStream(bytes));
        using var document = JsonDocument.Parse(File.ReadAllText(evidence,Encoding.UTF8));
        if (!string.Equals(document.RootElement.GetProperty("sha256").GetString(),Convert.ToHexString(SHA256.HashData(bytes)),StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("EXE string evidence belongs to a different sample.");
        var parser = new OriginalMessageParser();
        var rows = new List<object>();
        foreach (var item in document.RootElement.GetProperty("strings").EnumerateArray())
        {
            string address=item.GetProperty("address").GetString()!;
            long va=long.Parse(address[2..],NumberStyles.HexNumber,CultureInfo.InvariantCulture);
            byte[] raw=Convert.FromHexString(item.GetProperty("rawHex").GetString()!);
            var actual=pe.GetSectionData(checked((int)(va-(long)pe.PEHeaders.PEHeader!.ImageBase))).GetContent(0,raw.Length);
            if(!actual.AsSpan().SequenceEqual(raw)) throw new InvalidDataException($"EXE evidence byte mismatch at {address}.");
            string usage=item.GetProperty("usage").GetString()!;
            bool messageCandidate=usage=="message-candidate";
            bool formatCandidate=messageCandidate && System.Text.RegularExpressions.Regex.IsMatch(Encoding.ASCII.GetString(raw),@"%[-+ #0]*\d*(?:\.\d+)?[sdicuoxXf]");
            ParsedMessage? parsed=messageCandidate ? parser.Parse(raw,terminated:false,formatString:formatCandidate) : null;
            rows.Add(new {Address=address,RawHex=Convert.ToHexString(raw),References=item.GetProperty("references").Clone(),
                Usage=usage,MessageCandidate=messageCandidate,FormatCandidate=formatCandidate,Parsed=parsed,
                SyntaxSupported=parsed is null ? null : (bool?)(parsed.FullyParsed && parsed.Definition.SemanticsConfirmed),
                GameplayBound=false,
                Boundary=item.GetProperty("boundary").GetString()});
        }
        File.WriteAllText(Path.Combine(output,"executable-audit.json"),JsonSerializer.Serialize(rows,PackageFiles.JsonOptions),Encoding.UTF8);
    }
}
