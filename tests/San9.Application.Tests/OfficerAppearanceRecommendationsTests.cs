using San9.Application.Presentation;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed class OfficerAppearanceRecommendationsTests
{
    [Fact]
    public void ConflictingLocationsAtEqualDistanceRequireManualChoice()
    {
        var current = G01ScenarioTests.Fixture();
        var source = current with { AppearanceEvidence = [new(2, 1, 1, "first", 189), new(2, 1, 2, "second", 191)] };
        var result = Assert.Single(OfficerAppearanceRecommendations.Create(current, GameSession.Start(current, 42).Snapshot, [source], [2]));
        Assert.Contains("地点冲突", result.Problem);
        Assert.Null(result.SettlementId);
    }

    [Fact]
    public void FallbackPrefersCitiesButAcceptsAnEvidencedFacility()
    {
        var current = G01ScenarioTests.Fixture();
        var world = GameSession.Start(current, 42).Snapshot;
        world = world with { Settlements = world.Settlements.Select(s => s.Id == 1
            ? s with { Kind = SettlementKind.Facility } : s with { OwnerId = 1, Kind = SettlementKind.City }).ToArray() };
        var source = current with { AppearanceEvidence = [new(2, 1, null, "source", 190)] };
        Assert.Equal(2, Assert.Single(OfficerAppearanceRecommendations.Create(current, world, [source], [2])).SettlementId);
        source = source with { AppearanceEvidence = [new(2, 1, 1, "source", 190)] };
        Assert.Equal(1, Assert.Single(OfficerAppearanceRecommendations.Create(current, world, [source], [2])).SettlementId);
    }

    [Fact]
    public void UsesNearestScenarioAndExplicitLeaderIdentity()
    {
        var current = G01ScenarioTests.Fixture();
        var source = current with { ScenarioId = "source", Year = 195,
            Forces = current.Forces.Select(f => f.Id == 1 ? f with { Id = 99 } : f).ToArray(),
            Officers = current.Officers.Select(o => o.State.Id == 2 ? o with { State = o.State with { OwnerId = 99 } } : o).ToArray() };
        var world = GameSession.Start(current, 42).Snapshot;
        var suggestion = Assert.Single(OfficerAppearanceRecommendations.Create(current, world, [source], [2]));
        Assert.Equal(1, suggestion.ForceId);
        Assert.Equal(1, suggestion.SettlementId);
        Assert.Equal("source", suggestion.SourceScenarioId);
        Assert.Null(suggestion.Problem);
    }

    [Fact]
    public void AuthoredEvidencePreservesSourceAndOverridesDerivedScenarioState()
    {
        var current = G01ScenarioTests.Fixture();
        var source = current with { ScenarioId = "source", Year = 191,
            AppearanceEvidence = [new(2, 4, 2, "documented", 190)] };
        var result = Assert.Single(OfficerAppearanceRecommendations.Create(current, GameSession.Start(current, 42).Snapshot, [source], [2]));
        Assert.Equal(2, result.ForceId);
        Assert.Equal(2, result.SettlementId);
        Assert.Equal("documented", result.SourceScenarioId);
        Assert.Equal(190, result.SourceYear);
    }

    [Fact]
    public void UnmatchedNearestEvidenceDoesNotHideAnAvailableForce()
    {
        var current = G01ScenarioTests.Fixture();
        var source = current with { AppearanceEvidence = [new(2, 999, null, "unknown", 190), new(2, 4, 2, "available", 195)] };
        var result = Assert.Single(OfficerAppearanceRecommendations.Create(current, GameSession.Start(current, 42).Snapshot, [source], [2]));
        Assert.Equal(2, result.ForceId);
        Assert.Equal("available", result.SourceScenarioId);
    }

    [Fact]
    public void SameDistanceConflictingForcesRequireManualChoice()
    {
        var current = G01ScenarioTests.Fixture();
        var left = current with { ScenarioId = "left", Year = 189 };
        var right = current with { ScenarioId = "right", Year = 191,
            Officers = current.Officers.Select(o => o.State.Id == 2 ? o with { State = o.State with { OwnerId = 2 } } : o).ToArray() };
        var recommendation = Assert.Single(OfficerAppearanceRecommendations.Create(current, GameSession.Start(current, 42).Snapshot, [left, right], [2]));
        Assert.Null(recommendation.ForceId);
        Assert.Contains("冲突", recommendation.Problem);
    }
}
