using System.Collections.Immutable;
using San9.Application;
using San9.Content.Images;
using San9.Content.Maps;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class NavalMapAnimationTests
{
    public static IEnumerable<object[]> DirectionsAndActions =>
        from d in Enumerable.Range(0, 8) from a in Enum.GetValues<UnitVisualAction>() select new object[] { d, a };

    [Theory]
    [MemberData(nameof(DirectionsAndActions))]
    public void EveryFlagFollowsItsDirectionalAttachmentDuringIdleMovementAndAttacks(int direction, UnitVisualAction action)
    {
        var flags = Enumerable.Range(0, 8).Select(d => new[] {
            new MapUnitFlagPlacement(2 + d, -84 + d, 2), new MapUnitFlagPlacement(-16 + d, -93 + d, -1) }).ToArray();
        var catalog = UnitVisualFixture.Catalog with { Bodies = UnitVisualFixture.Catalog.Bodies
            .Select(b => b with { Flags = flags }).ToArray() };
        var state = UnitVisualFixture.State with { Direction = direction, Action = action, Elevation = 3 };
        var first = UnitVisuals.Resolve(state, catalog, UnitVisualFixture.Clips);
        var next = UnitVisuals.Resolve(state with { CellX = state.CellX + 1 }, catalog, UnitVisualFixture.Clips);
        var layers = first.Animations.Where(a => a.ClipId.Contains("-cloth-")).ToArray();
        var moved = next.Animations.Where(a => a.ClipId.Contains("-cloth-")).ToArray();
        Assert.Equal(2, layers.Length);
        for (int i = 0; i < 2; i++)
        {
            Assert.Equal($"unit-flag-{direction}-cloth-0", layers[i].ClipId);
            Assert.Equal((first.AnchorX + flags[direction][i].X, first.AnchorY + flags[direction][i].Y), (layers[i].X, layers[i].Y));
            Assert.Equal(30100 + 16 * (state.CellX + state.CellY + 1) + 112 + flags[direction][i].DepthOffset, layers[i].Depth);
            Assert.Equal((32, 16), (moved[i].X - layers[i].X, moved[i].Y - layers[i].Y));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(7)]
    public void OrdinaryVolleyIsIndependentOfTheBodyVariantAndUsesItsActualFrameCenter(int id)
    {
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "ship", 190, 1, 1, 1,
            [new(1, "player", 0, 100)], [], [], [])
        {
            Units = [new(id, 1, new(7, 7), 100) { FormationId = 10000 }, new(100, 1, new(10, 7), 100) { FormationId = 10000 }],
            Military = new(4, 4, Enumerable.Repeat(0, 16).ToImmutableArray(), [new(0, null, 1) { IsWater = true }],
                [new(10000, 1, 50, 1000, 5, new(1, 1), TravelMode.Water)], []),
            Information = new([], [], [], [new CorpsState(1, 1, null, new(100, 100)) { IsRoot = true }], [])
                { Formations = [new(10000, "ship")] }
        };
        var session = GameSession.Start(definition, 1);
        MapUnitAction Variant(string name) => new(Enumerable.Repeat(name, 8).ToArray());
        var catalog = UnitVisualFixture.Catalog with
        {
            Bodies = [UnitVisualFixture.Catalog.Bodies[0] with { ImperialBodyId = null, AnchorX = 63, AnchorY = 92,
                MeleeAttack = new([Variant("short"), Variant("long")], MapProjectileKind.Arrow, 5), RangedAttack = null }],
            Projectiles = new() { [MapProjectileKind.Arrow] = new(["arrow"], 64, false, 0, 0) }
        };
        var clips = UnitVisualFixture.Clips.Values.Concat(new[] { new MapAnimationClip("short", ["s"], 100, []),
            new MapAnimationClip("long", ["l0", "l1"], 2000, []) }).ToArray();
        ImageAsset[] assets = [new("s", "s.png", 128, 128, 4), new("l0", "l.png", 128, 128, 4)];
        var map = new MapPackage(16, "fixture", "ship", "spring", 4, 4, 13056, 6656,
            Enumerable.Repeat(new MapSquare(0, 1, 0, 0), 16).ToArray(), [], assets, [], [], [], [], [], [], [], [], [], new(clips, []), [])
            { Units = catalog };
        var plan = new MapDayPlan(map, session.View, [new BattleDamageEvent(session.Snapshot.Clock, id, 100, null, 1, 0, 1, 0, 0)]);
        var attack = Assert.Single(plan.Attacks);
        Assert.Equal(MapProjectileKind.Arrow, attack.Projectile);
        Assert.Equal(UnitVisualAction.Melee, attack.Action);
        Assert.Equal(5, attack.Origins.Length);
        var visual = UnitVisuals.Resolve(UnitVisualFixture.State with { Id = id, CellX = 1, CellY = 1, Direction = attack.Direction,
            Action = attack.Action }, catalog, clips.ToDictionary(c => c.Id));
        var body = Assert.Single(visual.Animations, a => a.IsBody);
        Assert.All(attack.Origins, p => Assert.Equal(new MapMotionPoint(body.X + 64, body.Y + 64), p));
        double bodyDuration = body.ClipId == "long" ? 4 : .1;
        Assert.Equal(Math.Max(bodyDuration, MapProjectiles.Duration(attack) / 1000), plan.AttackDuration(attack), 8);
        for (int i = 0; i < 5; i++)
        {
            var arrow = MapProjectiles.Sample(attack, catalog.Projectiles[MapProjectileKind.Arrow], i * 200).Last();
            Assert.Equal((attack.FromX, attack.FromY), (arrow.X, arrow.Y));
        }
    }
}
