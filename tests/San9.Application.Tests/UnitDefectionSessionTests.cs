using San9.Tests;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class UnitDefectionSessionTests
{
    [Fact]
    public void SiegeAtXunBoundaryRoundTripsSave()
    {
        var basis = FieldRecruitmentSessionTests.Definition(true);
        var content = basis with
        {
            Information = basis.Information with { Relations = [new(1, 2, 0)] },
            Military = basis.Military! with { Formations = basis.Military.Formations.Select(f => f with
            { CaptureRanks = [BattleRank.Middle, BattleRank.Middle, BattleRank.Middle, BattleRank.Middle, BattleRank.Middle],
                Attack = new(10, true), Combat = new(1, 1000, 1, 1000, 50) }).ToImmutableArray() },
            Units = [basis.Units[0] with { Condition = UnitCondition.Confused, ConditionDaysRemaining = 60, MemberRanks = new Dictionary<int, BattleRank> { [20] = BattleRank.Front, [21] = BattleRank.Middle, [22] = BattleRank.Rear }.ToImmutableDictionary() }]
        };
        var session = GameSession.Start(content, 17);
        for (int i = 0; i < 2; i++) Assert.Empty(session.AdvanceXun().Events.OfType<UnitDefectedEvent>());
        var capture = session.Capture();
        session = GameSession.Restore(content, capture with { World = capture.World with
        {
            Simulation = capture.World.Simulation with { NextUnitId = 103 },
            Officers = capture.World.Officers.Select(p => p.Id is 1 or 11 ? p with
                { SettlementId = null, UnitId = p.Id == 11 ? 101 : 102, IsCommander = false } : p).ToImmutableArray(),
            Units = [capture.World.Units.Single(u => u.Id == 100),
                .. new[] { 101, 102 }.Select(id => new UnitState(id, 1, new(id == 101 ? 327 : 337, 2), 5000)
                { FormationId = 1, CorpsId = 1, ServicePlaceId = 1, CommanderId = id == 101 ? 11 : 1, Morale = 100,
                    MemberRanks = new Dictionary<int, BattleRank> { [id == 101 ? 11 : 1] = BattleRank.Middle }.ToImmutableDictionary(),
                    Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 100 } })]
        } });
        var restored = SaveService.Decode(SaveService.Encode(session), content);
        var result = session.AdvanceXun();
        Assert.Equal(new GameClock(190, 2, 1), Assert.Single(result.Events.OfType<UnitDefectedEvent>()).Clock);
        EventAssert.Equal(result.Events, restored.AdvanceXun().Events);
        Assert.Equal(1, session.Snapshot.Units.Single(u => u.Id == 100).OwnerId);
        restored = SaveService.Decode(SaveService.Encode(session), content);
        Assert.Empty(restored.InitialEvents);
        StateAssert.Equal(session.Capture(), restored.Capture());
    }

    [Fact]
    public void UnitDefectionSwitchIsRequiredAndRetainsSavedConfiguration()
    {
        var content = FieldRecruitmentSessionTests.Definition(true);
        var disabled = content with { UnitDefectionsEnabled = false };

        var session = GameSession.Start(disabled, 17);
        var snapshot = session.Capture();
        Assert.False(GameSession.Restore(content, snapshot).Snapshot.Simulation.UnitDefectionsEnabled);
        var state = JsonNode.Parse(CanonicalJson.Text(snapshot.World.Simulation))!.AsObject();
        Assert.True(state.Remove("unitDefectionsEnabled"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SimulationState>(state.ToJsonString(), CanonicalJson.Options));
        var definition = JsonNode.Parse(CanonicalJson.Text(content))!.AsObject();
        Assert.True(definition.Remove("unitDefectionsEnabled"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ScenarioInitialState>(definition.ToJsonString(), CanonicalJson.Options));
    }
}
