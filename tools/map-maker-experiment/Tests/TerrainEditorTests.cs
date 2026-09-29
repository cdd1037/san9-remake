using System.Text.Json;
using San9.Content.Packages;
using San9.TerrainEditor.Model;
using Xunit;

namespace San9.TerrainEditor.Tests;

public sealed class TerrainEditorTests
{
    private static MapResources Geometry() => new([], [], [], [], [], new int[288], new int[16], [], new byte[32]);

    [Fact]
    public void NorthernHigherCellRaisesBothNorthernCornersAndLoweringRemovesThem()
    {
        TerrainDocument doc = TerrainDocument.Create(4, 4);
        doc.Cells[1] = new(0, 2, 0);
        Assert.Equal(9, TerrainCompiler.ElevationMasks(doc)[5]);
        doc.Cells[1] = new(0, 1, 0);
        Assert.All(TerrainCompiler.ElevationMasks(doc), mask => Assert.Equal(0, mask));
    }

    [Fact]
    public void VariantChangesResourceWithoutChangingHeightOrTerrain()
    {
        Assert.Equal(10225, TerrainCompiler.Resource(new(3, 1, 0), 0));
        Assert.Equal(10234, TerrainCompiler.Resource(new(3, 1, 1), 0));
        Assert.Equal(10243, TerrainCompiler.Resource(new(3, 1, 2), 0));
        Assert.Equal(10329, TerrainCompiler.Resource(new(16, 1, 4), 0));
        Assert.Equal(10328, TerrainCompiler.Resource(new(16, 1, 5), 0));
    }

    [Fact]
    public void UniformRectangularMapHasOneCenterPerCellAndNoTransitions()
    {
        TerrainDocument doc = TerrainDocument.Create(8, 4);
        var stamps = TerrainCompiler.Compile(doc, Geometry());
        Assert.Equal(32, stamps.Length);
        Assert.All(stamps, s => Assert.True(s.Stamp.IsCenter));
        Assert.Equal(32, stamps.Select(s => s.Cell).Distinct().Count());
        Assert.Equal((224, 128), TerrainCompiler.Center(0, 0, doc.Height));
    }

    [Fact]
    public void TransitionChangesAreRebuiltWithoutMutatingTheThreeAuthoredFields()
    {
        TerrainDocument doc = TerrainDocument.Create(4, 4);
        doc.Cells[5] = new(0, 4, 2);
        TerrainCell[] before = (TerrainCell[])doc.Cells.Clone();
        var stamps = TerrainCompiler.Compile(doc, Geometry());
        Assert.True(stamps.Length > doc.Cells.Length);
        Assert.Equal(before, doc.Cells);
        Assert.Equal(stamps, TerrainCompiler.Compile(doc, Geometry()));
        Assert.All(stamps, s => Assert.InRange(s.Cell, 0, 15));
    }

    [Fact]
    public void FastStrokeInterpolatesAndUndoRestoresOriginalValuesOncePerStroke()
    {
        TerrainDocument doc = TerrainDocument.Create(16, 16);
        var history = new EditHistory(); history.Begin();
        history.Paint(doc, 0, 0, 15, 15, 0, c => c with { Height = 3 });
        history.Paint(doc, 15, 15, 0, 0, 0, c => c with { Variant = 2 }); history.End();
        for (int i = 0; i < 16; i++) Assert.Equal(new TerrainCell(0, 3, 2), doc.Cells[i * 17]);
        history.Undo(doc); Assert.All(doc.Cells, c => Assert.Equal(new TerrainCell(0, 1, 0), c));
        history.Redo(doc); Assert.Equal(new TerrainCell(0, 3, 2), doc.Cells[255]);
        history.Undo(doc); history.Begin(); history.Paint(doc, 0, 0, 0, 0, 1, _ => new(3, 2, 1)); history.End();
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void UnsupportedSingleFieldChangesAreSkippedWithoutChangingOtherFields()
    {
        TerrainDocument doc = TerrainDocument.Create(4, 4); doc.Cells[0] = new(9, 1, 3);
        var history = new EditHistory(); history.Begin();
        Assert.False(history.Paint(doc, 0, 0, 0, 0, 0, c => c with { Height = 7 })); history.End();
        Assert.Equal(new TerrainCell(9, 1, 3), doc.Cells[0]); Assert.False(history.CanUndo);
    }

    [Fact]
    public void ProjectRoundtripIncludesOnlyThreeCellFieldsAndRejectsMalformedInput()
    {
        TerrainDocument doc = TerrainDocument.Create(4, 8); doc.Cells[3] = new(0, 7, 2);
        string json = JsonSerializer.Serialize(doc, TerrainDocument.JsonOptions);
        using JsonDocument parsed = JsonDocument.Parse(json);
        Assert.Equal(new[] { "type", "height", "variant" }, parsed.RootElement.GetProperty("cells")[0].EnumerateObject().Select(p => p.Name));
        var restored = JsonSerializer.Deserialize<TerrainDocument>(json, TerrainDocument.JsonOptions)!;
        restored.Validate(); Assert.Equal(doc.Cells, restored.Cells);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TerrainDocument>(json.Replace("\"variant\": 0", "\"unknown\": 0"), TerrainDocument.JsonOptions));
        Assert.Throws<InvalidDataException>(() => (doc with { Width = 65 }).Validate());
        doc.Cells[0] = new(9, 7, 0); Assert.Throws<InvalidDataException>(doc.Validate);
    }

    [Fact]
    public void EveryExposedRecipeCombinationResolvesWithinItsTextureCatalog()
    {
        foreach (var (recipe, type) in TerrainRecipe.All.Select((r, t) => (r, t)))
            for (int h = recipe.MinHeight; h <= recipe.MaxHeight; h++)
                foreach (int v in TerrainRecipe.VariantsFor(type))
                    for (int mask = 0; mask < 16; mask++)
                        Assert.InRange(TerrainCompiler.Resource(new(type, h, v), mask), 10006, 10776);
    }
}
