using San9.Importer.San9PkTc101.Text;
using San9.Importer.San9PkTc101.Containers;
using System.Buffers.Binary;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class San9MessageTableTests
{
    [Theory]
    [InlineData(0, "biographies.0")]
    [InlineData(562, "biographies.562")]
    [InlineData(563, null)]
    [InlineData(569, null)]
    [InlineData(570, "biographies.563")]
    [InlineData(685, "biographies.678")]
    public void BiographyMaterializesOwnedTextAtTheProvidedKey(int code, string? expected)
    {
        using var reader = new ScenarioTextReader(Archive(Table(Array.Empty<byte>())),
            File.ReadAllBytes(typeof(ScenarioTextReader).Assembly.Location));
        var biographies = new San9MessageTable(Archive(Table(Enumerable.Range(0, 679)
            .Select(i => System.Text.Encoding.ASCII.GetBytes($"Biography {i}")).ToArray())));
        Assert.Equal(expected is null ? null : "person-biography", reader.Biography("person-biography", code, biographies));
        if (expected is null) Assert.Empty(reader.Catalog.Entries);
        else Assert.Equal(expected.Replace("biographies.", "Biography ", StringComparison.Ordinal), reader.Catalog.Resolve("person-biography"));
    }

    [Theory]
    [InlineData(1, "正史\n中記載他字翼德。", "正史\n中记载他字益德。")]
    [InlineData(159, "糜竺", "麋竺")]
    [InlineData(163, "糜竺、傅士仁", "麋竺、士仁")]
    [InlineData(178, "傅士仁", "士仁")]
    [InlineData(430, "傅彤", "傅肜")]
    public void BiographyAppliesEditorialRulesUsingItsSourceRecord(int resource, string original, string expected)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var records = Enumerable.Range(0, 679).Select(_ => Array.Empty<byte>()).ToArray();
        records[resource] = System.Text.Encoding.GetEncoding(950).GetBytes(original);
        using var reader = new ScenarioTextReader(Archive(Table(Array.Empty<byte>())),
            File.ReadAllBytes(typeof(ScenarioTextReader).Assembly.Location));
        var biographies = new San9MessageTable(Archive(Table(records)));

        Assert.Equal("person-biography", reader.Biography("person-biography", resource, biographies));
        Assert.Equal(expected, reader.Catalog.Resolve("person-biography"));
    }

    [Fact]
    public void CountryDescriptionsComeFromTheirDedicatedMessages()
    {
        var records = Enumerable.Range(0, 7761).Select(_ => Array.Empty<byte>()).ToArray();
        records[7741] = "First country description"u8.ToArray();
        records[7760] = "Last country description"u8.ToArray();
        using var reader = new ScenarioTextReader(Archive(Table(records)),
            File.ReadAllBytes(typeof(ScenarioTextReader).Assembly.Location));
        Assert.Equal("First country description", reader.CountryTitleDescription(0));
        Assert.Equal("Last country description", reader.CountryTitleDescription(19));
    }

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

    [Theory]
    [InlineData(true, "麋竺")]
    [InlineData(false, "糜竺")]
    public void PublicationAndStaticBiographyShareTheSelectedTextPolicy(bool originalTextEdits, string expected)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        // Biography 159 is a reviewed name reference.
        var records = Enumerable.Range(0, 160).Select(_ => Array.Empty<byte>()).ToArray();
        records[1] = System.Text.Encoding.GetEncoding(950).GetBytes("翼德");
        records[3] = System.Text.Encoding.GetEncoding(950).GetBytes("郤因");
        records[159] = System.Text.Encoding.GetEncoding(950).GetBytes("糜竺");
        byte[] archive = Archive(Table(records));
        var publication = MessageArchiveCompiler.CompileForPublication(archive, "fixture", "biographies",
            originalTextEdits: originalTextEdits);
        Assert.Equal(expected, publication.Messages["biographies.159"].Nodes.OfType<San9.Content.Text.MessageLiteral>().Single().BbCode);
        var research = MessageArchiveCompiler.Compile(archive, "fixture", "biographies", originalTextEdits: originalTextEdits);
        Assert.True(research.Audit.Records[159].SemanticsConfirmed);
        Assert.Equal(expected, research.Messages["biographies.159"].Nodes.OfType<San9.Content.Text.MessageLiteral>().Single().BbCode);
        using var reader = new ScenarioTextReader(Archive(Table(Array.Empty<byte>())),
            File.ReadAllBytes(typeof(ScenarioTextReader).Assembly.Location), originalTextEdits);
        reader.Biography("biography", 159, new(archive));
        Assert.Equal(expected, reader.Catalog.Resolve("biography"));
        if (originalTextEdits)
            Assert.Throws<InvalidDataException>(() => reader.ValidatePersonReferences(348, 348, 349, 348));
        else reader.ValidatePersonReferences(348, 348, 349, 348);
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
        byte[] reference = [2, 0x8c, (byte)'1', (byte)'b', 1, (byte)'C', 0xe5, 0x21, 5, 5, 5];
        byte[] glossary = [.. appearance, 5, 5, 5];
        var parser = new OriginalMessageParser();
        var published = MessageArchiveCompiler.CompileRecordsForPublication(
            [("messages.0", reference, parser.ParseSyntax(reference)),
             ("messages.8677", glossary, parser.ParseSyntax(glossary))], "fixture", "messages");
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
    public void StaticRegionAndReferencedMessageUseTheSameEditorialSource()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var records = Enumerable.Range(0, 215).Select(_ => Array.Empty<byte>()).ToArray();
        records[0] = [1, (byte)'C', 214, 0];
        records[103] = System.Text.Encoding.GetEncoding(950).GetBytes("笵疆");
        records[107] = System.Text.Encoding.GetEncoding(950).GetBytes("一昧、劉襌、劉襌");
        records[113] = System.Text.Encoding.GetEncoding(950).GetBytes("襌讓");
        records[214] = System.Text.Encoding.GetEncoding(950).GetBytes("壼關");
        byte[] archive = Archive(Table(records));
        using var reader = new ScenarioTextReader(archive, File.ReadAllBytes(typeof(ScenarioTextReader).Assembly.Location));
        reader.Region("region.test", 24);
        var published = MessageArchiveCompiler.CompileForPublication(archive, "fixture", "messages");
        string publishedName = San9.Content.Text.MessageMarkup.Static(published.Messages["messages.214"].Nodes
            .TakeWhile(n => n is not San9.Content.Text.MessageReturn));
        Assert.Equal("壶关", reader.Catalog.Resolve("region.test"));
        Assert.Equal(reader.Catalog.Resolve("region.test"), publishedName);
        Assert.Equal("messages.214", Assert.IsType<San9.Content.Text.MessageReference>(published.Messages["messages.0"].Nodes[0]).Key);
    }

    [Theory]
    [InlineData(524, "傅士", "仁", "士", "仁", "士仁")]
    [InlineData(686, "大", "喬", "", "大乔", "大乔")]
    [InlineData(266, "鍾", "繇", "钟", "繇", "钟繇")]
    [InlineData(348, "孫", "乾", "孙", "乾", "孙乾")]
    public void FullNamesAndDynamicNamePartsShareTheSameFields(int person, string originalFamily, string originalGiven,
        string expectedFamily, string expectedGiven, string expectedName)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var records = Enumerable.Range(0, 4480).Select(_ => Array.Empty<byte>()).ToArray();
        records[1678 + 4 * person] = System.Text.Encoding.GetEncoding(950).GetBytes(originalFamily);
        records[1679 + 4 * person] = System.Text.Encoding.GetEncoding(950).GetBytes(originalGiven);
        using var reader = new ScenarioTextReader(Archive(Table(records)), File.ReadAllBytes(typeof(ScenarioTextReader).Assembly.Location));
        reader.Person("person.test", person, person);
        reader.MessagePerson(person, person, person, person);
        var catalog = reader.Catalog;
        var profile = catalog.MessagePeople[person];
        Assert.Equal(San9.Importer.San9PkTc101.Images.PortraitCompiler.ImageId(person), profile.PortraitKey);
        Assert.Equal(expectedName, catalog.Resolve("person.test"));
        Assert.Equal(expectedFamily, reader.Surname(person));
        Assert.Equal(expectedFamily.Length == 0 ? null : expectedFamily, profile.SurnameKey is null ? null : catalog.Resolve(profile.SurnameKey));
        Assert.Equal(expectedGiven, catalog.Resolve(profile.GivenNameKey!));
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
