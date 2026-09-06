using San9.Importer.San9PkTc101.Containers;
using System.Buffers.Binary;

namespace San9.Importer.San9PkTc101.Text;

// Plain-text message reader. Formatting/control sequences are deliberately rejected.
public sealed class San9MessageTable
{
    private readonly byte[] _data;
    private readonly int[] _offsets;
    public San9MessageTable(byte[] source)
    {
        _data = Ls11Archive.Decode(source);
        if (_data.Length < 2) throw new InvalidDataException("Missing message table.");
        int count = BinaryPrimitives.ReadUInt16LittleEndian(_data);
        int end = checked(2 + count * 4);
        if (count == 0 || end > _data.Length) throw new InvalidDataException("Invalid message count.");
        _offsets = new int[count + 1];
        for (int i = 0; i < count; i++) _offsets[i] = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(2 + i * 4));
        // Some text archives include one container-level NUL after the last message terminator.
        _offsets[count] = _data.AsSpan().EndsWith(new byte[] { 5, 5, 5, 0 }) ? _data.Length - 1 : _data.Length;
        if (_offsets[0] != end || _offsets.Zip(_offsets.Skip(1)).Any(p => p.First < end || p.First > p.Second || p.Second > _data.Length))
            throw new InvalidDataException("Invalid message offsets.");
    }

    public byte[] PlainText(int id) => ReadText(id, false);
    public byte[] MultilineText(int id) => ReadText(id, true);

    private byte[] ReadText(int id, bool multiline)
    {
        if (id < 0 || id >= _offsets.Length - 1) throw new InvalidDataException("Missing plain-text resource.");
        var text = _data.AsSpan(_offsets[id], _offsets[id + 1] - _offsets[id]);
        if (text.Length < 3 || !text[^3..].SequenceEqual(new byte[] { 5, 5, 5 })) throw new InvalidDataException($"Invalid plain-text terminator in resource {id}.");
        text = text[..^3];
        foreach (byte b in text) if (b < 32 && !(multiline && b == 10)) throw new InvalidDataException("Control code in plain-text resource.");
        return text.ToArray();
    }
}
