using System.Text;
using OpenccNetLib;
using San9.Content.Text;

namespace San9.Importer.San9PkTc101.Text;

/// <summary>Single original-byte to Unicode source/display text boundary.</summary>
public sealed class OriginalTextConverter
{
    private readonly Opencc _converter = new(OpenccConfig.T2S);
    private static readonly Encoding SourceEncoding = CreateEncoding();

    private static Encoding CreateEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(950, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    public TextEntry Convert(ReadOnlySpan<byte> bytes)
    {
        var source = new StringBuilder();
        for (int offset = 0; offset < bytes.Length;)
        {
            byte first = bytes[offset];
            int length = first < 128 ? 1 : 2;
            if (offset + length > bytes.Length) throw new InvalidDataException($"Truncated original text at byte {offset}.");
            if (first < 32 && first is not (9 or 10 or 13) || first == 127)
                throw new InvalidDataException($"Unsupported text control at byte {offset}.");
            string value;
            if (first == 0xFA)
            {
                value = bytes[offset + 1] switch
                {
                    0x40 => "伷", 0x41 => "璝", 0x42 => "寗", 0x43 => "勑", 0x44 => "𩅦", 0x45 => "儁",
                    _ => throw new InvalidDataException($"Unregistered original glyph at byte {offset}: {System.Convert.ToHexString(bytes.Slice(offset, length))}.")
                };
            }
            else
            {
                try { value = SourceEncoding.GetString(bytes.Slice(offset, length)); }
                catch (DecoderFallbackException exception) { throw new InvalidDataException($"Invalid original encoding at byte {offset}.", exception); }
                if (value.Any(c => c is >= '\uE000' and <= '\uF8FF'))
                    throw new InvalidDataException($"Unregistered original glyph at byte {offset}.");
            }
            source.Append(value);
            offset += length;
        }
        string text = source.ToString();
        return new(_converter.Convert(text), text);
    }
}
