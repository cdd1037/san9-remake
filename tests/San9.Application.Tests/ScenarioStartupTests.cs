using San9.Tests;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed class ScenarioStartupTests
{
    [Fact]
    public void DormantForceKeepsBaseTroopsButItsOfficersResolveBirth()
    {
        var baseline = G01ScenarioTests.Fixture();
        var definition = baseline with
        {
            Forces = baseline.Forces.Select(f => f.Id == 2 ? f with { IsDormant = true, IsBarbarian = true } : f).ToArray(),
            Settlements = baseline.Settlements.Select(s => s.OwnerId == 2 ? s with { IsDormant = true } : s).ToArray(),
            Officers = baseline.Officers.Select(o => o.State.OwnerId == 2 ? o with
                { AgeAtScenarioStart = 19, State = o.State with { IsDormant = true } } : o).ToArray()
        };
        var session = GameSession.Start(definition, 123);
        Assert.Equal(100u, session.Snapshot.Settlements.Single(s => s.Id == 2).Soldiers);
        Assert.All(session.Snapshot.Officers.Where(o => o.OwnerId == 2), o => Assert.Equal(171, o.BirthYear));
    }

    [Theory]
    [InlineData(100000u, 500000u, 300000u)]
    [InlineData(490000u, 500000u, 500000u)]
    [InlineData(500000u, 500000u, 500000u)]
    public void ComputerBonusPreservesPopulationAndIsNotRepeatedOnRestore(uint soldiers, uint capacity, uint expected)
    {
        var baseline = G01ScenarioTests.Fixture();
        var definition = baseline with
        {
            Forces = baseline.Forces.Select(f => f with { IsBarbarian = true }).ToArray(),
            Settlements = baseline.Settlements.Select(s => s with
                { Soldiers = soldiers, SoldierCapacity = capacity, DraftPopulation = 12345 }).ToArray()
        };
        var session = GameSession.Start(definition, 123);
        Assert.Equal(soldiers, session.Snapshot.Settlements.Single(s => s.Id == 1).Soldiers);
        Assert.Equal(expected, session.Snapshot.Settlements.Single(s => s.Id == 2).Soldiers);
        Assert.All(session.Snapshot.Settlements, s => Assert.Equal(12345u, s.DraftPopulation));
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        StateAssert.Equal(session.Capture(), restored.Capture());
        Assert.All(definition.Settlements, s => Assert.Equal(soldiers, s.Soldiers));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BonusUsesRulerCityOrFirstOwnedCity(bool rulerInCity)
    {
        var baseline = G01ScenarioTests.Fixture();
        var definition = baseline with
        {
            Forces = baseline.Forces.Select(f => f with { IsBarbarian = f.Id == 2 }).ToArray(),
            Settlements = [.. baseline.Settlements, baseline.Settlements[1] with
                { Id = 3, X = 3, Kind = rulerInCity ? SettlementKind.City : SettlementKind.Facility }],
            Officers = baseline.Officers.Select(o => o.State.Id == 4 ? o with
                { State = o.State with { SettlementId = 3 } } : o).ToArray()
        };
        var session = GameSession.Start(definition, 123);
        Assert.Equal(200100u, session.Snapshot.Settlements.Single(s => s.Id == (rulerInCity ? 3 : 2)).Soldiers);
        Assert.Equal(100u, session.Snapshot.Settlements.Single(s => s.Id == (rulerInCity ? 2 : 3)).Soldiers);
    }

    [Theory]
    [InlineData(190, 34, 156)]
    [InlineData(200, 19, 181)]
    public void RelativeBirthIgnoresForceIdentityAndResolvesOnlyForNewWorld(int year, int age, int expected)
    {
        var baseline = G01ScenarioTests.Fixture();
        var definition = baseline with
        {
            Year = year,
            Forces = baseline.Forces.Select(f => f with { IsBarbarian = true }).ToArray(),
            Officers = baseline.Officers.Select(o => o.State.Id is 1 or 4
                ? o with { AgeAtScenarioStart = age }
                : o with { State = o.State with { BirthYear = 150 } }).ToArray()
        };
        var session = GameSession.Start(definition, 123);
        Assert.All(session.Snapshot.Officers.Where(o => o.Id is 1 or 4), o => Assert.Equal(expected, o.BirthYear));
        Assert.All(session.Snapshot.Officers.Where(o => o.Id is not (1 or 4)), o => Assert.Equal(150, o.BirthYear));
        session.AdvanceXun();
        StateAssert.Equal(session.Capture(), SaveService.Decode(SaveService.Encode(session), definition).Capture());
    }

    [Theory]
    [InlineData(34, 150)]
    [InlineData(-1, 0)]
    [InlineData(190, 0)]
    public void InvalidOrConflictingBirthDefinitionsAreRejected(int age, int year)
    {
        var baseline = G01ScenarioTests.Fixture();
        var definition = baseline with { Officers = baseline.Officers.Select(o => o with
            { AgeAtScenarioStart = age, State = o.State with { BirthYear = year } }).ToArray() };
        Assert.Throws<InvalidDataException>(definition.Validate);
    }

    [Fact]
    public void ChangedInitialConfigurationPreservesSavedAgeAndSimulationSettings()
    {
        var baseline = G01ScenarioTests.Fixture();
        var definition = baseline with { Officers = baseline.Officers.Select(o => o with { AgeAtScenarioStart = 19 }).ToArray() };
        var original = GameSession.Start(definition, 123);
        var saved = SaveService.Encode(original);
        var changed = definition with { Officers = definition.Officers.Select(o => o with { AgeAtScenarioStart = 34 }).ToArray(),
            AnnualAppearancesEnabled = false, NaturalDeathsEnabled = false, FreeOfficerMigrationEnabled = false };
        var restored = SaveService.Decode(saved, changed);
        StateAssert.Equal(original.Capture(), restored.Capture());
        Assert.True(restored.Snapshot.Simulation.AnnualAppearancesEnabled);
        Assert.True(restored.Snapshot.Simulation.NaturalDeathsEnabled);
        Assert.True(restored.Snapshot.Simulation.FreeOfficerMigrationEnabled);
    }
    [Fact]
    public void MultiplePlayerForcesAreNormalizedBeforeBonusesAndRestoreRetainsConfirmedSettings()
    {
        var definition = G01ScenarioTests.Fixture();
        definition = definition with { Forces = definition.Forces.Select(f => f with { IsBarbarian = true }).ToArray() };
        var settings = new NewGameSettings { Difficulty = GameDifficulty.Advanced, Mode = GameMode.Fictional,
            MoneyBonus = 100, SoldierBonus = 10000, DomesticBonus = 100, MoraleBonus = 20 };
        definition = definition with { FictionalOfficerPlaceIds = [1, 2] };
        var session = GameSession.Start(definition, 123, settings: settings, playerForceIds: [2, 1]);
        Assert.All(session.Snapshot.Forces, f => Assert.False(f.IsBarbarian));
        Assert.Equal(new[] { 1, 2 }, session.Snapshot.Simulation.Controls.Where(c => c.Kind == San9.Core.Simulation.ControllerKind.Player).Select(c => c.ForceId));
        Assert.All(session.Snapshot.Settlements, s => Assert.Equal(definition.Settlements.Single(p => p.Id == s.Id).Soldiers, s.Soldiers));
        Assert.Equal(settings, session.Snapshot.Simulation.NewGame);
        var changedDefaults = definition with { OriginalAi = definition.OriginalAi with { Advanced = false } };
        var restored = SaveService.Decode(SaveService.Encode(session), changedDefaults);
        StateAssert.Equal(session.Capture(), restored.Capture());
        Assert.Equal(new[] { 1, 2 }, restored.InitialPlayerForceIds);
        Assert.Equal(1, restored.Messages(new(San9.Content.Text.TextCatalog.CurrentSchemaVersion, [])).Query("game.is-fictional", []).Number);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitCommandScriptsRejectDifferentNewGameConfigurationBeforeApplyingAnyOperation(bool changePlayers)
    {
        var definition = G01ScenarioTests.Fixture();
        var original = GameSession.Start(definition, 123, settings: new() { Difficulty = GameDifficulty.Beginner }, playerForceIds: [1, 2]);
        string path = Path.Combine(Path.GetTempPath(), $"san9-new-game-{Guid.NewGuid():N}.jsonl");
        try
        {
            var header = CommandLogService.CreateHeader(original);
            CommandLogService.Write(path, header, [original.Request(new ChronicleOperation("new game"))]);
            var same = GameSession.Start(definition, header.Seed, settings: header.NewGame, playerForceIds: header.PlayerForceIds);
            Assert.Equal(1, CommandLogService.Replay(path, same).Accepted);
            var different = GameSession.Start(definition, header.Seed,
                settings: changePlayers ? header.NewGame : header.NewGame with { MoraleBonus = 1 },
                playerForceIds: changePlayers ? [1] : header.PlayerForceIds);
            Assert.Throws<CommandLogException>(() => CommandLogService.Replay(path, different));
            Assert.Empty(different.Chronicle);
        }
        finally { File.Delete(path); }
    }
}
