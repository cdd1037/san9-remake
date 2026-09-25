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
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(restored));
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
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(SaveService.Decode(SaveService.Encode(session), definition)));
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
    public void ChangingStartingAgeInvalidatesSavedContentFingerprint()
    {
        var baseline = G01ScenarioTests.Fixture();
        var definition = baseline with { Officers = baseline.Officers.Select(o => o with { AgeAtScenarioStart = 19 }).ToArray() };
        var saved = SaveService.Encode(GameSession.Start(definition, 123));
        var changed = definition with { Officers = definition.Officers.Select(o => o with { AgeAtScenarioStart = 34 }).ToArray() };
        Assert.Equal(SaveFailure.ContentMismatch, Assert.Throws<SaveException>(() => SaveService.Decode(saved, changed)).Failure);
    }
}
