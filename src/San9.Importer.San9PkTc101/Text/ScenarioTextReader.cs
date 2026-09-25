using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using San9.Content.Text;

namespace San9.Importer.San9PkTc101.Text;

public sealed class ScenarioTextReader(byte[] messages, byte[] executable) : IDisposable
{
    private readonly San9MessageTable _messages = new(messages);
    private readonly PEReader _executable = new(new MemoryStream(executable));
    private readonly Dictionary<string, TextEntry> _entries = [];
    private readonly OriginalTextConverter _converter = new();

    public TextCatalog Catalog => new(3, new(_entries));
    public void ReadStrategyHints()
    {
        // RE-SAN9PK-0160: toolbar IDs 1000..1013, table 0x660F60.
        int[] messages = [8261, 8260, 8262, 8439, 8430, 8291, 8293, 8253, 8290, 8289, 8288, 8440, 8442, 8441,
            8287, 8281, 8366, 8280, 8367, 8282, 8279, 8283, 8405, 8537, 8371, 8372, 8159,
            8362, 8355, 8356, 8364, 8365, 8354, 8363];
        for (int i = 0; i < messages.Length; i++) Add(StrategyHintText.Key(StrategyHintText.Roles[i]), _messages.PlainText(messages[i]));
    }
    private string Add(string key, byte[] source)
    {
        _entries.Add(key, _converter.Convert(source)); return key;
    }
    private string? AddOptional(string key, byte[] source)
    {
        var entry = _converter.Convert(source);
        if (string.IsNullOrWhiteSpace(entry.Text)) return null;
        _entries.Add(key, entry); return key;
    }
    public string? Courtesy(string key, int code)
    {
        if (code >= 700) throw new InvalidDataException("Unsupported generated courtesy name.");
        var source = _messages.PlainText(1680 + 4 * code);
        return AddOptional(key, source);
    }
    public string? Biography(string key, int code, San9MessageTable biographies)
    {
        if (code is >= 563 and <= 569) return null;
        var source = biographies.MultilineText(code >= 570 ? code - 7 : code);
        return AddOptional(key, source);
    }
    public string CountryTitle(string key, int id) => Add(key, _messages.PlainText(San9ScenarioCountryTitleArchive.GetNameMessageId(id)));
    // Rank 8 is the valid lowest rank, but its name resource 1668 is empty.
    // Reuse the source's explicit "none" label (5217) for this semantic state.
    public string Rank(string key, int id)
    {
        if (id != 8) Add($"rank-description.{id}", _messages.PlainText(7761 + id));
        return Add(key, _messages.PlainText(id == 8 ? 5217 : 1652 + 2 * id));
    }
    public string Province(int id) => Add($"province.{id}", _messages.PlainText(San9ScenarioAreaArchive.GetNameMessageId(id)));
    public string TreasureDescription(int id, int resourceId)
    {
        if ((uint)resourceId >= 100) throw new InvalidDataException("Unsupported expansion treasure description.");
        return Add(TreasureText.DescriptionKey(id), _messages.MultilineText(7641 + resourceId));
    }
    public string Treasure(string key, int id) => Add(key, _messages.PlainText(1392 + 2 * id));
    public string Tactic(int id) => Add($"tactic.{id}", _messages.PlainText(1219 + 3 * id));
    public string TacticDescription(int id) => Add($"tactic-description.{id}", _messages.PlainText(1221 + 3 * id));
    public string Formation(int id) => Add($"formation.{id}", _messages.PlainText(5363 + 3 * id));
    public string OfficeDescription(int id) => Add($"office-description.{id}", _messages.PlainText(7770 + id));
    public string Office(string key, int id) => Add(key, _messages.PlainText(5065 + 2 * id));
    public string City(string key, int code) => Add(key, _messages.PlainText(721 + 2 * code));
    public string Region(string key, int code) => Add(key, _messages.PlainText(166 + 2 * code));
    public string? FacilitySuffix(string key, int type) => AddOptional(key, TypeSuffix(type));
    public string Settlement(string key, int code, int type) => Add(key, [.. _messages.PlainText(166 + 2 * code), .. TypeSuffix(type)]);
    public string Surname(int family)
    {
        if (family >= 700) throw new InvalidDataException("Unsupported generated surname.");
        return _converter.Convert(_messages.PlainText(1678 + 4 * family)).Text;
    }
    public string Person(string key, int family, int given)
    {
        if (family >= 700 || given >= 700) throw new InvalidDataException("Unsupported generated person name.");
        return Add(key, [.. _messages.PlainText(1678 + 4 * family), .. _messages.PlainText(1679 + 4 * given)]);
    }
    private byte[] TypeSuffix(int type)
    {
        int label = type switch { 1 => 1, 6 => 3, 7 => 4, 8 => 5, 9 => 6, 10 or 11 => 7, 12 or 13 => 8, 14 or 15 => 9, 16 => 10, _ => -1 };
        if (label < 0) return [];
        var table = _executable.GetSectionData(0x26a90c).GetContent(4 * label, 4);
        int pointer = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan());
        int rva = checked(pointer - (int)_executable.PEHeaders.PEHeader!.ImageBase);
        var data = _executable.GetSectionData(rva).GetContent(0, 64).ToArray();
        int end = Array.IndexOf(data, (byte)0);
        if (end < 0) throw new InvalidDataException("Unterminated building label.");
        return data[..end];
    }
    public void Dispose() => _executable.Dispose();
}
