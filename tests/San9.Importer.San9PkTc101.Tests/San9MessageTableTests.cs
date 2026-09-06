using San9.Importer.San9PkTc101.Text;
using San9.Importer.San9PkTc101.Containers;
using System.Buffers.Binary;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class San9MessageTableTests
{
    private static byte[] Archive(byte[] payload, int? decodedSize = null)
    {
        byte[] archive = new byte[288 + payload.Length];
        "LS11"u8.CopyTo(archive);
        for (int i = 0; i < 256; i++) archive[16 + i] = (byte)i;
        BinaryPrimitives.WriteInt32BigEndian(archive.AsSpan(272), payload.Length);
        BinaryPrimitives.WriteInt32BigEndian(archive.AsSpan(276), decodedSize ?? payload.Length);
        BinaryPrimitives.WriteInt32BigEndian(archive.AsSpan(280), 288);
        payload.CopyTo(archive, 288); return archive;
    }
    private static byte[] Table(params byte[][] names)
    {
        byte[] data = new byte[2 + 4 * names.Length + names.Sum(n => n.Length + 3)];
        BinaryPrimitives.WriteUInt16LittleEndian(data, (ushort)names.Length);
        int position = 2 + 4 * names.Length;
        for (int i = 0; i < names.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(2 + i * 4), position);
            names[i].CopyTo(data, position); position += names[i].Length;
            data[position++] = 5; data[position++] = 5; data[position++] = 5;
        }
        return data;
    }
    private static byte[] Codes(params int[] codes)
    {
        List<int> bits = [];
        foreach (int code in codes)
        {
            int length = 1;
            while (code >= (1 << (length + 1)) - 2) length++;
            bits.AddRange(Enumerable.Repeat(1, length - 1)); bits.Add(0);
            int value = code - ((1 << length) - 2);
            for (int i = length - 1; i >= 0; i--) bits.Add((value >> i) & 1);
        }
        byte[] bytes = new byte[(bits.Count + 7) / 8];
        for (int i = 0; i < bits.Count; i++) bytes[i / 8] |= (byte)(bits[i] << (7 - i % 8));
        return bytes;
    }
    [Fact]
    public void StoredNamesKeepSourceTextAndExplicitExtension()
    {
        var table = new San9MessageTable(Archive(Table([0xB5, 0x4C], [0xFA, 0x40], [])));
        Assert.Equal("無", new OriginalTextConverter().Convert(table.PlainText(0)).SourceText);
        Assert.Equal("伷", new OriginalTextConverter().Convert(table.PlainText(1)).SourceText);
        Assert.Equal("", new OriginalTextConverter().Convert(table.PlainText(2)).SourceText);
        var extensions = new San9MessageTable(Archive(Table([0xFA, 0x41], [0xFA, 0x45])));
        Assert.Equal("璝", new OriginalTextConverter().Convert(extensions.PlainText(0)).SourceText); Assert.Equal("儁", new OriginalTextConverter().Convert(extensions.PlainText(1)).SourceText);
        Assert.Throws<InvalidDataException>(() => table.PlainText(3));
    }
    [Fact]
    public void BiographyAllowsLineBreaksWithoutRelaxingOtherTextControls()
    {
        var table = new San9MessageTable(Archive(Table([65, 10, 66], [65, 1, 66])));
        Assert.Equal(new byte[] { 65, 10, 66 }, table.MultilineText(0));
        var terminatedArchive = new San9MessageTable(Archive([.. Table([65, 10, 66]), 0]));
        Assert.Equal(table.MultilineText(0), terminatedArchive.MultilineText(0));
        Assert.Throws<InvalidDataException>(() => table.PlainText(0));
        Assert.Throws<InvalidDataException>(() => table.MultilineText(1));
    }
    [Fact]
    public void CompressedLiteralsAndOverlappingCopiesDecodeToDeclaredSize()
    {
        Assert.Equal("abcabc"u8.ToArray(), Ls11Archive.Decode(Archive(Codes(97, 98, 99, 259, 0), 6)));
        Assert.Equal("aaaaaa"u8.ToArray(), Ls11Archive.Decode(Archive(Codes(97, 257, 4), 6)));
        Assert.Throws<InvalidDataException>(() => Ls11Archive.Decode(Archive(Codes(259, 0), 6)));
    }
    [Fact]
    public void CorruptOffsetsControlsTerminatorsAndBlockBoundsAreRejected()
    {
        var data = Table("City"u8.ToArray()); data[2] = 1;
        Assert.Throws<InvalidDataException>(() => new San9MessageTable(Archive(data)));
        var control = new San9MessageTable(Archive(Table([65, 10, 66])));
        Assert.Throws<InvalidDataException>(() => control.PlainText(0));
        data = Table([65]); data[^1] = 0;
        Assert.Throws<InvalidDataException>(() => new San9MessageTable(Archive(data)).PlainText(0));
        var archive = Archive(Table([65])); archive[283] = 0;
        Assert.Throws<InvalidDataException>(() => new San9MessageTable(archive));
        Assert.Throws<InvalidDataException>(() => Ls11Archive.Decode(new byte[8]));
        Assert.Throws<InvalidDataException>(() => new OriginalTextConverter().Convert(new San9MessageTable(Archive(Table([0xFA, 0x42]))).PlainText(0)));
    }
}
