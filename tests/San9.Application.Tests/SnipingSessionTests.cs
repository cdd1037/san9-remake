using San9.Tests;
using System.Collections.Immutable;
using System.Text.Json;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class SnipingSessionTests
{
    private static ScenarioInitialState Scenario(string path)
    {
        bool facility = path is "facility" or "facility-counter", assault = path == "assault";
        var combat = new CombatProfile(0, 0, 0, 1000, 50);
        var tactic = new TacticRule(9000, 3, TacticEffect.Martial, 1, 9, 0, true, true, [100])
            { SnipingProficiencyCategory = 3 };
        var actor = new OfficerState(11, 1, facility ? 1 : null, new(100, 70, 70, 100), false)
        {
            UnitId = facility ? null : 1, IsCommander = facility, Status = OfficerStatus.Ruler,
            LearnedTactics = [9000], EquippedTacticId = 9000,
            TacticProficiencies = [0, 0, 0, 1000, 0, 0, 0, 0, 0]
        };
        var commander = new OfficerState(21, 2, assault ? 2 : null, new(100, 70, 70, 100), false)
            { UnitId = assault ? null : 2, IsCommander = assault };
        var ruler = new OfficerState(22, 2, assault ? 2 : null, new(70, 70, 70, 70), false)
            { UnitId = assault ? null : 2, Status = OfficerStatus.Ruler };
        return new(ScenarioInitialState.CurrentSchemaVersion, "sniping-session", 190, 1, 1, 2,
            [new(1, "first", 1, 100) { LeaderId = 11 }, new(2, "second", 2, 100) { LeaderId = 22 }],
            [new(1, "home", 1, 0, 1, 10000, 100, 100, 100, []),
                new(2, "away", 2, 21, 1, 10000, 100, 100, 100, []),
                new(3, "reserve", 2, 99, 1, 10000, 100, 100, 100, [])], [], [])
        {
            NaturalDeathsEnabled = false,
            Officers = [new("actor", actor), new("commander", commander), new("ruler", ruler),
                new("heir", new(23, 2, 3, new(50, 50, 50, 50), false))],
            Units = [..(facility ? Array.Empty<UnitState>() : [new(1, 1, new(102, 7), 100000)
                { FormationId = 100, Morale = 100, ServicePlaceId = 1, CommanderId = 11,
                    MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(11, BattleRank.Front),
                    Order = assault ? new(MilitaryOrderKind.AttackSettlement, null, 2) :
                        new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 2 } }]),
                ..(assault ? Array.Empty<UnitState>() : [new(2, 2, new(facility ? 6 : 106, 7), 100000)
                { FormationId = 100, CommanderId = 21, Morale = 100, ServicePlaceId = 3,
                    Order = path == "facility-counter" ? new(MilitaryOrderKind.AttackSettlement, null, 1) : null,
                    MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(21, BattleRank.Rear).Add(22, BattleRank.Middle) }])],
            Military = new(100, 3, [.. Enumerable.Repeat(0, 300)], [new(0, 1, 1)],
                [new(100, 5, 0, 100000, 2, new(1, 1), TravelMode.Land)
                    { Combat = combat, Attack = new(10, false), SnipingChanceBonus = 100,
                        CaptureRanks = [BattleRank.Front, BattleRank.Middle], Tactics = new(100, 100, 2, [3], []) }],
                [new(1, new(2, 7), new(1, 1)) { Combat = combat, FacilityTypeId = 8000, Attack = new(10, false) },
                    new(2, new(107, 7), new(1, 1)) { Combat = combat, FacilityTypeId = 8000, Attack = new(10, false) },
                    new(3, new(497, 7), new(1, 1)) { Combat = combat, FacilityTypeId = 8000 }])
            {
                BattleDeathsAllowed = true, DuelCapturesAllowed = false, Tactics = [tactic], Geography = new(5, []),
                Facilities = [new(8000, 0, 100, new(1, 1), 0, false, true, true, 100000, null, null, null, [])
                    { Combat = combat, Attack = new(10, false), Tactics = new(100, 3, false, [9000]) }]
            }
        , Information = new([], [], [], [new San9.Core.Domain.CorpsState(1, 1, 11, new(0, 100000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 22, new(0, 100000)) { IsRoot = true }], []) };
    }

    [Theory]
    [InlineData("field", false)]
    [InlineData("field", true)]
    [InlineData("assault", false)]
    [InlineData("facility", false)]
    [InlineData("facility-counter", false)]
    public void SnipingWaitsAndResumesWithoutRepeatingDamageAndReplays(string path, bool automatic)
    {
        const ulong seed = 366UL;
        Assert.Equal(0UL, new StableRandom(StableRandom.DeriveSeed(seed, "military-sniping-death")).NextUInt64() % 100);
        Assert.True(new StableRandom(StableRandom.DeriveSeed(seed, "military-sniping")).NextUInt64() % 100 < 50);
        Assert.Equal(1UL, new StableRandom(StableRandom.DeriveSeed(seed, "military-sniping-target")).NextUInt64() % 2);
        var scenario = Scenario(path);
        var session = GameSession.Start(scenario, seed, battleDeathsEnabled: true);
        Assert.True(session.Snapshot.Simulation.BattleDeathsEnabled);
        Assert.Equal(70, session.Snapshot.Officers.Single(p => p.Id == 22).BaseAbilities.War);

        session.BeginRecording();
        var restored = SaveService.Decode(SaveService.Encode(session), scenario);
        var arrival = session.AdvanceXun();
        EventAssert.Equal(arrival.Events, restored.AdvanceXun().Events);
        var prompt = Assert.IsType<SnipingSuccessionPrompt>(session.GetSnipingSuccessionPrompt());
        Assert.Equal(22, prompt.RulerId);
        Assert.Equal(new[] { 21, 23 }, prompt.CandidateIds);
        Assert.Equal(new GameClock(190, 1, 2), session.Snapshot.Clock);
        Assert.Equal(OfficerStatus.Ruler, session.Snapshot.Officers.Single(p => p.Id == 22).Status);
        Assert.DoesNotContain(arrival.Events, e => e is OfficerDiedEvent or XunAdvancedEvent);
        Assert.DoesNotContain(arrival.Events.OfType<StageCompletedEvent>(), e => e.Stage == ResolutionStage.DailyMapUpdate);
        Assert.Equal(SaveFailure.NotStable, Assert.Throws<SaveException>(() => SaveService.Encode(session)).Failure);
        Assert.Throws<InvalidOperationException>(() => session.CaptureRecording());
        var hash = session.Snapshot;
        long sequence = session.Snapshot.Simulation.NextSequence;
        Assert.Equal(CommandRejection.DecisionPending, session.Execute(new("player"), new WaitCommand(Guid.NewGuid(), 2, sequence)).Submission.Rejection);
        Assert.Equal(CommandRejection.NotActiveController, session.Execute(new("ai:1"),
            new ChooseSnipingSuccessorCommand(Guid.NewGuid(), 2, sequence, 22, 23)).Submission.Rejection);
        Assert.Equal(CommandRejection.InvalidDecision, session.Execute(new("player"),
            new ChooseSnipingSuccessorCommand(Guid.NewGuid(), 2, sequence, 21, 23)).Submission.Rejection);
        Assert.Equal(CommandRejection.InvalidDecision, session.Execute(new("player"),
            new ChooseSnipingSuccessorCommand(Guid.NewGuid(), 2, sequence, 22, 11)).Submission.Rejection);
        StateAssert.Equal(hash, session.Snapshot);
        var pending = session.Snapshot.Simulation.PendingSniping!;
        var snapshot = new SessionSnapshot(session.Snapshot, [], [], session.NextOperationSequence);
        foreach (var invalid in new[] {
            pending with { RulerId = 21 }, pending with { Exchange = null }, pending with { Tactic = null },
            pending with { Exchange = pending.Exchange! with { XunStarted = new(190, 1, 11) } },
            pending with { Tactic = pending.Tactic! with { Target = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 99 } } } })
            Assert.Throws<ArgumentException>(() => GameSession.Restore(scenario, snapshot with {
                World = snapshot.World with { Simulation = snapshot.World.Simulation with { PendingSniping = invalid } } }));
        var answer = new ChooseSnipingSuccessorCommand(Guid.NewGuid(), 2, sequence, 22, automatic ? null : 23);
        var result = session.Execute(new("player"), answer);
        Assert.True(result.Submission.Accepted);
        EventAssert.Equal(result.Events, restored.Execute(new("player"), answer).Events);
        StateAssert.Equal(session.Capture(), restored.Capture());
        Assert.Null(session.GetSnipingSuccessionPrompt());
        Assert.Equal(automatic ? 21 : 23, session.Snapshot.Forces.Single(f => f.Id == 2).LeaderId);
        Assert.Equal(new GameClock(190, 1, 11), session.Snapshot.Clock);
        var all = arrival.Events.Concat(result.Events).ToArray();
        Assert.Single(all.OfType<TacticSnipingResolvedEvent>(), e => e.OfficerId == 22);
        Assert.Single(all.OfType<OfficerDiedEvent>(), e => e.OfficerId == 22 && e.Cause == OfficerDeathCause.Sniping);
        Assert.Equal(10, all.OfType<StageCompletedEvent>().Count(e => e.Stage == ResolutionStage.DailyMapUpdate));
        Assert.DoesNotContain(result.Events.OfType<BattleDamageEvent>(), e => e.Clock.Day == 2 && e.AttackerId == 1 &&
            e.TargetUnitId == pending.Tactic!.Target.UnitId && e.SettlementId == pending.Tactic.Target.SettlementId);
        Assert.DoesNotContain("pendingSniping", System.Text.Encoding.UTF8.GetString(SaveService.Encode(session)));
        StateAssert.Equal(session.Capture(), SaveService.Decode(SaveService.Encode(session), scenario).Capture());
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), scenario, GameSession.EngineFactory(scenario));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }

    [Fact]
    public void SuccessorFieldMustBeExplicitEvenForAutomaticSelection()
    {
        GameCommand command = new ChooseSnipingSuccessorCommand(Guid.NewGuid(), 2, 0, 22, null);
        var json = JsonSerializer.Serialize(command, CanonicalJson.Options);
        Assert.Equal(command, JsonSerializer.Deserialize<GameCommand>(json, CanonicalJson.Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GameCommand>(json.Replace(",\"successorId\":null", ""), CanonicalJson.Options));
    }
    [Fact]
    public void AutomaticSnipingSuccessionCompletesWithoutPlayerInputAndReplays()
    {
        const ulong seed = 366UL;
        Assert.Equal(0UL, new StableRandom(StableRandom.DeriveSeed(seed, "military-sniping-death")).NextUInt64() % 100);
        Assert.True(new StableRandom(StableRandom.DeriveSeed(seed, "military-sniping")).NextUInt64() % 100 < 50);
        Assert.Equal(1UL, new StableRandom(StableRandom.DeriveSeed(seed, "military-sniping-target")).NextUInt64() % 2);
        var scenario = Scenario("field") with { PlayerForceId = 1 };
        var session = GameSession.Start(scenario, seed, battleDeathsEnabled: true);
        var events = AutomaticResponseReplay.Advance(session, scenario, 1);
        Assert.Contains(events.OfType<OfficerDiedEvent>(), e => e.OfficerId == 22);
        Assert.NotEqual(22, session.Snapshot.Forces.Single(f => f.Id == 2).LeaderId);
    }
}
