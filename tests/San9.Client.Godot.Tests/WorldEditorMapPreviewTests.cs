using San9.Application;
using San9.Application.Presentation;
using San9.Content.Maps;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Client.Godot.Tests;

public sealed class WorldEditorMapPreviewTests
{
    [Theory]
    [InlineData(WorldEditorKind.Cities, 1)]
    [InlineData(WorldEditorKind.Facilities, 2)]
    [InlineData(WorldEditorKind.Obstacles, 3)]
    public void DraftChangesAndResetDriveMapAppearanceWithoutPublishing(WorldEditorKind kind, int id)
    {
        var (session, map, texts) = Fixture();
        var world = session.Snapshot;
        var draft = new WorldEditorDraft(session, kind, texts);
        var initial = draft.Preview;
        var original = MapSceneProjection.Create(initial, map);

        draft.Fields(id).Single(f => f.Label == "耐久").Set(1);
        if (kind != WorldEditorKind.Obstacles)
            draft.Fields(id).Single(f => f.Label == "士兵").Set(0);
        if (kind == WorldEditorKind.Cities)
            draft.Fields(id).Single(f => f.Label == "收益").Set(601);

        var changed = draft.Preview;
        Assert.NotSame(initial, changed);
        Assert.Same(changed, draft.Preview);
        Assert.Equal(initial.World.Revision, changed.World.Revision);
        var preview = MapSceneProjection.Create(changed, map);
        Assert.NotEqual(original.Buildings.Single(b => b.Id == id).CompositionId,
            preview.Buildings.Single(b => b.Id == id).CompositionId);
        Assert.DoesNotContain(preview.Animations, a => a.BuildingId == id && a.ClipId.StartsWith("soldier-", StringComparison.Ordinal));
        if (kind != WorldEditorKind.Obstacles)
            Assert.Contains(original.Animations, a => a.BuildingId == id && a.ClipId.StartsWith("soldier-", StringComparison.Ordinal));
        if (kind == WorldEditorKind.Cities)
            Assert.NotEqual(original.Buildings.Single(b => b.Id == id).Sprites.Single().AssetId,
                preview.Buildings.Single(b => b.Id == id).Sprites.Single().AssetId);

        draft.Reset(id);
        Assert.NotSame(changed, draft.Preview);
        var restored = MapSceneProjection.Create(draft.Preview, map);
        Assert.Equal(original.Buildings.Select(b => (b.Id, b.CompositionId)), restored.Buildings.Select(b => (b.Id, b.CompositionId)));
        Assert.Equal(original.Buildings.Single(b => b.Id == id).Sprites, restored.Buildings.Single(b => b.Id == id).Sprites);
        Assert.Equal(original.Animations, restored.Animations);
        Assert.False(draft.HasChanges);
        Assert.Same(world, session.Snapshot);
    }

    private static (GameSession Session, MapPackage Map, TextCatalog Texts) Fixture()
    {
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "editor-preview", 190, 1, 1, 5,
            [new(5, "force", 0, 100)],
            [new(1, "city", 5, 0, 0, 100, 100, 100, 60, [])
                { Population = 30000, Commerce = 300, Cultivation = 300, MaxCommerce = 1000, MaxCultivation = 1000 },
                new(2, "facility", 5, 1, 0, 100, 100, 100, 60, []) { Kind = SettlementKind.Facility },
                new(3, "obstacle", 5, 2, 0, 0, 100, 100, 0, []) { Kind = SettlementKind.Facility, CanHostPersons = false }], [], [])
            { Information = new([], [], [], [new(5, 5, null, new(0, 0)) { IsRoot = true }], []) };
        var map = new MapPackage(MapPackage.CurrentSchemaVersion, "fixture", definition.ScenarioId, "spring", 3, 1, 512, 256,
            Enumerable.Repeat(new MapSquare(0, 1, 0, 0), 3).ToArray(), [], [], [], [], [],
            [new(1, 0, 0, 0), new(2, 1, 0, 1), new(3, 2, 0, 1)],
            Enumerable.Range(1, 18).Select(id => new MapComposition(id, [new(false, true, 100, 0, 0)])
                { Guards = [new(0, 0, 0)] }).ToArray(), [], [], [], [], new([], []), [])
            { BuildingTypes = [new(0, 100), new(1, 100)] };
        return (GameSession.Start(definition, 1), map,
            new(TextCatalog.CurrentSchemaVersion, new() { ["force"] = "势力", ["city"] = "都市", ["facility"] = "设施", ["obstacle"] = "障碍物" }));
    }
}
