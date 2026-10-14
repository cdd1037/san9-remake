using San9.Tests;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class FacilityCombatSessionTests
{
    [Fact]
    public void PublicSiegeExposesFacilityTacticAndPreservesEffectsAcrossSave()
    {
        var combat = new CombatProfile(0, 0, 0, 1000, 50);
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "facility-battle", 190, 1, 1, 2,
            [new(1, "first", 1, 100) { LeaderId = 11 }, new(2, "second", 2, 100) { LeaderId = 21 }],
            [new(1, "defense", 1, 0, 0, 10000, 100, 100, 100, []), new(2, "home", 2, 2, 0, 20000, 100, 100, 100, [])], [], [])
        {
            Officers = [new("defender", new(11, 1, 1, new(100, 100, 100, 100), false) { Status = OfficerStatus.Ruler }),
                new("attacker", new(21, 2, 2, new(100, 100, 100, 100), false) { Status = OfficerStatus.Ruler, TacticLinkBonus = 10 })],
            Military = new(4, 1, [0, 0, 0, 0], [new(0, 1, null)],
                [new(700, 5, 0, 100000, 1, new(1, 1), TravelMode.Land) { Combat = combat, Attack = new(1, false), CaptureRanks = [BattleRank.Middle] }],
                [new(1, new(2, 2), new(1, 1)) { FacilityTypeId = 8000, Combat = combat, Attack = new(1, false), EnabledTacticIds = [9000] },
                    new(2, new(12, 2), new(1, 1)) { FacilityTypeId = 8000, Combat = combat }])
            {
                LinkRules = new(true, 20, [new(11, 21, 45, false)]),
                CityInfluence = new(1, 10),
                Facilities = [new(8000, 0, 100, new(1, 1), 0, false, true, true, 100000, null, null, null, [])
                    { Combat = combat, Attack = new(1, false), Tactics = new(100, 3, true, [9000]), InnateAttack = new(true, 1),
                        Reprisal = new(6, 1000, 10, 10, 10, 5) }],
                Tactics = [new(9000, 3, TacticEffect.Martial, 1, 9, 0, true, false, [])]
            }
        , Information = new([], [], [], [new San9.Core.Domain.CorpsState(1, 1, 11, new(10000, 100000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 21, new(10000, 100000)) { IsRoot = true }], []) };
        Assert.Throws<InvalidDataException>(() => (definition with { Military = definition.Military with
            { LinkRules = new(true, 20, [new(11, 9000, 45, false)]) } }).Validate());
        var session = GameSession.Start(definition, 42, 100, aiPolicy: San9.Application.AI.AiPolicyConfiguration.Standard with { WorkBudget = 1 });
        session.BeginRecording();
        var beforeQuery = session.Snapshot;
        Assert.True(session.View.HasCityInfluence(1, new(7, 2)));
        Assert.False(session.View.HasCityInfluence(1, new(17, 2)));
        Assert.True(session.View.HasCityInfluence(2, new(17, 2)));
        StateAssert.Equal(beforeQuery, session.Snapshot);
        var query = session.View.Settlements.Single(s => s.Id == 1);
        Assert.Equal(8000, query.FacilityTypeId);
        Assert.Equal(2, query.BaseAttackRange);
        Assert.True(query.UsesRockfallPower);
        Assert.Equal(new FacilityReprisal(6, 1000, 10, 10, 10, 5), query.Reprisal);
        Assert.Equal(9000, query.ConfiguredTacticId);
        Assert.Equal(9000, Assert.Single(query.EnabledTacticIds));
        Assert.Null(session.View.Settlements.Single(s => s.Id == 2).ConfiguredTacticId);
        Assert.True(session.Execute(new("player"), new DeployUnitCommand(Guid.NewGuid(), 2, 0, 2, [21], 10000, 700,
            new(MilitaryOrderKind.AttackSettlement, null, 1))).Submission.Accepted);
        var first = session.AdvanceXun();
        Assert.Contains(first.Events.OfType<TacticActivatedEvent>(), e => e.SourceIsSettlement && e.SourceId == 1);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        Assert.Equal(100, restored.Snapshot.Simulation.ComputerDamageBonusPercent);
        Assert.Equal(10, restored.Snapshot.Officers.Single(o => o.Id == 21).TacticLinkBonus);
        Assert.Equal(session.View.HasCityInfluence(2, new(17, 2)), restored.View.HasCityInfluence(2, new(17, 2)));
        Assert.Equal(9000, restored.View.Settlements.Single(s => s.Id == 1).ConfiguredTacticId);
        Assert.True(restored.View.Settlements.Single(s => s.Id == 1).UsesRockfallPower);
        Assert.Equal(query.Reprisal, restored.View.Settlements.Single(s => s.Id == 1).Reprisal);
        EventAssert.Equal(session.AdvanceXun().Events, restored.AdvanceXun().Events);
        StateAssert.Equal(session.Snapshot, restored.Snapshot);
        var replay = RecordingService.Replay(RecordingService.Encode(session.CaptureRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(100, replay.Session.Snapshot.Simulation.ComputerDamageBonusPercent);
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
        string log = Path.Combine(Path.GetTempPath(), $"san9-bonus-{Guid.NewGuid():N}.jsonl");
        try
        {
            CommandLogService.Write(log, session);
            var header = CommandLogService.ReadHeader(log);
            Assert.Equal(100, header.ComputerDamageBonusPercent);
            var mismatch = GameSession.Start(definition, header.Seed);
            var unchanged = mismatch.Capture();
            Assert.Equal(1, Assert.Throws<CommandLogException>(() => CommandLogService.Replay(log, mismatch)).Line);
            StateAssert.Equal(unchanged, mismatch.Capture());
            var logReplay = GameSession.Start(definition, header.Seed, header.ComputerDamageBonusPercent,
                header.BattleDeathsEnabled, aiPolicy: San9.Application.AI.AiPolicyConfiguration.Standard with { WorkBudget = 1 });
            Assert.Null(CommandLogService.Replay(log, logReplay).FirstFailure);
            StateAssert.Equal(session.Capture(), logReplay.Capture());
            Assert.Equal(session.Snapshot.Units.Single().Soldiers, logReplay.Snapshot.Units.Single().Soldiers);
            var lines = File.ReadAllLines(log);
            var missing = System.Text.Json.Nodes.JsonNode.Parse(lines[0])!;
            missing.AsObject().Remove("computerDamageBonusPercent");
            lines[0] = missing.ToJsonString();
            File.WriteAllLines(log, lines);
            Assert.Equal(1, Assert.Throws<CommandLogException>(() => CommandLogService.ReadHeader(log)).Line);
        }
        finally { File.Delete(log); }
    }
}
