using San9.Importer.San9PkTc101.Containers;
using System.Buffers.Binary;

namespace San9.Importer.San9PkTc101.Text;

// Name-only message reader. Formatting/control sequences are deliberately rejected.
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
        _offsets[count] = _data.Length;
        if (_offsets[0] != end || _offsets.Zip(_offsets.Skip(1)).Any(p => p.First < end || p.First > p.Second || p.Second > _data.Length))
            throw new InvalidDataException("Invalid message offsets.");
    }

    public byte[] Name(int id)
    {
        if (id < 0 || id >= _offsets.Length - 1) throw new InvalidDataException("Missing name resource.");
        var text = _data.AsSpan(_offsets[id], _offsets[id + 1] - _offsets[id]);
        if (text.Length < 3 || !text[^3..].SequenceEqual(new byte[] { 5, 5, 5 })) throw new InvalidDataException("Invalid name terminator.");
        text = text[..^3];
        foreach (byte b in text) if (b < 32) throw new InvalidDataException("Control code in name resource.");
        return text.ToArray();
    }
}
