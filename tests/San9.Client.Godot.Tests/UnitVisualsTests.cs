using San9.Content.Maps;
using San9.Core.Domain;
using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class UnitVisualsTests
{
    private static readonly Dictionary<string, MapAnimationClip> Clips = Enumerable.Range(0, 8).Select(d => $"body-{d}")
        .Concat(Enumerable.Range(0, 8).Select(d => $"imperial-{d}"))
        .Concat(Enumerable.Range(0, 8).SelectMany(d => new[] { $"unit-flag-{d}-pole-back", $"unit-flag-{d}-pole-front", $"unit-flag-{d}-cloth-0", $"unit-flag-{d}-glyph-0-0", $"unit-flag-{d}-glyph-1-0", $"unit-flag-{d}-glyph-0-2" }))
        .Concat(["flag-glyph-0-0", "flag-glyph-1-0", "flag-pole-back", "flag-cloth-0", "flag-glyph-0-2", "flag-pole-front", "confused", "high", "influence"])
        .ToDictionary(id => id, id => new MapAnimationClip(id, [id + "-0", id + "-1"], 100,
            id.StartsWith("unit-flag-") ? [] : new() { ["spring"] = [id + "-0", id + "-1"], ["summer"] = [id + "-0", id + "-1"],
                ["autumn"] = [id + "-winter-0", id + "-winter-1"], ["winter"] = [id + "-winter-0", id + "-winter-1"] }));
    private static readonly MapUnitCatalog Catalog = new(
        [new("body", Enumerable.Range(0, 8).Select(d => $"body-{d}").ToArray(), 18, 30) { ImperialBodyId = "imperial" },
         new("imperial", Enumerable.Range(0, 8).Select(d => $"imperial-{d}").ToArray(), 18, 30)],
        [new(10000, [new(1, [new("body", 0, 0)], new("influence", -40, -50)),
                    new(1001, [new("body", -12, 0), new("body", 12, 0)], new("influence", -44, -57))], -64, -70),
         new(20000, [new(1, [new("body", 0, 0), new("body", 12, 12)], new("influence", -40, -50))], -64, -70)],
        new() { [UnitCondition.Confused] = new("confused", -16, 0), [UnitCondition.HighSpirits] = new("high", -16, 0) }, 0);
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
    public void InformationCardStaysAboveTheUnitAndPreservesCommanderAndActualValues()
    {
        var unit = Resolve(State with { Soldiers = 18000, Morale = 85, CommanderNameKey = "officer.commander" });
        Assert.Equal("officer.commander", unit.CommanderNameKey);
        Assert.Equal(18000u, unit.Soldiers);
        Assert.Equal(85, unit.Morale);
        Assert.True(unit.LabelY + 47 < unit.Animations[0].Y);
        var changed = Resolve(State with { Soldiers = 1, Condition = UnitCondition.Confused, Direction = 4 });
        Assert.Equal((unit.LabelX, unit.LabelY), (changed.LabelX, changed.LabelY));
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
        Assert.All(confused.Animations.Take(confused.BodySpriteCount), s => Assert.StartsWith("body-", s.ClipId));
        Assert.DoesNotContain(confused.Animations.Skip(confused.BodySpriteCount), s => s.ClipId.StartsWith("body-"));
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
            if (sprite.ClipId.StartsWith("unit-flag-"))
                Assert.Equal(MapAnimationClock.Asset(clip, "spring", 100, sprite.Phase), MapAnimationClock.Asset(clip, "winter", 100, sprite.Phase));
            else Assert.Contains("winter", MapAnimationClock.Asset(clip, "winter", 100, sprite.Phase));
        }
    }

    [Fact]
    public void MissingBodiesAndClipsFailValidation()
    {
        Catalog.Validate(Clips);
        Assert.Throws<InvalidDataException>(() => (Catalog with { Bodies = [] }).Validate(Clips));
        Assert.Throws<InvalidDataException>(() => Catalog.Validate(new Dictionary<string, MapAnimationClip>()));
    }

    [Fact]
    public void CompleteLayoutsReplaceRanksAndShareTheirInfluenceAnchor()
    {
        var small = Resolve(State with { HasCityInfluence = true });
        var large = Resolve(State with { Soldiers = 1001, HasCityInfluence = true, Direction = 6 });
        var center = MapGeometry.Center(89, 64);
        var soldiers = large.Animations.Where(s => s.ClipId.StartsWith("body-")).ToArray();
        Assert.Equal(new[] { center.X - 30, center.X - 6 }, soldiers.Select(s => s.X));
        Assert.All(soldiers, s => Assert.Equal(center.Y - 30, s.Y));
        Assert.DoesNotContain(soldiers, s => s.X == center.X - 18);
        Assert.Equal(center.X - 40, small.Animations.Single(s => s.ClipId == "influence").X);
        Assert.Equal(center.X - 44, large.Animations.Single(s => s.ClipId == "influence").X);
        Assert.Equal(center.Y - 57, large.Animations.Single(s => s.ClipId == "influence").Y);
    }

    [Fact]
    public void ImperialBodiesAndRulerFlagsAreIndependentAndReversible()
    {
        var imperial = Resolve(State with { UsesImperialAppearance = true });
        Assert.Contains(imperial.Animations, s => s.ClipId == "imperial-0");
        Assert.Contains(imperial.Animations, s => s.ClipId == "unit-flag-0-glyph-0-2");
        var ruler = Resolve(State with { IsRulerCommander = true });
        Assert.Contains(ruler.Animations, s => s.ClipId == "body-0");
        Assert.Contains(ruler.Animations, s => s.ClipId == "unit-flag-0-glyph-0-0");
        Assert.DoesNotContain(Resolve(State).Animations, s => s.ClipId == "unit-flag-0-glyph-0-0" || s.ClipId.StartsWith("imperial-"));
    }

    [Fact]
    public void UnitFlagsFollowAllEightFacingsWithoutChangingForTheSeason()
    {
        foreach (int direction in Enumerable.Range(0, 8))
        {
            var unit = Resolve(State with { Direction = direction });
            var flags = unit.Animations.Where(s => s.ClipId.StartsWith("unit-flag-")).ToArray();
            Assert.Equal(4, flags.Length);
            Assert.All(flags, flag =>
            {
                Assert.StartsWith($"unit-flag-{direction}-", flag.ClipId);
                var clip = Clips[flag.ClipId];
                Assert.Empty(clip.SeasonFrames);
                Assert.Equal(MapAnimationClock.Asset(clip, "spring", 142), MapAnimationClock.Asset(clip, "winter", 142));
            });
        }
        Assert.Throws<InvalidDataException>(() => Catalog.Validate(Clips.Where(p => p.Key != "unit-flag-7-pole-front").ToDictionary()));
    }

    [Theory]
    [InlineData(0, 1, 4)]
    [InlineData(1, 0, 2)]
    [InlineData(0, -1, 0)]
    [InlineData(-1, 0, 6)]
    public void FacingUsesTheCurrentRouteOrTarget(int x, int y, int expected) =>
        Assert.Equal(expected, UnitVisuals.Direction(new(10, 10), new(10 + x, 10 + y)));
}
