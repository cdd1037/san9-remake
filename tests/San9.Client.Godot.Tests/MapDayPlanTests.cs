using San9.Tests;
using San9.Application;
using San9.Content.Maps;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Client.Godot.Tests;

public sealed class MapDayPlanTests
{
    [Fact]
    public void MovementVisionSurvivesDayHandoffsAndPreparationUntilMovementActuallyStops()
    {
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "vision", 190, 1, 1, 5,
            [new(5, "player", 0, 100)], [new(1, "city", 5, 0, 0, 100, 100, 100, 60, [])], [], [])
        {
            Units = [new(10, 5, new(2, 2), 100) { FormationId = 10000 }],
            Military = new(3, 1, [0, 0, 0], [new(0, 1, null)],
                [new(10000, 1, 50, 1000, 5, new(1, 1), TravelMode.Land)], [new(1, new(2, 2), new(1, 1))]),
            Information = new([], [], [], [new CorpsState(5, 5, null, new(100, 100)) { IsRoot = true }], [])
                { Formations = [new(10000, "formation") { Presentation = new(1, 1, [], 1, 180, null, null) }] }
        };
        var session = GameSession.Start(definition, 1);
        var catalog = UnitVisualFixture.Catalog with
            { VisionSlices = Enumerable.Repeat(new MapUnitEffect("influence", 0, 0), 16).ToArray() };
        var map = new MapPackage(MapPackage.CurrentSchemaVersion, "fixture", "vision", "spring", 3, 1, 13056, 6656,
            Enumerable.Repeat(new MapSquare(0, 1, 0, 0), 3).ToArray(), [], [], [], [], [], [new(1, 0, 0, 0)],
            Enumerable.Range(1, 14).Select(id => new MapComposition(id, [])).ToArray(),
            [], [], [], [], new(UnitVisualFixture.Clips.Values.ToArray(), []), [])
            { BuildingTypes = [new(0, 100)], Units = catalog };
        var terrain = new San9.Rendering.TerrainRenderer(map, [], []);
        var before = session.View;
        var after = before.ForWorld(before.World with
        {
            Units = [before.World.Units.Single() with { Position = new(7, 2) }],
            Simulation = before.World.Simulation with { Boundary = SimulationBoundary.Advancing }
        });
        DomainEvent[] events = [new UnitMovedEvent(before.World.Clock, 10, new(2, 2), new(7, 2), 1, NavigationStatus.Moving)];
        var day = new TurnDayPresentation(before, after, events, new MapDayPlan(map, before, events));
        MapScene Scene(San9.Application.Presentation.StrategySnapshot state, MapScene? previous = null, TurnDayPresentation? current = null) =>
            new(map, terrain, "spring", state, new Dictionary<string, San9.Content.Images.ImageAsset>(), () => 0, previous, current);
        static bool HasVision(MapScene scene) => scene.Sample(Assert.Single(scene.Units)).Animations.Any(a => a.Depth < 30000);

        var moving = Scene(before, current: day);
        var region = new MapSceneRegion(moving, new(-10000, -10000, 100000, 100000), [], moving.Units, [], [], [], []);
        long visibleRevision = region.UnitAnimationRevision(region.Bounds);
        Assert.Equal(visibleRevision, region.UnitAnimationRevision(region.Bounds));
        long hiddenRevision = region.UnitAnimationRevision(new(200000, 200000, 10, 10));
        Assert.True(hiddenRevision > visibleRevision);
        Assert.True(region.UnitAnimationRevision(region.Bounds) > hiddenRevision);
        Assert.False(HasVision(moving));
        day.Advance(.5);
        Assert.True(HasVision(moving));
        day.Advance(.5);
        Assert.True(HasVision(moving));
        var published = Scene(after, moving);
        Assert.True(HasVision(published));
        var nextDay = new TurnDayPresentation(after, after, [], new MapDayPlan(map, after, []));
        var prepared = Scene(after, published, nextDay);
        Assert.True(HasVision(prepared));
        nextDay.Advance(.1);
        Assert.False(HasVision(prepared));
        Assert.False(HasVision(Scene(before, published)));
    }

    [Fact]
    public void RepeatedSourceReplacesTheBodyWaitAndKeepsBothVolleys()
    {
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "replies", 190, 1, 1, 5,
            [new(5, "player", 0, 100)], [new(1, "city", 5, 0, 0, 100, 100, 100, 60, [])], [], [])
        {
            Units = [new(10, 5, new(2, 2), 100) { FormationId = 10000 },
                new(20, 5, new(12, 2), 100) { FormationId = 10000 }],
            Military = new(3, 1, [0, 0, 0], [new(0, 1, null)],
                [new(10000, 1, 50, 1000, 5, new(1, 1), TravelMode.Land)], [new(1, new(2, 2), new(1, 1))]),
            Information = new([], [], [], [new CorpsState(5, 5, null, new(100, 100)) { IsRoot = true }], [])
                { Formations = [new(10000, "formation")] }
        };
        var session = GameSession.Start(definition, 1);
        var catalog = UnitVisualFixture.Catalog with
        {
            Bodies = UnitVisualFixture.Catalog.Bodies.Select(b => b with
            {
                MeleeAttack = new([new(Enumerable.Repeat("melee", 8).ToArray()) { BlocksDayProgress = true }], MapProjectileKind.Arrow, 5),
                RangedAttack = new([new(Enumerable.Repeat("ranged", 8).ToArray()) { BlocksDayProgress = true }], MapProjectileKind.Arrow, 5)
            }).ToArray(),
            Projectiles = new() { [MapProjectileKind.Arrow] = new(["arrow"], 64, false, 0, 0) }
        };
        var clips = UnitVisualFixture.Clips.Values.Concat(new MapAnimationClip[]
            { new("melee", ["body", "body"], 100, []), new("ranged", Enumerable.Repeat("body", 30).ToArray(), 100, []) }).ToArray();
        var map = new MapPackage(MapPackage.CurrentSchemaVersion, "fixture", "replies", "spring", 3, 1, 13056, 6656,
            Enumerable.Repeat(new MapSquare(0, 1, 0, 0), 3).ToArray(), [], [new("body", "body.png", 32, 32)], [], [], [],
            [new(1, 0, 0, 0)], Enumerable.Range(1, 14).Select(id => new MapComposition(id, [])).ToArray(),
            [], [], [], [], new(clips, []), []) { BuildingTypes = [new(0, 100)], Units = catalog };
        DomainEvent[] events = [new BattleDamageEvent(session.Snapshot.Clock, 10, 20, null, 10, 0, 10, 0, 0),
            new BattleDamageEvent(session.Snapshot.Clock, 10, null, 1, 10, 0, 10, 0, 0)];
        var texts = new San9.Content.Text.TextCatalog(San9.Content.Text.TextCatalog.CurrentSchemaVersion, []);
        var battle = San9.Application.Presentation.BattlePresentationPlan.Create(session.View, session.View, events,
            session.Messages(texts), texts, new([], null));
        var plan = new MapDayPlan(map, session.View, events);
        var bounds = new San9.Rendering.MapPresentationBounds(-100000, -100000, 200000, 200000);
        var day = new TurnDayPresentation(session.View, session.View, events, plan, battle: battle) { Viewport = () => bounds };
        var scene = new MapScene(map, new San9.Rendering.TerrainRenderer(map, [], []), "spring", session.View,
            new Dictionary<string, San9.Content.Images.ImageAsset>(), () => 0, day: day);
        day.Advance(1);
        Assert.Equal(San9.Rendering.UnitVisualAction.Ranged, plan.Attacks[0].Action);
        Assert.Equal(San9.Rendering.UnitVisualAction.Melee, plan.Attacks[1].Action);
        Assert.All(plan.Attacks, attack => Assert.Equal(5, day.PresentedShots(attack).Count));
        var unit = scene.Units.Single(u => u.Id == 10);
        Assert.Equal(San9.Rendering.UnitVisualAction.Melee, scene.Sample(unit).Action);
        string? attackId = scene.Sample(unit).AttackId;
        day.Advance(.1);
        Assert.Equal(attackId, scene.Sample(unit).AttackId);
        Assert.Equal(100, scene.Sample(unit).AttackMilliseconds!.Value, 6);
        double blocking = Math.Max(plan.Admit(plan.Attacks[1], bounds).BodyBlockingDuration,
            plan.Attacks.Max(attack => plan.Admit(attack, bounds).ProjectileBlockingDuration));
        Assert.True(blocking < plan.Admit(plan.Attacks[0], bounds).BodyBlockingDuration);
        day.Advance(blocking - .1);
        Assert.True(day.IsComplete);
        Assert.Equal(1 + blocking, day.VisualElapsed, 8);
    }

    [Fact]
    public void SiegeAttacksSeparatedByNonvisualFactsShareOneBlockingWindow()
    {
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "siege", 190, 1, 1, 5,
            [new(5, "player", 0, 100)], [new(1, "city", 5, 3, 0, 100, 100, 100, 60, [])], [], [])
        {
            Units = [new(10, 5, new(2, 2), 100) { FormationId = 10000 },
                new(11, 5, new(7, 2), 100) { FormationId = 10000 },
                new(12, 5, new(12, 2), 100) { FormationId = 10000 }],
            Military = new(4, 1, [0, 0, 0, 0], [new(0, 1, null)],
                [new(10000, 1, 50, 1000, 5, new(1, 1), TravelMode.Land)], [new(1, new(17, 2), new(1, 1))]),
            Information = new([], [], [], [new CorpsState(5, 5, null, new(100, 100)) { IsRoot = true }], [])
                { Formations = [new(10000, "formation")] }
        };
        var session = GameSession.Start(definition, 1);
        var catalog = UnitVisualFixture.Catalog with
        {
            Bodies = UnitVisualFixture.Catalog.Bodies.Select(b => b with
                { MeleeAttack = new([new(Enumerable.Repeat("attack", 8).ToArray())], MapProjectileKind.Arrow, 5) }).ToArray(),
            Projectiles = new() { [MapProjectileKind.Arrow] = new(["arrow"], 64, false, 0, 0) }
        };
        var map = new MapPackage(MapPackage.CurrentSchemaVersion, "fixture", "siege", "spring", 4, 1, 13056, 6656,
            Enumerable.Repeat(new MapSquare(0, 1, 0, 0), 4).ToArray(), [], [new("body", "body.png", 32, 32)], [], [], [],
            [new(1, 3, 0, 0)], Enumerable.Range(1, 14).Select(id => new MapComposition(id, [])).ToArray(),
            [], [], [], [], new([new("attack", ["body"], 100, [])], []), [])
            { BuildingTypes = [new(0, 100)], Units = catalog };
        var clock = session.Snapshot.Clock;
        List<DomainEvent> events = [];
        foreach (int id in new[] { 10, 11, 12 })
        {
            events.Add(new BattleDamageEvent(clock, id, null, 1, 10, 0, 10, 1, 0));
            events.Add(new TacticProficiencyGrownEvent(clock, id, 5, 0, 1));
            events.Add(new CityBattleDamageEvent(clock, 1, id, 1, 1, 1));
        }
        var texts = new San9.Content.Text.TextCatalog(San9.Content.Text.TextCatalog.CurrentSchemaVersion, []);
        var battle = San9.Application.Presentation.BattlePresentationPlan.Create(session.View, session.View, events,
            session.Messages(texts), texts, new([], null));
        var plan = new MapDayPlan(map, session.View, events);
        var bounds = new San9.Rendering.MapPresentationBounds(-100000, -100000, 200000, 200000);
        var day = new TurnDayPresentation(session.View, session.View, events, plan, battle: battle) { Viewport = () => bounds };
        day.Advance(1);
        Assert.Equal(3, plan.Attacks.Count);
        Assert.All(plan.Attacks, attack => Assert.True(day.IsAttackPresented(attack)));
        Assert.All(plan.Attacks, attack => Assert.Equal(0, day.AttackMilliseconds(attack)));
        Assert.True(day.IsCombatBlocking);
        double blocking = plan.Attacks.Max(attack => plan.Admit(attack, bounds).BlockingDuration);
        day.Advance(blocking);
        Assert.True(day.IsComplete);
        Assert.Equal(1 + blocking, day.VisualElapsed, 8);
        Assert.Equal(70u, day.MapFrame.Settlements[1].Soldiers);

        // The finite state effect and volleys share their longest wait. A longer
        // number animation must remain independent of that blocking interval.
        var content = new San9.Content.Interface.CommandPresentation([], null)
        {
            Battle = new(new(), new(), "hit", "block")
            {
                ConditionEffects = new() { [UnitCondition.Confused] = new("state", 8, 300) },
                MapNumbers = new("digits", 12, 20, 1000, 8, 4)
            }
        };
        var withState = events.ToList();
        withState.Insert(1, new BattleConditionChangedEvent(clock, 1, true, UnitCondition.Confused, 3));
        withState.Add(((BattleDamageEvent)events[0]) with { TacticId = 99 });
        var liveBattle = San9.Application.Presentation.BattlePresentationPlan.Create(session.View, session.View, withState,
            session.Messages(texts), texts, content);
        Assert.Single(liveBattle.MapBatches);
        Assert.Single(liveBattle.MapNumbers);
        var livePlan = new MapDayPlan(map, session.View, withState);
        var effect = Assert.Single(liveBattle.MapEffects);
        var assets = new Dictionary<string, San9.Content.Images.ImageAsset> { ["state"] = new("state", "state.png", 512, 64) };
        var liveDay = new TurnDayPresentation(session.View, session.View, withState, livePlan, battle: liveBattle)
            { Viewport = () => bounds, MapEffectAssets = assets };
        liveDay.Advance(100);
        Assert.True(liveDay.IsMapEffectPresented(effect.Id));
        Assert.True(liveDay.IsComplete);
        Assert.Equal(1 + Math.Max(blocking, 2.4), liveDay.VisualElapsed, 8);
        var hiddenDay = new TurnDayPresentation(session.View, session.View, withState, livePlan, battle: liveBattle)
            { Viewport = () => new(-10000, -10000, 1, 1), MapEffectAssets = assets };
        hiddenDay.Advance(1);
        Assert.True(hiddenDay.IsComplete);
        Assert.False(hiddenDay.IsMapEffectPresented(effect.Id));
        Assert.Equal(1, hiddenDay.VisualElapsed, 8);
    }

    [Fact]
    public void AdmittedWallArrowOutsideTheEndpointBoundsIsPreparedAndDrawn()
    {
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "wall", 190, 1, 1, 5,
            [new(5, "player", 5, 100)],
            [new(1, "source", 5, 0, 0, 100, 100, 100, 60, []),
             new(2, "target", 5, 1, 0, 100, 100, 100, 60, [])], [], [])
        { Information = new([], [], [], [new CorpsState(5, 5, null, new(100, 100)) { IsRoot = true }], []) };
        var session = GameSession.Start(definition, 1);
        var map = new MapPackage(MapPackage.CurrentSchemaVersion, "fixture", "wall", "spring", 2, 1, 13056, 6656,
            Enumerable.Repeat(new MapSquare(0, 1, 0, 0), 2).ToArray(), [], [], [], [], [],
            [new(1, 0, 0, 0), new(2, 1, 0, 0)],
            Enumerable.Range(1, 14).Select(id => new MapComposition(id, [])
            { ArrowOrigins = Enumerable.Range(0, 8).Select(_ => new MapFlag[] { new(0, -600) }).ToArray() }).ToArray(),
            [], [], [], [], new([], []), [])
        {
            BuildingTypes = [new(0, 100)],
            Units = MapUnitCatalog.Empty with { Projectiles = new() { [MapProjectileKind.Arrow] = new(["arrow"], 64, false, 0, 0) } }
        };
        DomainEvent[] events = [new BattleDamageEvent(session.Snapshot.Clock, 1, null, 2, 10, 0, 10, 0, 0, true)];
        var plan = new MapDayPlan(map, session.View, events);
        var origin = Assert.Single(Assert.Single(plan.Attacks).Origins);
        var viewport = new global::Godot.Rect2((float)origin.X - 10, (float)origin.Y - 10, 20, 20);
        var day = new TurnDayPresentation(session.View, session.View, events, plan)
        { Viewport = () => new(viewport.Position.X, viewport.Position.Y, viewport.Size.X, viewport.Size.Y) };
        var scene = new MapScene(map, new San9.Rendering.TerrainRenderer(map, [], []), "spring", session.View,
            new Dictionary<string, San9.Content.Images.ImageAsset>(), () => 0, day: day);

        Assert.Contains("arrow", scene.MotionAssets([], viewport));
        day.Advance(1);
        Assert.True(day.IsCombatBlocking);
        var projectile = Assert.Single(scene.Projectiles(viewport));
        Assert.Equal((origin.X, origin.Y), (projectile.X, projectile.Y));
    }

    [Theory]
    [InlineData(false, 1.6)]
    [InlineData(true, .4)]
    public void BodyAtTheMovementDestinationHasAnIndependentNonblockingLifetime(bool startsOnWater, double expectedDuration)
    {
        var from = new WorldPosition(startsOnWater ? 7 : 2, 2);
        var to = new WorldPosition(startsOnWater ? 2 : 7, 2);
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "shore", 190, 1, 1, 5,
            [new(5, "player", 5, 100)], [new(1, "target", 5, to.X / 5, 0, 100, 100, 100, 60, [])], [], [])
        {
            Units = [new(10, 5, from, 100) { FormationId = 10000, NavalFormationId = 20000 }],
            Military = new(2, 1, [0, 1], [new(0, 1, null), new(1, null, 1) { IsWater = true }],
                [new(10000, 1, 50, 1000, 5, new(1, 1), TravelMode.Land), new(20000, 1, 50, 1000, 5, new(1, 1), TravelMode.Water)],
                [new(1, to, new(1, 1))]),
            Information = new([], [], [], [new CorpsState(5, 5, null, new(100, 100)) { IsRoot = true }], [])
                { Formations = [new(10000, "land"), new(20000, "ship")] }
        };
        var session = GameSession.Start(definition, 1);
        var catalog = new MapUnitCatalog(
            [new("land", Enumerable.Repeat("land", 8).ToArray(), 0, 0)
                { MeleeAttack = new([new(Enumerable.Repeat("land", 8).ToArray())], MapProjectileKind.None, 0) },
             new("ship", Enumerable.Repeat("ship", 8).ToArray(), 0, 0)
                { MeleeAttack = new([new(Enumerable.Repeat("ship", 8).ToArray())], MapProjectileKind.None, 0) }],
            [new(10000, [new(1, [new("land", 0, 0)], new("land", 0, 0))], 0),
             new(20000, [new(1, [new("ship", 0, 0)], new("ship", 0, 0))], 0)], [], -1);
        var map = new MapPackage(MapPackage.CurrentSchemaVersion, "fixture", "shore", "spring", 2, 1, 13056, 6656,
            [new(0, 1, 0, 0), new(1, 1, 0, 0)], [], [], [], [], [], [new(1, to.X / 5, 0, 0)],
            Enumerable.Range(1, 14).Select(id => new MapComposition(id, [])).ToArray(), [], [], [], [],
            new([new("land", ["l0", "l1"], 200, []),
                 new("ship", ["s0", "s1", "s2", "s3", "s4", "s5", "s6", "s7"], 200, [])], []), [])
            { BuildingTypes = [new(0, 100)], Units = catalog };
        DomainEvent[] events = [new UnitMovedEvent(session.Snapshot.Clock, 10, from, to, 1, NavigationStatus.Arrived),
            new BattleDamageEvent(session.Snapshot.Clock, 10, null, 1, 10, 0, 10, 0, 0)];
        var plan = new MapDayPlan(map, session.View, events);
        var attack = Assert.Single(plan.Attacks);
        Assert.Equal(expectedDuration, plan.BodyDuration(attack), 8);
        var day = new TurnDayPresentation(session.View, session.View, events, plan) { Viewport = () => new(-100000, -100000, 200000, 200000) };
        day.Advance(.9);
        Assert.False(day.IsBodyPresented(attack));
        day.Advance(.1);
        Assert.True(day.IsBodyPresented(attack));
        Assert.False(day.IsCombatBlocking);
        Assert.True(day.IsComplete);
        Assert.Equal(1, day.VisualElapsed, 8);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void BodyBlockingComesFromTheSelectedActionAndItsAdmission(bool visible, bool blocks)
    {
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "melee", 190, 1, 1, 5,
            [new(5, "player", 0, 100)], [new(1, "place", 5, 0, 0, 100, 100, 100, 60, [])], [], [])
        {
            Units = [new(10, 5, new(2, 2), 100) { FormationId = 10000 }],
            Military = new(1, 1, [0], [new(0, 1, null)],
                [new(10000, 1, 50, 1000, 5, new(1, 1), TravelMode.Land)], [new(1, new(2, 2), new(1, 1))]),
            Information = new([], [], [], [new CorpsState(5, 5, null, new(100, 100)) { IsRoot = true }], [])
                { Formations = [new(10000, "formation")] }
        };
        var session = GameSession.Start(definition, 1);
        var clips = UnitVisualFixture.Clips.Values.Append(new("melee", ["a", "b", "c", "d"], 200, [])).ToArray();
        var catalog = UnitVisualFixture.Catalog with
        {
            Bodies = UnitVisualFixture.Catalog.Bodies.Select(b => b with
                { MeleeAttack = new([new(Enumerable.Repeat("melee", 8).ToArray()) { BlocksDayProgress = blocks }], MapProjectileKind.None, 0)
                    { Timings = [new(200, 700)] } }).ToArray()
        };
        var map = new MapPackage(MapPackage.CurrentSchemaVersion, "fixture", "melee", "spring", 1, 1, 13056, 6656,
            [new(0, 1, 0, 0)], [], [], [], [], [], [new(1, 0, 0, 0)],
            Enumerable.Range(1, 14).Select(id => new MapComposition(id, [])).ToArray(),
            [], [], [], [], new(clips, []), []) { BuildingTypes = [new(0, 100)], Units = catalog };
        DomainEvent[] events = [new BattleDamageEvent(session.Snapshot.Clock, 10, null, 1, 10, 0, 10, 0, 0)];
        var plan = new MapDayPlan(map, session.View, events);
        var attack = Assert.Single(plan.Attacks);
        Assert.Equal(MapProjectileKind.None, attack.Projectile);
        var day = new TurnDayPresentation(session.View, session.View, events, plan) { Viewport = () => visible ? new(-100000, -100000, 200000, 200000) : null };
        double clock = 0;
        var terrain = new San9.Rendering.TerrainRenderer(map, [], []);
        var scene = new MapScene(map, terrain, "spring", session.View, new Dictionary<string, San9.Content.Images.ImageAsset>(), () => clock, day: day);
        day.Advance(1);
        Assert.Equal(visible && blocks, day.IsCombatBlocking);
        Assert.Equal(visible, day.IsBodyPresented(attack));
        Assert.Equal(1, day.Elapsed, 8);
        if (visible && !blocks)
        {
            var published = new MapScene(map, terrain, "spring", session.View, new Dictionary<string, San9.Content.Images.ImageAsset>(), () => clock, scene);
            clock = 300;
            var continued = published.Sample(Assert.Single(published.Units));
            Assert.Equal(San9.Rendering.UnitVisualAction.Melee, continued.Action);
            Assert.Equal(100, continued.AttackMilliseconds);
            clock = 1000;
            Assert.Null(published.Sample(Assert.Single(published.Units)).AttackMilliseconds);
        }
        day.Advance(1);
        Assert.True(day.IsComplete);
        Assert.Equal(visible && blocks ? 2 : 1, day.VisualElapsed, 8);
    }

    [Fact]
    public void VisibleAttackHoldsDailyProgressWhileVisualLifetimesContinueAndIndependentViewsStayIndependent()
    {
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "plan", 190, 1, 1, 5,
            [new(5, "player", 5, 100)],
            [new(1, "source", 5, 0, 0, 100, 100, 100, 60, []),
             new(2, "target", 5, 10, 0, 100, 100, 100, 60, [])], [], [])
        { Information = new([], [], [], [new CorpsState(5, 5, null, new(100, 100)) { IsRoot = true }], []) };
        var session = GameSession.Start(definition, 1);
        var map = new MapPackage(MapPackage.CurrentSchemaVersion, "fixture", "plan", "spring", 11, 1, 13056, 6656,
            Enumerable.Repeat(new MapSquare(0, 1, 0, 0), 11).ToArray(), [], [], [], [], [],
            [new(1, 0, 0, 0), new(2, 10, 0, 0)],
            Enumerable.Range(1, 14).Select(id => new MapComposition(id, [])).ToArray(),
            [], [], [], [], new([new("impact", ["a", "b"], 2000, [])], []), [])
        {
            BuildingTypes = [new(0, 100)],
            Units = MapUnitCatalog.Empty with
            {
                Projectiles = new()
                {
                [MapProjectileKind.Arrow] = new(["arrow"], 64, false, 0, 0) { Impact = new("impact", 0, 0) }
                }
            }
        };
        DomainEvent[] events = [new BattleDamageEvent(session.Snapshot.Clock, 1, null, 2, 10, 0, 10, 0, 0, true)];
        var hash = session.Snapshot;
        var plan = new MapDayPlan(map, session.View, events);
        var tacticPlan = new MapDayPlan(map, session.View, [((BattleDamageEvent)events[0]) with { TacticId = 99 }]);
        Assert.Empty(tacticPlan.Attacks);
        var ordinaryInTactic = new MapDayPlan(map, session.View,
            [((BattleDamageEvent)events[0]) with { IsTacticExchange = true }]);
        Assert.Empty(ordinaryInTactic.Attacks);
        var siegeContinuation = new MapDayPlan(map, session.View,
            [((BattleDamageEvent)events[0]) with { ContinuesSiege = true }]);
        Assert.Empty(siegeContinuation.Attacks);
        var attack = Assert.Single(plan.Attacks);
        double blocking = plan.Admit(attack, new(-100000, -100000, 200000, 200000)).BlockingDuration;
        Assert.True(blocking > 4);
        bool visible = true;
        var first = new TurnDayPresentation(session.View, session.View, events, plan) { Viewport = () => visible ? new(-100000, -100000, 200000, 200000) : null };
        var preview = new TurnDayPresentation(session.View, session.View, events, plan);
        first.Advance(1);
        Assert.Equal(1, first.Elapsed, 8);
        Assert.Equal(1, first.VisualElapsed, 8);
        Assert.True(first.IsCombatBlocking);
        visible = false;
        first.Advance(10, paused: true);
        Assert.Equal(1, first.Elapsed, 8);
        Assert.False(first.IsCombatBlocking);
        first.Advance(.1);
        Assert.True(first.IsComplete);
        Assert.Equal(0, preview.Elapsed);
        preview.Advance(1);
        Assert.True(preview.IsComplete);
        Assert.False(preview.IsAttackPresented(attack));
        Assert.Equal(1, preview.VisualElapsed);
        var offscreen = new TurnDayPresentation(session.View, session.View, events, plan) { Viewport = () => visible ? new(-100000, -100000, 200000, 200000) : null };
        offscreen.Advance(1);
        visible = true;
        offscreen.Advance(.1);
        Assert.True(offscreen.IsComplete);
        Assert.False(offscreen.IsAttackPresented(attack));
        Assert.Empty(plan.Admit(attack, new(attack.FromX + 1000, attack.FromY, 100, 100)).Shots);
        Assert.NotEmpty(plan.Admit(attack, new(attack.FromX, attack.FromY, 100, 100)).Shots);
        var replay = new TurnDayPresentation(session.View, session.View, events, plan) { Viewport = () => new(-100000, -100000, 200000, 200000) };
        replay.Advance(100);
        Assert.True(replay.IsComplete);
        Assert.Equal(1 + blocking, replay.VisualElapsed, 8);

        // State publication does not split the live batch or add another volley wait.
        var hit = (BattleDamageEvent)events[0];
        DomainEvent[] ordered = [hit, hit with { AttackerId = 2, SettlementId = 1 },
            new BattleConditionChangedEvent(hit.Clock, 2, true, UnitCondition.Confused, 3), hit];
        var text = new San9.Content.Text.TextCatalog(San9.Content.Text.TextCatalog.CurrentSchemaVersion, []);
        var battle = San9.Application.Presentation.BattlePresentationPlan.Create(session.View, session.View, ordered,
            session.Messages(text), text, new([], null));
        Assert.Equal((0, 3), (Assert.Single(battle.MapBatches).FirstFactIndex, battle.MapBatches[0].LastFactIndex));
        var orderedPlan = new MapDayPlan(map, session.View, ordered);
        var orderedDay = new TurnDayPresentation(session.View, session.View, ordered, orderedPlan, battle: battle)
            { Viewport = () => new(-100000, -100000, 200000, 200000) };
        orderedDay.Advance(1);
        Assert.True(orderedDay.IsAttackPresented(orderedPlan.Attacks[0]));
        Assert.True(orderedDay.IsAttackPresented(orderedPlan.Attacks[1]));
        Assert.True(orderedDay.IsAttackPresented(orderedPlan.Attacks[2]));
        Assert.True(orderedDay.IsFactReady(2));
        double firstPhase = orderedPlan.Attacks.Max(a => orderedPlan.Admit(a, new(-100000, -100000, 200000, 200000)).BlockingDuration);
        orderedDay.Advance(firstPhase);
        Assert.True(orderedDay.IsFactReady(2));
        Assert.True(orderedDay.IsAttackPresented(orderedPlan.Attacks[2]));
        Assert.Equal(firstPhase * 1000, orderedDay.AttackMilliseconds(orderedPlan.Attacks[2]), 6);
        Assert.True(orderedDay.IsComplete);
        orderedDay.Advance(100);
        Assert.True(orderedDay.IsComplete);

        string[] roles = ["support", "announcement", "action", "hit"];
        var dialogueText = new San9.Content.Text.TextCatalog(San9.Content.Text.TextCatalog.CurrentSchemaVersion, [])
        { Messages = roles.ToDictionary(r => r, r => new San9.Content.Text.MessageDefinition([new San9.Content.Text.MessageLiteral(r)])) };
        var messages = roles.ToDictionary(r => r, r => new San9.Content.Interface.DecisionMessageDefinition(r, []));
        var content = new San9.Content.Interface.CommandPresentation([], null)
        { Battle = new(messages, new() { [99] = new(messages, new("support-effect", 2, 200), null) { Support = true, EffectAtSource = true } }, "hit", "block") };
        var source = new BattleObjectContext(1, true, 5, null, null, 1, true);
        var support = new TacticActivatedEvent(hit.Clock, 1, true, 2, true, 99, [new(7, 99)], false, 10)
        { Occurrence = new(source, source with { Id = 2, ServicePlaceId = 2 }, null, null, UnitCondition.Normal, null, null, null, false) };
        DomainEvent[] withDialogue = [hit, support, hit];
        var dialogueBattle = San9.Application.Presentation.BattlePresentationPlan.Create(session.View, session.View, withDialogue,
            session.Messages(dialogueText), dialogueText, content);
        var dialogueMap = new MapDayPlan(map, session.View, withDialogue);
        var dialogueDay = new TurnDayPresentation(session.View, session.View, withDialogue, dialogueMap, battle: dialogueBattle)
            { Viewport = () => new(-100000, -100000, 200000, 200000),
                MapEffectAssets = new Dictionary<string, San9.Content.Images.ImageAsset> { ["support-effect"] = new("support-effect", "effect.png", 64, 32) } };
        dialogueDay.Advance(1);
        Assert.Empty(dialogueDay.PendingDialogues);
        Assert.Empty(dialogueDay.ReadyRecords);
        dialogueDay.Advance(blocking);
        Assert.Single(dialogueDay.PendingDialogues);
        Assert.False(dialogueDay.IsAttackPresented(dialogueMap.Attacks[1]));
        dialogueDay.Advance(100);
        Assert.False(dialogueDay.IsAttackPresented(dialogueMap.Attacks[1]));
        dialogueDay.CompleteDialogue(Assert.Single(dialogueDay.PendingDialogues).Id);
        dialogueDay.Advance(0);
        Assert.True(dialogueDay.IsAttackPresented(dialogueMap.Attacks[1]));
        dialogueDay.Advance(100);
        Assert.True(dialogueDay.IsComplete);
        StateAssert.Equal(hash, session.Snapshot);
    }
}
