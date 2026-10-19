using San9.Tests;
using San9.Core.Domain;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Scenarios;

namespace San9.Application.Tests;

public sealed class MapMusicTests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 1)]
    [InlineData(7, 2)]
    [InlineData(10, 3)]
    [InlineData(12, 3)]
    public void RestoredWorldDateAndAuthoredRankReferencesSelectTheMapTrack(int month, int season)
    {
        var audio = new MapAudioDefinition("cue", ["spring", "summer", "autumn", "winter"],
            ["rank-spring", "rank-summer", "rank-autumn", "rank-winter"], [99], [], [], ["battle"]);
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "music", 190, month, 1, 5,
            [new(5, "force", 5, 100)], [new(15, "city", 5, 0, 0, 0, 100, 100, 0, [])], [], [])
        { Information = new([new(99, "rank", 100, 0, 0, 10)], [], [],
            [new San9.Core.Domain.CorpsState(5, 5, null, new(100, 100)) { IsRoot = true }], []) };
        var session = GameSession.Start(definition, 1);
        var restored = GameSession.Restore(definition, session.Capture());
        var hash = restored.Snapshot;
        Assert.Equal(audio.SeasonalMusic[season], MapMusic.Select(restored.View, audio, execution: false));
        var ranked = definition with { Forces = [definition.Forces[0] with { RankId = 99 }] };
        var rankedSession = GameSession.Start(ranked, 1);
        Assert.Equal(audio.RankedSeasonalMusic[season], MapMusic.Select(rankedSession.View, audio, execution: false));
        StateAssert.Equal(hash, restored.Snapshot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExecutionTracksFollowPlayerActivityAndReturnToSeasonalMusic(bool ranked)
    {
        var audio = new MapAudioDefinition("cue", ["spring", "summer", "autumn", "winter"],
            ["rank-spring", "rank-summer", "rank-autumn", "rank-winter"], [99], [], [], [])
        { ExecutionMusic = new("battle", "rank-battle", "march", "rank-march", "domestic") };
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "music", 190, 1, 1, 5,
            [new(5, "force", 5, 100) { RankId = ranked ? 99 : null }],
            [new(15, "city", 5, 0, 0, 0, 100, 100, 0, [])], [], [])
        { Information = new([new(99, "rank", 100, 0, 0, 10)], [], [],
            [new CorpsState(5, 5, null, new(100, 100)) { IsRoot = true }], []) };
        var world = GameSession.Start(definition, 1).Snapshot;
        string Select(params UnitState[] units) => MapMusic.Select(
            new StrategySnapshot(definition, world with { Units = [.. units] }), audio, execution: true);
        var marching = new UnitState(1000, 5, new(0, 0), 100) { OrderClass = UnitOrderClass.Military };
        var fighting = marching with { Id = 1001, ActiveEngagement = true };
        Assert.Equal("domestic", Select());
        Assert.Equal(ranked ? "rank-march" : "march", Select(marching));
        // Selection is global, independent of camera, movement and iteration order.
        Assert.Equal(ranked ? "rank-battle" : "battle", Select(marching, fighting));
        Assert.Equal(ranked ? "rank-battle" : "battle", Select(fighting, marching));
        Assert.Equal(ranked ? "rank-battle" : "battle", Select(fighting with { ActiveEngagement = false, PassiveEngagement = true }));
        Assert.Equal("domestic", Select(fighting with { OwnerId = 77 }));
        Assert.Equal("domestic", Select(marching with { OrderClass = UnitOrderClass.NonMilitary }));
        Assert.Equal("domestic", Select(marching with { IsEmbedded = true }));
        Assert.Equal("domestic", Select(fighting with { IsEmbedded = true }));
        Assert.Equal(ranked ? "rank-battle" : "battle", Select(fighting with
            { IsEmbedded = true, ActiveEngagement = false, PassiveEngagement = true }));
        Assert.Equal("domestic", Select(marching with { IsPersonnel = true }));
        Assert.Equal(ranked ? "rank-march" : "march", Select(marching));
        Assert.Equal("domestic", Select());
        Assert.Equal(ranked ? "rank-spring" : "spring", MapMusic.Select(
            new StrategySnapshot(definition, world with { Units = [fighting] }), audio, execution: false));
    }

    [Fact]
    public void AnEnemyBesiegingThePlayersGarrisonSelectsBattleWithoutPlayerFieldCombat()
    {
        var basis = MissionScenario.Create();
        var combat = new CombatProfile(10, 10, 10, 10, 50);
        var definition = basis with { Military = basis.Military! with
        {
            Formations = [basis.Military.Formations[0] with { Combat = combat, Attack = new(5, false), CaptureRanks = [.. Enumerable.Repeat(BattleRank.Middle, basis.Military.Formations[0].OfficerCapacity)] }],
            Places = [.. basis.Military.Places.Select(p => p with { Combat = combat })]
        } };
        var session = GameSession.Start(basis, 1);
        var world = session.Snapshot with { Units = [.. session.Snapshot.Units.Select(u => u.Id == 20
            ? u with { Position = new(7, 7), Order = new(MilitaryOrderKind.AttackSettlement, null, 1) }
            : u with { Position = new(102, 7), Order = null })] };
        var view = new StrategySnapshot(definition, world);
        var audio = new MapAudioDefinition("cue", ["spring", "summer", "autumn", "winter"],
            ["spring", "summer", "autumn", "winter"], [], [], [], [])
        { ExecutionMusic = new("battle", "rank-battle", "march", "rank-march", "domestic") };
        Assert.True(view.IsSettlementEngaged(1));
        Assert.False(view.IsUnitEngaged(10));
        Assert.Equal("battle", MapMusic.Select(view, audio, execution: true));
        var distant = new StrategySnapshot(definition, world with { Units = [.. world.Units.Select(u =>
            u.Id == 20 ? u with { Position = new(42, 7) } : u)] });
        Assert.Equal("domestic", MapMusic.Select(distant, audio, execution: true));
    }
}
