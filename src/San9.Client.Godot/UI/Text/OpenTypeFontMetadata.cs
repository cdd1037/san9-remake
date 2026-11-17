using System.Buffers.Binary;
using System.Text;

namespace San9.Client.Godot;

internal sealed record FontFaceMetadata(string[] Families, string ChineseName, bool? ChineseMarked, bool Regular);

/// <summary>Reads naming and declared character coverage without initializing a font renderer.</summary>
internal static class OpenTypeFontMetadata
{
    private static readonly Encoding Utf16 = new UnicodeEncoding(true, false, true);
    private readonly record struct Table(uint Offset, uint Length);
    private sealed record FontNames(string[] Families, string ChineseName, bool Regular);

    internal static FontFaceMetadata[] Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        byte[] header = ReadAt(stream, 0, 12);
        if (UInt32(header, 0) != 0x74746366) return [ReadFace(stream, 0)]; // ttcf
        uint count = UInt32(header, 8);
        if (count > (stream.Length - 12) / 4 || count > int.MaxValue / 4)
            throw new InvalidDataException("Invalid OpenType collection directory.");
        byte[] offsets = ReadAt(stream, 12, (int)count * 4);
        var faces = new FontFaceMetadata[count];
        for (int index = 0; index < faces.Length; index++) faces[index] = ReadFace(stream, UInt32(offsets, index * 4));
        return faces;
    }

    private static FontFaceMetadata ReadFace(FileStream stream, long offset)
    {
        byte[] header = ReadAt(stream, offset, 12);
        if (UInt32(header, 0) is not (0x00010000 or 0x4f54544f or 0x74727565))
            throw new InvalidDataException("Unsupported system font format.");
        byte[] directory = ReadAt(stream, offset + 12, UInt16(header, 4) * 16);
        Table? nameTable = null, coverageTable = null;
        for (int index = 0; index < directory.Length; index += 16)
        {
            var table = new Table(UInt32(directory, index + 8), UInt32(directory, index + 12));
            switch (UInt32(directory, index))
            {
                case 0x6e616d65: nameTable = table; break; // name
                case 0x4f532f32: coverageTable = table; break; // OS/2
            }
        }
        var names = nameTable is { } name ? ReadNames(ReadTable(stream, name)) : new FontNames([], "", false);
        bool? chineseMarked = coverageTable is { } coverage ? ReadCoverage(ReadTable(stream, coverage)) : null;
        return new(names.Families, names.ChineseName, chineseMarked, names.Regular);
    }

    private static bool? ReadCoverage(byte[] table)
    {
        if (table.Length < 58) return null;
        uint range1 = UInt32(table, 42), range2 = UInt32(table, 46);
        uint range3 = UInt32(table, 50), range4 = UInt32(table, 54);
        uint codePages1 = 0, codePages2 = 0;
        if (UInt16(table, 0) >= 1 && table.Length >= 86)
        {
            codePages1 = UInt32(table, 78);
            codePages2 = UInt32(table, 82);
        }
        // Unicode range bit 59 declares CJK ideographs; code page bits 18/20 declare simplified/traditional Chinese.
        if ((range2 & (1u << (59 - 32))) != 0 || (codePages1 & ((1u << 18) | (1u << 20))) != 0) return true;
        return (range1 | range2 | range3 | range4 | codePages1 | codePages2) == 0 ? null : false;
    }

    private static FontNames ReadNames(byte[] table)
    {
        int version = UInt16(table, 0), count = UInt16(table, 2), storage = UInt16(table, 4);
        if (version > 1) return new([], "", false);
        Slice(table, 6, count * 12);
        var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string chinese = "";
        int bestPriority = int.MaxValue;
        bool regular = false;
        for (int index = 0; index < count; index++)
        {
            int record = 6 + index * 12;
            int platform = UInt16(table, record), encoding = UInt16(table, record + 2);
            int language = UInt16(table, record + 4), id = UInt16(table, record + 6);
            if (id is not (1 or 2 or 16 or 17 or 21)) continue;
            var bytes = Slice(table, storage + UInt16(table, record + 10), UInt16(table, record + 8));
            string text;
            if (platform == 0 || platform == 3 && encoding is 0 or 1 or 10)
                text = Utf16.GetString(bytes).TrimEnd('\0');
            else if (platform == 1 && encoding == 0 && IsAscii(bytes))
                text = Encoding.ASCII.GetString(bytes).TrimEnd('\0');
            else continue;
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (id is 2 or 17)
            {
                regular |= text.Equals("Regular", StringComparison.OrdinalIgnoreCase) ||
                    text.Equals("Normal", StringComparison.OrdinalIgnoreCase) || text.Equals("Book", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            families.Add(text);
            int languagePriority = ChineseLanguagePriority(table, version, count, storage, platform, language);
            int priority = languagePriority * 2 + (id == 16 ? 0 : 1);
            if (languagePriority < 0 || priority >= bestPriority || !text.Any(character => character is >= '\u3400' and <= '\u9fff')) continue;
            chinese = text;
            bestPriority = priority;
        }
        return new(families.ToArray(), chinese, regular);
    }

    private static int ChineseLanguagePriority(byte[] table, int version, int count, int storage, int platform, int language)
    {
        if (language >= 0x8000 && version == 1)
        {
            int tags = 6 + count * 12;
            int index = language - 0x8000;
            if (index >= UInt16(table, tags)) return -1;
            int record = tags + 2 + index * 4;
            string tag = Utf16.GetString(Slice(table, storage + UInt16(table, record + 2), UInt16(table, record))).TrimEnd('\0');
            if (!tag.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return -1;
            return tag.Equals("zh-cn", StringComparison.OrdinalIgnoreCase) || tag.StartsWith("zh-hans", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
        }
        if (platform == 3 && (language & 0x3ff) == 4) return language is 0x0804 or 0x1004 ? 0 : 1;
        return platform == 0 && language == 0 ? 2 : -1;
    }

    private static bool IsAscii(ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes) if (value > 127) return false;
        return true;
    }

    private static byte[] ReadTable(FileStream stream, Table table)
    {
        if (table.Length > int.MaxValue) throw new InvalidDataException("OpenType table is too large.");
        return ReadAt(stream, table.Offset, (int)table.Length);
    }

    private static byte[] ReadAt(FileStream stream, long offset, int length)
    {
        if (offset < 0 || length < 0 || offset > stream.Length - length)
            throw new InvalidDataException("OpenType data is outside the font file.");
        byte[] bytes = new byte[length];
        stream.Position = offset;
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static ReadOnlySpan<byte> Slice(byte[] bytes, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > bytes.Length - length)
            throw new InvalidDataException("Invalid OpenType metadata range.");
        return bytes.AsSpan(offset, length);
    }

    private static ushort UInt16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16BigEndian(Slice(bytes, offset, 2));
    private static uint UInt32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32BigEndian(Slice(bytes, offset, 4));
}
