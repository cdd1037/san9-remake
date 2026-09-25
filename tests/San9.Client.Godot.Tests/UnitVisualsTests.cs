using San9.Content.Maps;
using San9.Core.Domain;
using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class UnitVisualsTests
{
    private static readonly Dictionary<string, MapAnimationClip> Clips = Enumerable.Range(0, 8).Select(d => $"body-{d}")
        .Concat(["flag-pole-back", "flag-cloth-0", "flag-glyph-0-2", "flag-pole-front", "confused", "high", "influence"])
        .ToDictionary(id => id, id => new MapAnimationClip(id, [id + "-0", id + "-1"], 100,
            new() { ["spring"] = [id + "-0", id + "-1"], ["summer"] = [id + "-0", id + "-1"],
                ["autumn"] = [id + "-winter-0", id + "-winter-1"], ["winter"] = [id + "-winter-0", id + "-winter-1"] }));
    private static readonly MapUnitCatalog Catalog = new(
        [new("body", Enumerable.Range(0, 8).Select(d => $"body-{d}").ToArray(), 18, 30)],
        [new(10000, [new(1, "body", 0, 0), new(1001, "body", -12, 12)], -64, -70),
         new(20000, [new(1, "body", 0, 0), new(1001, "body", 12, 12)], -64, -70)],
        new() { [UnitCondition.Confused] = new("confused", -16, 0), [UnitCondition.HighSpirits] = new("high", -16, 0) },
        [new(1, new("influence", -40, -50))]);
    private static UnitVisualState State => new(10000, 89, 64, 1, 1000, 10000, UnitCondition.Normal, 0, false, 0, 2);
    private static UnitVisual Resolve(UnitVisualState state) => UnitVisuals.Resolve(state, Catalog, Clips);

    [Fact]
    public void FinePositionAndElevationShareTheBuildingProjection()
    {
        var center = MapGeometry.Center(89, 64);
        var unit = Resolve(State);
        Assert.Equal(center.X - 18, unit.Animations[0].X);
        Assert.Equal(center.Y - 30, unit.Animations[0].Y);
        var moved = Resolve(State with { CellX = 89.2, Elevation = 2 });
        Assert.Equal(unit.Animations[0].X + 6, moved.Animations[0].X);
        Assert.Equal(unit.Animations[0].Y + 3 - 10, moved.Animations[0].Y);
        Assert.True(moved.Depth > unit.Depth);
    }

    [Fact]
    public void SoldierThresholdsAndFormationReferencesChangeTheVisibleRanks()
    {
        Assert.Single(Resolve(State).Animations, s => s.ClipId.StartsWith("body-"));
        var large = Resolve(State with { Soldiers = 1001 });
        Assert.Equal(2, large.Animations.Count(s => s.ClipId.StartsWith("body-")));
        Assert.NotEqual(large.Animations, Resolve(State with { Soldiers = 1001, FormationId = 20000 }).Animations);
        var enormous = Resolve(State with { Soldiers = uint.MaxValue });
        Assert.Equal(large.Animations, enormous.Animations);
        Assert.Equal(uint.MaxValue, enormous.Soldiers);
    }

    [Fact]
    public void InfluenceAndConditionCoexistAndClearIndependently()
    {
        var confused = Resolve(State with { HasCityInfluence = true, Condition = UnitCondition.Confused });
        Assert.Contains(confused.Animations, s => s.ClipId == "confused");
        Assert.Contains(confused.Animations, s => s.ClipId == "influence");
        var recovered = Resolve(State with { HasCityInfluence = true });
        Assert.DoesNotContain(recovered.Animations, s => s.ClipId == "confused");
        Assert.Contains(recovered.Animations, s => s.ClipId == "influence");
        Assert.DoesNotContain(Resolve(State with { Condition = UnitCondition.HighSpirits }).Animations, s => s.ClipId == "influence");
    }

    [Fact]
    public void IdleAndFlagsAnimateWithoutSimulationChangesAndKeepSeasonalPhase()
    {
        var unit = Resolve(State with { Glyph = -1, Direction = 6 });
        Assert.DoesNotContain(unit.Animations, s => s.ClipId.Contains("glyph"));
        Assert.Equal("body-6", unit.Animations[0].ClipId);
        foreach (var sprite in unit.Animations)
        {
            var clip = Clips[sprite.ClipId];
            Assert.NotEqual(MapAnimationClock.Asset(clip, "spring", 0, sprite.Phase), MapAnimationClock.Asset(clip, "spring", 100, sprite.Phase));
            Assert.Contains("winter", MapAnimationClock.Asset(clip, "winter", 100, sprite.Phase));
        }
    }

    [Fact]
    public void MissingBodiesAndClipsFailValidation()
    {
        Catalog.Validate(Clips);
        Assert.Throws<InvalidDataException>(() => (Catalog with { Bodies = [] }).Validate(Clips));
        Assert.Throws<InvalidDataException>(() => Catalog.Validate(new Dictionary<string, MapAnimationClip>()));
    }

    [Theory]
    [InlineData(0, 1, 4)]
    [InlineData(1, 0, 2)]
    [InlineData(0, -1, 0)]
    [InlineData(-1, 0, 6)]
    public void FacingUsesTheCurrentRouteOrTarget(int x, int y, int expected) =>
        Assert.Equal(expected, UnitVisuals.Direction(new(10, 10), new(10 + x, 10 + y)));
}
