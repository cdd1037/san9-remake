using San9.Importer.San9PkTc101.Text;
using San9.Importer.San9PkTc101.Containers;
using System.Buffers.Binary;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class San9MessageTableTests
{
    [Theory]
    [InlineData(0, "Standard title")]
    [InlineData(1, "Standard title")]
    [InlineData(2, "Tutorial title")]
    public void ScenarioTitleUsesTheHeaderModeAndSourceMessage(int mode, string expected)
    {
        var records = Enumerable.Range(0, 63).Select(_ => Array.Empty<byte>()).ToArray();
        records[2] = "Standard title"u8.ToArray();
        records[62] = "Tutorial title"u8.ToArray();
        using var reader = new ScenarioTextReader(Archive(Table(records)),
            File.ReadAllBytes(typeof(ScenarioTextReader).Assembly.Location));
        reader.ReadScenarioTitle(1, mode);
        Assert.Equal(expected, reader.Catalog.Resolve(San9.Content.Text.TextCatalog.ScenarioTitleKey));
    }

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
    [Fact]
    public void PublicationRejectsUnconfirmedSourceWhileResearchKeepsDiagnostics()
    {
        byte[] archive = Archive(Table([1, (byte)'C', 1, 0], [0x1b, (byte)'H']));
        var research = MessageArchiveCompiler.Compile(archive, "fixture", "messages");
        Assert.False(research.Audit.Records[1].SemanticsConfirmed);
        Assert.False(research.Audit.Records[0].DependenciesConfirmed);
        Assert.NotEmpty(research.Audit.Records[1].Issues);
        var error = Assert.Throws<InvalidDataException>(() =>
            MessageArchiveCompiler.CompileForPublication(archive, "fixture", "messages"));
        Assert.Contains("messages.1", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PublicationRejectsCyclicOrMissingReferences(byte target)
    {
        byte[] archive = Archive(Table([1, (byte)'C', target, 0]));
        Assert.Throws<InvalidDataException>(() =>
            MessageArchiveCompiler.CompileForPublication(archive, "fixture", "messages"));
    }

    [Fact]
    public void PublicationKeepsValidMessagesAndDerivesReferencedArguments()
    {
        byte[] archive = Archive(Table([1, (byte)'C', 1, 0], [2, 1, 0xDB]));
        var published = MessageArchiveCompiler.CompileForPublication(archive, "fixture", "messages");
        Assert.Equal(2, published.Messages.Count);
        Assert.Empty(published.Adaptations);
        Assert.Equal(["person.0.surname"], San9.Content.Text.MessageGraph.Requirements("messages.0", published.Messages).Bindings);
    }

    [Fact]
    public void PublicationAppliesReviewedContentBeforeExpandingStyledReferences()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var encoding = System.Text.Encoding.GetEncoding(950);
        byte[] appearance = [.. encoding.GetBytes("男性容貌有"), 2, 0x8c, .. System.Text.Encoding.ASCII.GetBytes("14b\x1bH70"),
            2, 0x8c, .. System.Text.Encoding.ASCII.GetBytes("32b"), .. encoding.GetBytes("種，\n女性容貌有"),
            2, 0x8c, .. System.Text.Encoding.ASCII.GetBytes("14b\x1bH30"), 2, 0x8c,
            .. System.Text.Encoding.ASCII.GetBytes("32b"), .. encoding.GetBytes("種。")];
        var records = Enumerable.Range(0, 8678).Select(_ => Array.Empty<byte>()).ToArray();
        records[0] = [2, 0x8c, (byte)'1', (byte)'b', 1, (byte)'C', 0xe5, 0x21];
        records[8677] = appearance;
        var published = MessageArchiveCompiler.CompileForPublication(Archive(Table(records)), "fixture", "messages");
        var references = San9.Content.Text.MessageGraph.Walk(published.Messages["messages.0"].Nodes)
            .OfType<San9.Content.Text.MessageReference>();
        Assert.Equal("messages.8677", Assert.Single(references).Key);
        Assert.Contains("[preset=yellow]70[/preset]", San9.Content.Text.MessageMarkup.Static(
            published.Messages["messages.8677"].Nodes.TakeWhile(n => n is not San9.Content.Text.MessageReturn)));
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
    public void StoredNamesConvertDisplayTextAndExplicitExtension()
    {
        var table = new San9MessageTable(Archive(Table([0xB5, 0x4C], [0xFA, 0x40], [])));
        Assert.Equal("无", new OriginalTextConverter().Convert(table.PlainText(0)));
        Assert.Equal("伷", new OriginalTextConverter().Convert(table.PlainText(1)));
        Assert.Equal("", new OriginalTextConverter().Convert(table.PlainText(2)));
        var extensions = new San9MessageTable(Archive(Table([0xFA, 0x41], [0xFA, 0x45])));
        Assert.Equal("𪻺", new OriginalTextConverter().Convert(extensions.PlainText(0))); Assert.Equal("俊", new OriginalTextConverter().Convert(extensions.PlainText(1)));
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
        Assert.Throws<InvalidDataException>(() => new OriginalTextConverter().Convert(new San9MessageTable(Archive(Table([0xFA, 0x46]))).PlainText(0)));
    }
}
