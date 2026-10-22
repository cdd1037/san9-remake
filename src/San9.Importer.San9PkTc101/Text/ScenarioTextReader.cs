using San9.Content.Interface;
using San9.Importer.San9PkTc101.Interface;
using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using San9.Content.Text;

namespace San9.Importer.San9PkTc101.Text;

public sealed class ScenarioTextReader(byte[] messages, byte[] executable) : IDisposable
{
    private readonly San9MessageTable _messages = new(messages);
    private readonly PEReader _executable = new(new MemoryStream(executable));
    private readonly Dictionary<string, string> _entries = [];
    private readonly Dictionary<int, MessagePersonProfile> _messagePeople = [];
    private readonly OriginalTextConverter _converter = new();

    public TextCatalog Catalog => new(TextCatalog.CurrentSchemaVersion, new(_entries)) { MessagePeople = new(_messagePeople) };
    // RE-0023 / RE-0180: title selection belongs to the source importer, not the client.
    public void ReadScenarioTitle(int scenarioId, int mode) =>
        Add(TextCatalog.ScenarioTitleKey, _messages.PlainText(checked(2 * scenarioId + (mode == 2 ? 60 : 0))));
    public void MessagePerson(int id, int family, int given, int portrait)
    {
        _messagePeople.Add(id, new(AddOptional($"officer-surname.{id}", _messages.PlainText(1678 + 4 * family)),
            AddOptional($"officer-given-name.{id}", _messages.PlainText(1679 + 4 * given)), $"portrait.{portrait}"));
    }
    public void ReadStrategyHints()
    {
        // RE-0158: execution table header pointers, initialized by the source UI.
        foreach (var (column, rva) in new[] { ("officer", 0x26a510), ("command", 0x26b3fc),
            ("target", 0x26a6ac), ("remaining", 0x26a2fc) })
        {
            int pointer = BinaryPrimitives.ReadInt32LittleEndian(_executable.GetSectionData(rva).GetContent(0, 4).AsSpan());
            var bytes = _executable.GetSectionData(checked(pointer - (int)_executable.PEHeaders.PEHeader!.ImageBase)).GetContent(0, 64).ToArray();
            int end = Array.IndexOf(bytes, (byte)0);
            if (end < 0) throw new InvalidDataException("Unterminated execution panel header.");
            Add(ExecutionPanelSkin.HeaderKey(column), bytes[..end]);
        }
        ReadCommandMenuText();
        Add(WorldEditorText.ConfirmDiscardKey, _messages.MultilineText(7509));
        // RE-SAN9PK-0160: toolbar IDs 1000..1013, table 0x660F60.
        int[] messages = [8261, 8260, 8262, 8439, 8430, 8291, 8293, 8253, 8290, 8289, 8288, 8440, 8442, 8441,
            8287, 8281, 8366, 8280, 8367, 8282, 8279, 8283, 8405, 8537, 8371, 8372, 8159,
            8362, 8355, 8356, 8364, 8365, 8354, 8363,
            // RE-0083: function menu mode 1, descriptor +12.
            8269, 8270, 8547, 8548, 8549, 8550, 8551, 8552, 8096, 8111, 8066,
            // RE-0199: editor cancel hints, enabled/unchanged pairs; close reuses 8354 above.
            8481, 8482, 8517, 8518, 8500, 8501, 8505, 8506, 8510, 8511];
        for (int i = 0; i < messages.Length; i++) Add(StrategyHintText.Key(StrategyHintText.Roles[i]), _messages.PlainText(messages[i]));
    }
    private void ReadCommandMenuText()
    {
        CommandText.AddRemakeEntries(_entries);
        foreach (var (role, rva) in CommandTextSources.Labels)
        {
            var bytes = _executable.GetSectionData(rva).GetContent(0, 256).ToArray();
            int end = Array.IndexOf(bytes, (byte)0);
            if (end < 0) throw new InvalidDataException($"Unterminated command label: {role}.");
            _entries.Add(CommandText.Key(role), CommandTextSources.Format(_converter.Convert(bytes.AsSpan(0, end))));
        }
        foreach (var (role, message) in CommandTextSources.Messages)
            Add(CommandText.Key(role), _messages.MultilineText(message));
        // RE-0137: construction preparation and shared fixed-formation destination workflow.
        foreach (var (role, message) in new[] { ("build",6013), ("transport",6109), ("officers",8168),
            ("soldiers",8169), ("facility",8170), ("need-officers",8037), ("need-soldiers",8157),
            ("need-facility",8171), ("no-facility",8426), ("next",8193), ("cancel",8172), ("policy",8317), ("site",5948), ("facility-catalog",8164),
            ("facility-rockfall",8412), ("facility-stone",8413), ("facility-range",8414) })
            Add(SpecialDeploymentText.Key(role), _messages.PlainText(message));
        foreach (var (role, message) in new[] { ("select",8038), ("select-office",8268), ("individual",8175),
            ("fill",8407), ("promote",8408), ("remove",8409), ("no-vacancy",8406), ("no-target",8375),
            ("accept",8205), ("no-changes",8052), ("cancel",8172) })
            Add(AppointmentText.Key("rank",role), _messages.PlainText(message));
        foreach (var (role, message) in new[] { ("select", 8038), ("target", 8174), ("accept", 8207), ("cancel", 8172) })
            Add(AppointmentText.Key("exile", role), _messages.PlainText(message));
        foreach (string kind in new[] { "bestow", "confiscate" })
            foreach (var (role, message) in new[] { ("select", 8038), ("target", 8174), ("accept", kind == "bestow" ? 8203 : 8204),
                ("cancel", 8172), ("treasure", 8173), ("select-treasure", 8043) })
                Add(AppointmentText.Key(kind, role), _messages.PlainText(message));
        foreach (var (role, message) in new[] { ("select", 8038), ("target", 8174), ("accept", 8206), ("cancel", 8172) })
            Add(AppointmentText.Key("execute", role), _messages.PlainText(message));
        Add(CommandMenuSkin.OfficerSelectionHintKey, _messages.PlainText(8037));
        foreach (var (role, message) in new[] { ("select", 8038), ("target", 8174), ("accept", 8202), ("cancel", 8172) })
            Add(AppointmentText.Key("reward", role), _messages.PlainText(message));
        foreach (var (role, message) in new[] { ("select", 8050), ("target", 8174), ("accept", 8201), ("cancel", 8172) })
            Add(AppointmentText.Key("strategist", role), _messages.PlainText(message));
        foreach (var entry in Interface.CommandMenuSources.Entries)
        {
            Add(CommandMenuSkin.DescriptionKey(entry.Id), _messages.MultilineText(entry.Message));
            Add(CommandMenuSkin.HintKey(entry.Id), _messages.PlainText(entry.Hint));
        }
        foreach (var (role, message) in Interface.CommandMenuSources.Reasons)
            Add(CommandMenuSkin.ReasonKey(role), _messages.PlainText(message));
        foreach (var (role, message, amount) in Interface.CommandMenuSources.NumberedReasons)
        {
            var parsed = new OriginalMessageParser().Parse(_messages.Record(message));
            if (!parsed.FullyParsed || !parsed.SemanticsConfirmed) throw new InvalidDataException($"Unconfirmed command reason {role} (message {message}).");
            var nodes = parsed.Definition.Nodes.TakeWhile(node => node is not MessageReturn).Select(node => node is MessageValue { Expression: { Operation: "binding", Binding: "number.0" }, Format: null }
                ? new MessageLiteral(amount.ToString(System.Globalization.CultureInfo.InvariantCulture)) : node);
            _entries.Add(CommandMenuSkin.ReasonKey(role), MessageMarkup.Static(nodes));
        }
    }

    private string Add(string key, byte[] source)
    {
        _entries.Add(key, _converter.Convert(source)); return key;
    }
    private string? AddOptional(string key, byte[] source)
    {
        var entry = _converter.Convert(source);
        if (string.IsNullOrWhiteSpace(entry)) return null;
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
    public string? FormationDescription(int id) => AddOptional($"formation-description.{id}", _messages.PlainText(5365 + 3 * id));
    public string OfficeDescription(int id) => Add($"office-description.{id}", _messages.PlainText(7770 + id));
    public string Office(string key, int id) => Add(key, _messages.PlainText(5065 + 2 * id));
    public string City(string key, int code) => Add(key, _messages.PlainText(721 + 2 * code));
    public string Region(string key, int code) => Add(key, _messages.PlainText(166 + 2 * code));
    public string? FacilitySuffix(string key, int type) => AddOptional(key, TypeSuffix(type));
    public string Settlement(string key, int code, int type) => Add(key, [.. _messages.PlainText(166 + 2 * code), .. TypeSuffix(type)]);
    public string Surname(int family)
    {
        if (family >= 700) throw new InvalidDataException("Unsupported generated surname.");
        return _converter.Convert(_messages.PlainText(1678 + 4 * family));
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
