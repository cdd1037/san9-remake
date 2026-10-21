using San9.Tests;
using System.Collections.Immutable;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class DuelSessionTests
{
    private static ScenarioInitialState Scenario()
    {
        var combat = new CombatProfile(0, 0, 0, 1000, 50);
        OfficerInitialState Person(int id, int force, int war, OfficerStatus status = OfficerStatus.Serving) =>
            new($"officer.{id}", new(id, force, null, new(war, 50, 50, 50), false) { UnitId = force, Status = status });
        return new(ScenarioInitialState.CurrentSchemaVersion, "duel-session", 190, 1, 1, 2,
            [new(1, "first", 1, 100) { LeaderId = 11 }, new(2, "second", 2, 100) { LeaderId = 22 }],
            [new(1, "home", 1, 0, 0, 100, 100, 100, 100, []), new(2, "away", 2, 3, 0, 100, 100, 100, 100, [])], [], [])
        {
            NaturalDeathsEnabled = false,
            Officers = [Person(11, 1, 79, OfficerStatus.Ruler), Person(21, 2, 20), Person(22, 2, 50, OfficerStatus.Ruler), Person(23, 2, 30)],
            Units = [new(1, 1, new(6, 2), 10000) { FormationId = 1, Morale = 100, CommanderId = 11,
                    MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(11, BattleRank.Front),
                    Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 2 } },
                new(2, 2, new(7, 2), 10000) { FormationId = 1, Morale = 100, CommanderId = 21,
                    MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(21, BattleRank.Rear).Add(22, BattleRank.Front).Add(23, BattleRank.Rear) }],
            Military = new(4, 1, [0, 0, 0, 0], [new(0, 1, null)],
                [new(1, 5, 0, 100000, 3, new(1, 1), TravelMode.Land) { Combat = combat, Attack = new(1, false),
                    CaptureRanks = [BattleRank.Front, BattleRank.Front, BattleRank.Rear], Tactics = new(0, 2, [], []) }],
                [new(1, new(2, 2), new(1, 1)) { Combat = combat }, new(2, new(17, 2), new(1, 1)) { Combat = combat }])
            { BattleDeathsAllowed = true, DuelCapturesAllowed = false }
        , Information = new([], [], [], [new San9.Core.Domain.CorpsState(1, 1, 11, new(0, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 22, new(0, 10000)) { IsRoot = true }], []) };
    }

    [Fact]
    public void DuelSuccessionIsTransientAndReplaysFromStableStart()
    {
        const ulong seed = 16UL;
        Assert.True(new StableRandom(StableRandom.DeriveSeed(seed, "duel-trigger")).NextUInt64() % 100 < 3);
        Assert.True(new StableRandom(StableRandom.DeriveSeed(seed, "duel-death")).NextUInt64() % 100 < 30);
        var scenario = Scenario();
        // Isolate succession/replay from strategic decisions to withdraw from this deliberately losing duel.
        var session = GameSession.Start(scenario, seed, battleDeathsEnabled: true);
        session.BeginRecording();
        var restored = SaveService.Decode(SaveService.Encode(session), scenario);
        var arrival = session.AdvanceXun();
        EventAssert.Equal(arrival.Events, restored.AdvanceXun().Events);
        var prompt = Assert.IsType<DuelSuccessionPrompt>(session.GetDuelSuccessionPrompt());
        Assert.Equal(22, prompt.RulerId);
        Assert.Equal(SaveFailure.NotStable, Assert.Throws<SaveException>(() => SaveService.Encode(session)).Failure);
        Assert.Throws<InvalidOperationException>(() => session.CaptureRecording());
        var answer = new ChooseDuelSuccessorCommand(Guid.NewGuid(), 2, session.Snapshot.Simulation.NextSequence, 22, 21);
        var result = session.Execute(new("player"), answer);
        Assert.True(result.Submission.Accepted);
        EventAssert.Equal(result.Events, restored.Execute(new("player"), answer).Events);
        StateAssert.Equal(session.Capture(), restored.Capture());
        Assert.DoesNotContain("pendingDuel", System.Text.Encoding.UTF8.GetString(SaveService.Encode(session)));
        var recording = RecordingService.Encode(session.EndRecording());
        var replay = RecordingService.Replay(recording, scenario, GameSession.EngineFactory(scenario), verifyEvents: true);
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
        Assert.Single(replay.Events.OfType<OfficerDiedEvent>(), e => e.OfficerId == 22);
        Assert.Single(replay.Events.OfType<DuelStartedEvent>(), e => e.Clock.Day == 2);
        if (Environment.GetEnvironmentVariable("SAN9_G09_REPORT_DIR") is { Length: > 0 } output)
        {
            Directory.CreateDirectory(output);
            File.WriteAllBytes(Path.Combine(output, "duel-scenario.json"), CanonicalJson.Encode(scenario));
            File.WriteAllBytes(Path.Combine(output, "duel.recording.json"), recording);
        }
    }
    [Fact]
    public void AutomaticDuelSuccessionCompletesWithoutPlayerInputAndReplays()
    {
        const ulong seed = 16UL;
        Assert.True(new StableRandom(StableRandom.DeriveSeed(seed, "duel-trigger")).NextUInt64() % 100 < 3);
        Assert.True(new StableRandom(StableRandom.DeriveSeed(seed, "duel-death")).NextUInt64() % 100 < 30);
        var scenario = Scenario() with { PlayerForceId = 1 };
        var session = GameSession.Start(scenario, seed, battleDeathsEnabled: true);
        var events = AutomaticResponseReplay.Advance(session, scenario, 1);
        Assert.Contains(events.OfType<OfficerDiedEvent>(), e => e.OfficerId == 22);
        Assert.NotEqual(22, session.Snapshot.Forces.Single(f => f.Id == 2).LeaderId);
    }
}
