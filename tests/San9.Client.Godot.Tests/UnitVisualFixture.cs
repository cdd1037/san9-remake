using San9.Content.Maps;
using San9.Core.Domain;
using San9.Rendering;

namespace San9.Client.Godot.Tests;

internal static class UnitVisualFixture
{
    public static readonly Dictionary<string, MapAnimationClip> Clips = Enumerable.Range(0, 8).Select(d => $"body-{d}")
        .Concat(Enumerable.Range(0, 8).Select(d => $"imperial-{d}"))
        .Concat(Enumerable.Range(0, 8).SelectMany(d => new[] { $"unit-flag-{d}-pole-back", $"unit-flag-{d}-pole-front", $"unit-flag-{d}-cloth-0", $"unit-flag-{d}-glyph-0-0", $"unit-flag-{d}-glyph-1-0", $"unit-flag-{d}-glyph-0-2" }))
        .Concat(["flag-glyph-0-0", "flag-glyph-1-0", "flag-pole-back", "flag-cloth-0", "flag-glyph-0-2", "flag-pole-front", "confused", "high", "influence"])
        .ToDictionary(id => id, id => new MapAnimationClip(id, [id + "-0", id + "-1"], 100,
            id.StartsWith("unit-flag-") ? [] : new() { ["spring"] = [id + "-0", id + "-1"], ["summer"] = [id + "-0", id + "-1"],
                ["autumn"] = [id + "-winter-0", id + "-winter-1"], ["winter"] = [id + "-winter-0", id + "-winter-1"] }));
    public static readonly MapUnitCatalog Catalog = new(
        [new("body", Enumerable.Range(0, 8).Select(d => $"body-{d}").ToArray(), 18, 30) { ImperialBodyId = "imperial", Flags = Flags() },
         new("imperial", Enumerable.Range(0, 8).Select(d => $"imperial-{d}").ToArray(), 18, 30) { Flags = Flags() }],
        [new(10000, [new(1, [new("body", 0, 0)], new("influence", -40, -50)),
                    new(1001, [new("body", -12, 0), new("body", 12, 0) { Index = 1 }], new("influence", -44, -57))], -70),
         new(20000, [new(1, [new("body", 0, 0), new("body", 12, 12) { Index = 1 }], new("influence", -40, -50))], -70)],
        new() { [UnitCondition.Confused] = new("confused", -16, 0), [UnitCondition.HighSpirits] = new("high", -16, 0) }, 0);
    private static MapUnitFlagPlacement[][] Flags() => Enumerable.Range(0, 8)
        .Select(_ => new[] { new MapUnitFlagPlacement(-34, -64, 0) }).ToArray();
    public static readonly MapUnitCatalog TrailCatalog = Catalog with { Bodies = Catalog.Bodies.Select(b => b with
            { Trail = new("influence", 0, 0), TrailChance = 100 }).ToArray() };
    public static readonly MapUnitCatalog OverlappingCatalog = Catalog with { Formations = Catalog.Formations.Select(f => f with
            { Layouts = f.Layouts.Select(l => l with { Members = l.Members.Select(m => m with { X = 0, Y = 0 }).ToArray() }).ToArray() }).ToArray() };

    public static UnitVisualState State => new(10000, 89, 64, 1, 1000, 10000, UnitCondition.Normal, 0, false, 0, 2);
    public static UnitVisual Sample(UnitVisualState state, MapUnitCatalog catalog,
        double milliseconds = 0,
        Func<double, UnitVisualState>? history = null, Func<double, double>? visualAge = null,
        Func<double, double, double>? elevationAt = null, UnitMotionPlayback? playback = null)
    {
        playback ??= new();
        history ??= _ => state;
        if (!playback.IsInitialized)
            playback.Begin(history(-MapMovementCadence.StepMilliseconds), catalog, 0, 0,
                history(0).IsMoving);
        double visualTime = visualAge?.Invoke(0) ?? milliseconds;
        playback.Advance(milliseconds, visualTime, true, history, catalog, Clips,
            t => visualAge is null ? t : visualTime - visualAge(t));
        return UnitVisuals.Resolve(state, catalog, Clips, playback, elevationAt);
    }
}
