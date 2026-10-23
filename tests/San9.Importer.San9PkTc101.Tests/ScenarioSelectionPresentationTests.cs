using San9.Content.Text;
using San9.Importer.San9PkTc101.Interface;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class ScenarioSelectionPresentationTests
{
    [Fact]
    public void CatalogReadsPreviewHeaderColorsAndStandardMessagesEvenForAgelessScenarios()
    {
        byte[] colors = Enumerable.Repeat((byte)255, 50).ToArray(); colors[0] = 0; colors[45] = 48;
        var header = new San9ScenarioHeader(0, [], 19, 256, 0, new(281, 1, 1), 0, 200, 200, colors);
        var text = new TextCatalog(TextCatalog.CurrentSchemaVersion, []) { Messages = new()
        {
            ["messages.38"] = new([new MessageLiteral("英雄集结")]),
            ["messages.39"] = new([new MessageLiteral("群雄争雄。\n这是目录简介。")])
        } };
        var entry = Assert.Single(ScenarioSelectionPresentationCompiler.CompileEntries([header], text));
        Assert.Equal("19", entry.Id);
        Assert.Equal("fictional", entry.CategoryId);
        Assert.Equal((281, 1, "英雄集结"), (entry.Year, entry.Month, entry.Title));
        Assert.Contains("\n这是目录简介。", entry.Description);
        Assert.Equal(2, entry.CityColors.Count);
        Assert.Equal("color-0", entry.CityColors[0]);
        Assert.Equal("color-48", entry.CityColors[45]);
        Assert.Equal(255, colors[1]);
    }
}
