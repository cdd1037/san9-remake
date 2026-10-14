using San9.Tests;
using System.Text;
using System.Text.Json;
using San9.Application.Persistence;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed class DuelAdvantagesSessionTests
{
    [Fact]
    public void BattleDeathOptionDefaultsOffAndSurvivesSaveRecordingAndCommandLog()
    {
        var scenario = G01ScenarioTests.Fixture();
        Assert.False(GameSession.Start(scenario, 190).Snapshot.Simulation.BattleDeathsEnabled);
        var session = GameSession.Start(scenario, 190, battleDeathsEnabled: true);
        var restored = SaveService.Decode(SaveService.Encode(session), scenario, GameSession.EngineFactory(scenario));
        Assert.True(restored.Snapshot.Simulation.BattleDeathsEnabled);
        session.BeginRecording();
        session.AdvanceXun();
        var log = Path.Combine(Path.GetTempPath(), $"san9-duel-{Guid.NewGuid():N}.jsonl");
        try
        {
            CommandLogService.Write(log, session);
            Assert.True(CommandLogService.ReadHeader(log).BattleDeathsEnabled);
            Assert.Throws<CommandLogException>(() => CommandLogService.Replay(log, GameSession.Start(scenario, 190)));
            var compatible = GameSession.Start(scenario, 190, battleDeathsEnabled: true);
            Assert.Null(CommandLogService.Replay(log, compatible).FirstFailure);
            StateAssert.Equal(session.Capture(), compatible.Capture());
        }
        finally { File.Delete(log); }
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), scenario, GameSession.EngineFactory(scenario));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    public void StrictPersistenceAcceptsDeclaredFlagCombinations(int flags)
    {
        var value = (DuelAdvantages)flags;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, StrictJson.Options);
        Assert.Equal(value, StrictJson.Deserialize<DuelAdvantages>(bytes));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(8)]
    [InlineData(9)]
    public void StrictPersistenceRejectsUnknownBitsOnReadAndWrite(int flags)
    {
        Assert.Throws<JsonException>(() => StrictJson.Deserialize<DuelAdvantages>(Encoding.UTF8.GetBytes(flags.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        Assert.Throws<JsonException>(() => JsonSerializer.SerializeToUtf8Bytes((DuelAdvantages)flags, StrictJson.Options));
    }

    [Fact]
    public void AuthoredDuelAdvantagesSurviveWorldSaveAndRecording()
    {
        var fixture = G01ScenarioTests.Fixture();
        var officer = fixture.Officers[0];
        var scenario = fixture with { Officers = fixture.Officers.Select(p => p.State.Id == officer.State.Id
            ? p with { State = p.State with { DuelAdvantages = DuelAdvantages.DominantParry | DuelAdvantages.DominantDoubleStrike } }
            : p).ToArray() };
        var session = GameSession.Start(scenario, 190);

        var restored = SaveService.Decode(SaveService.Encode(session), scenario, GameSession.EngineFactory(scenario));
        Assert.Equal(DuelAdvantages.DominantParry | DuelAdvantages.DominantDoubleStrike,
            restored.Snapshot.Officers.Single(p => p.Id == officer.State.Id).DuelAdvantages);
        session.BeginRecording();
        session.AdvanceXun();
        restored.AdvanceXun();
        StateAssert.Equal(session.Capture(), restored.Capture());
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), scenario, GameSession.EngineFactory(scenario));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }
}
