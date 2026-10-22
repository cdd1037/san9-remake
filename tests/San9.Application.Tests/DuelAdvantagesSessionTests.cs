using San9.Tests;
using System.Text;
using System.Text.Json;
using San9.Application.Persistence;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed class DuelAdvantagesSessionTests
{
    [Fact]
    public void DuelOptionsAndAuthoredAdvantagesSurviveSaveAndCommandLog()
    {
        var fixture = G01ScenarioTests.Fixture();
        var officer = fixture.Officers[0];
        var scenario = fixture with { Officers = fixture.Officers.Select(p => p.State.Id == officer.State.Id
            ? p with { State = p.State with { DuelAdvantages = DuelAdvantages.DominantParry | DuelAdvantages.DominantDoubleStrike } }
            : p).ToArray() };
        Assert.False(GameSession.Start(scenario, 190).Snapshot.Simulation.BattleDeathsEnabled);
        var session = GameSession.Start(scenario, 190, battleDeathsEnabled: true);
        List<SessionRequest> requests = [];
        var inputHeader = CommandLogService.CreateHeader(session);
        SessionResult ApplyInput(SessionRequest request)
        {
            var result = session.Apply(request);
            if (result.Disposition == SessionDisposition.Accepted)
                requests.Add(request);
            return result;
        }
        SessionResult AdvanceInput() => ApplyInput(session.Request(new AdvanceOperation()));
        var restored = SaveService.Decode(SaveService.Encode(session), scenario, GameSession.EngineFactory(scenario));
        Assert.True(restored.Snapshot.Simulation.BattleDeathsEnabled);
        Assert.Equal(DuelAdvantages.DominantParry | DuelAdvantages.DominantDoubleStrike,
            restored.Snapshot.Officers.Single(p => p.Id == officer.State.Id).DuelAdvantages);
AdvanceInput();
        var log = Path.Combine(Path.GetTempPath(), $"san9-duel-{Guid.NewGuid():N}.jsonl");
        try
        {
            CommandLogService.Write(log, inputHeader, requests);
            Assert.True(CommandLogService.ReadHeader(log).BattleDeathsEnabled);
            Assert.Throws<CommandLogException>(() => CommandLogService.Replay(log, GameSession.Start(scenario, 190)));
            var compatible = GameSession.Start(scenario, 190, battleDeathsEnabled: true);
            Assert.Null(CommandLogService.Replay(log, compatible).FirstFailure);
            StateAssert.Equal(session.Capture(), compatible.Capture());
        }
        finally { File.Delete(log); }
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
}
