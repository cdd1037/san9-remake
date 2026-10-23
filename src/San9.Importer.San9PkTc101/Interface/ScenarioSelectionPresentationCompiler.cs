using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using San9.Content.Images;
using San9.Content.Interface;
using San9.Content.Text;

namespace San9.Importer.San9PkTc101.Interface;

public static class ScenarioSelectionPresentationCompiler
{
    public static ScenarioSelectionPresentation Compile(byte[] executable, TextureLibrary library, TextCatalog text,
        IEnumerable<San9ScenarioHeader> headers)
    {
        using var pe = new PEReader(new MemoryStream(executable));
        string Asset(string archive, int image) => library.Archives.Single(a => a.Id == archive)
            .Variants.Single(v => v.ImageIndex == image).Image.Id;
        StartMenuSprite Start(int image, int x, int y, int columns = 1) => new(Asset("g_start", image), x, y, columns);
        StartMenuSprite Selection(int image, int x, int y) => new(Asset("g_start2", image), x, y);
        string Message(int id) => MessageMarkup.Static(text.Messages[$"messages.{id}"].Nodes);
        int Int(int rva) => BinaryPrimitives.ReadInt32LittleEndian(pe.GetSectionData(rva).GetContent(0, 4).AsSpan());
        // RE-0201: preview map shares the confirmation map's authored city anchors, not the world map projection.
        var cityPositions = Enumerable.Range(0, 50).ToDictionary(i => i,
            i => new ScenarioPreviewPoint(Int(0x2689c0 + i * 8), Int(0x2689c4 + i * 8)));
        var entries = CompileEntries(headers, text);
        return new("选择剧本", Message(8067), "返回",
            [Start(39, 448, 48), Start(38, 448, 48), new(Asset("g_strtpk", 2), 0, 0), Start(20, 0, 640)],
            Selection(67, 456, 136), new(Selection(68, 800, 350), 45), Selection(69, 552, 432),
            Selection(70, 0, 120), Asset("g_start2", 71), Selection(72, 512, 96), Start(26, 856, 672, 4),
            Enumerable.Range(0, 10).Select(i => Start(27 + i, 0, 180 + 42 * i, 4)).ToArray(), cityPositions,
            Enumerable.Range(0, 50).ToDictionary(i => $"color-{i}", i => Asset("g_start2", 86 + i)),
            [new("historical", "一般", Message(8068), new(36, 120)), new("fictional", "if", Message(8069), new(206, 120))], entries);
    }

    internal static ScenarioPreviewEntry[] CompileEntries(IEnumerable<San9ScenarioHeader> headers, TextCatalog text)
    {
        string Message(int id) => MessageMarkup.Static(text.Messages[$"messages.{id}"].Nodes);
        return headers.OrderBy(h => h.ScenarioId).Select(header =>
        {
            if (header.ScenarioId >= 20) throw new InvalidDataException("Unsupported TC101 scenario preview identity.");
            return new ScenarioPreviewEntry($"scenario-{header.ScenarioId}", header.ScenarioId < 10 ? "historical" : "fictional",
                header.StartDate.Year, header.StartDate.Month, MessageMarkup.Plain(Message(2 * header.ScenarioId)), Message(2 * header.ScenarioId + 1),
                header.CityForceColorCodes.Select((color, city) => (color, city)).Where(c => c.color < 50)
                    .ToDictionary(c => c.city, c => $"color-{c.color}"));
        }).ToArray();
    }
}
