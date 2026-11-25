using San9.Tests;
using System.Collections.Immutable;
using San9.Application.Authoring;
using San9.Content.Authoring;
using San9.Application.Persistence;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class PenglaiSessionTests
{
    private static ScenarioInitialState Definition()
    {
        var places = Enumerable.Range(1, 6).Select(id => new PersonPlace(id, MapCoordinates.Center(new(id - 1, 0)))).ToArray();
        return new(ScenarioInitialState.CurrentSchemaVersion, "penglai-public", 190, 2, 1, 1,
            [new(1, "force.1", 1, 800) { BaseTrust = 800, LeaderId = 1 },
             new(2, "force.2", 2, 100) { BaseTrust = 100, LeaderId = 2, IsDormant = true, IsBarbarian = true },
             new(3, "force.3", 3, 100) { BaseTrust = 100, LeaderId = 10 }],
            places.Where(p => p.Id != 5).Select(p => new SettlementInitialState(p.Id, $"place.{p.Id}",
                p.Id switch { 3 => 2, 4 => null, 6 => 3, _ => 1 }, p.Id - 1, 0,
                p.Id == 3 ? 100000u : 0u, 100, 100, p.Id == 3 ? 60 : 0, [])
                { IsDormant = p.Id is 3 or 4, Kind = p.Id == 4 ? SettlementKind.Facility : SettlementKind.City,
                  ConsumesFood = false, CorpsId = p.Id == 3 ? 2 : null }).ToArray(),
            places.ToDictionary(p => p.Id, p => $"place.{p.Id}"), new() { [1] = 1, [2] = 2, [3] = 3, [4] = 3, [5] = 1, [6] = 6 })
        {
            CityProvinceIds = new() { [1] = 1, [2] = 2, [6] = 3 }, UnbuiltPlaces = [places[4]],
            Officers = new[] { new OfficerInitialState("person.1", new(1, 1, 1, new(50, 50, 50, 50), false)),
                new OfficerInitialState("person.10", new(10, 3, 6, new(50, 50, 50, 50), false)) }
                .Concat(Enumerable.Range(2, 5).Select(id => new OfficerInitialState($"person.{id}",
                    new(id, 2, 3, new(70, 70, 70, 70), false) { IsDormant = true, Status = id == 2 ? OfficerStatus.Ruler : OfficerStatus.Serving }))).ToArray(),
            SearchEvents = [new ForceActivationSearchEventDefinition(5, 2, 3, [4], [2, 3, 4, 5, 6], 700, 2, 500000, 10, new("penglai"))
                { Id = "penglai", Priority = -3, Consumption = EventConsumption.OnSuccess }],
            Information = InformationDefinitions.Empty with { Corps = [new San9.Core.Domain.CorpsState(1, 1, 1, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(3, 3, 10, new(10000, 10000)) { IsRoot = true }, new(2, 2, 2, new(10000, 300000)) { IsRoot = true }] },
            Military = new(6, 1, Enumerable.Repeat(0, 6).ToImmutableArray(), [new(0, 1, 1)], [],
                places.Where(p => p.Id != 5).Select(p => new BattlefieldPlace(p.Id, p.Position, new(1, 1))).ToImmutableArray())
                { RegionIds = Enumerable.Range(1, 6).Select(i => (int?)i).ToImmutableArray() },
            ForceConsolidationEnabled = false, ForceEmergenceEnabled = false, CityIndependenceEnabled = false,
            AnnualAppearancesEnabled = false, NaturalDeathsEnabled = false, OfficeRequestsEnabled = false, FreeOfficerMigrationEnabled = false
        };
    }

    [Fact]
    public void ExplorationActivatesQueriesAndSurvivesSave()
    {
        var d = Definition(); var session = GameSession.Start(d, 17);         var texts = new TextCatalog(San9.Content.Text.TextCatalog.CurrentSchemaVersion, d.Forces.Select(f => f.NameKey).Concat(d.Officers.Select(p => p.NameKey)).Concat(d.RegionNameKeys.Values).Distinct().ToDictionary(k => k, k => k));
        var query = new InformationQuery(session, texts);
        Assert.DoesNotContain(query.Read(InformationCategory.Corps).Rows, r => r.Key.Id == 2);
        Assert.DoesNotContain(session.View.Settlements, s => s.Id is 3 or 4);
        Assert.False(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, [1], 3)).Submission.Accepted);
        Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, [1], 5)).Submission.Accepted);
        var before = SaveService.Decode(SaveService.Encode(session), d);
        var result = session.AdvanceXun(); var repeated = before.AdvanceXun();
        EventAssert.Equal(result.Events, repeated.Events);
        Assert.Single(result.Events.OfType<ForceActivatedBySearchEvent>());
        Assert.DoesNotContain(result.Events.OfType<SearchResolvedEvent>(), e => e.OfficerId == 1);
        Assert.Null(session.Snapshot.Simulation.PendingDailyTask);
        Assert.Contains(session.View.Settlements, s => s.Id == 3 && s.Soldiers == 500000);
        Assert.Contains(session.View.Settlements, s => s.Id == 4 && s.OwnerId == 2 && s.Soldiers == 0);
        Assert.Equal(5, session.View.Officers.Count(p => p.OwnerId == 2));
        Assert.Contains(query.Read(InformationCategory.Corps).Rows, r => r.Key.Id == 2);
        Assert.Equal(810, session.Snapshot.Forces.Single(f => f.Id == 1).BaseTrust);
        var restored = SaveService.Decode(SaveService.Encode(session), d);
        StateAssert.Equal(session.Capture(), restored.Capture());
        var activation = (ForceActivationSearchEventDefinition)d.SearchEvents.Single();
        byte[] bytes = SaveService.Encode(session);
        foreach (var revised in new[] { d with { SearchEvents = [] },
            d with { SearchEvents = [activation with { ConsumptionKey = new("penglai.revised") }] } })
        {
            StateAssert.Equal(session.Capture(), SaveService.Decode(bytes, revised).Capture());
            var loaded = SaveService.Load(bytes, revised);
            Assert.False(loaded.Snapshot.Forces.Single(f => f.Id == 2).IsDormant);
            Assert.Contains(activation.ConsumptionKey, loaded.Snapshot.Simulation.ConsumedEventKeys);
            Assert.Contains(loaded.Snapshot.Simulation.Controls, c => c.ForceId == 2);
        }
        Assert.DoesNotContain(session.AdvanceXun().Events, e => e is ForceActivatedBySearchEvent);
        Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, [1], 5)).Submission.Accepted);
        Assert.DoesNotContain(session.AdvanceXun().Events, e => e is ForceActivatedBySearchEvent);
    }

    [Fact]
    public void DormantGroupRemainsUnchangedAcrossEconomicAndPersonnelCycles()
    {
        var original = Definition();
        var d = original with { Month = 12, AnnualAppearancesEnabled = true, NaturalDeathsEnabled = true,
            Officers = original.Officers.Select(p => p.State.IsDormant
                ? p with { State = p.State with { BirthYear = 100, LifespanTier = 1, Injury = 3 } } : p).ToArray() };
        var session = GameSession.Start(d, 17);
        var before = session.Snapshot;
        for (int i = 0; i < 12; i++) session.AdvanceXun();
        Assert.Equal(CanonicalJson.Text(before.Forces.Where(f => f.IsDormant)), CanonicalJson.Text(session.Snapshot.Forces.Where(f => f.IsDormant)));
        Assert.Equal(CanonicalJson.Text(before.Settlements.Where(s => s.IsDormant)), CanonicalJson.Text(session.Snapshot.Settlements.Where(s => s.IsDormant)));
        Assert.Equal(CanonicalJson.Text(before.Officers.Where(p => p.IsDormant)), CanonicalJson.Text(session.Snapshot.Officers.Where(p => p.IsDormant)));
        StateAssert.Equal(session.Capture(), SaveService.Decode(SaveService.Encode(session), d).Capture());
    }

    [Fact]
    public void SelectingReservedForceStartsActiveWithoutExplorationTroopsOrReward()
    {
        var d = Definition() with { PlayerForceId = 2 };
        d.Validate();
        var session = GameSession.Start(d, 17);
        Assert.False(session.Snapshot.Forces.Single(f => f.Id == 2).IsDormant);
        Assert.Equal(100000u, session.Snapshot.Settlements.Single(s => s.Id == 3).Soldiers);
        Assert.Contains(new PersistentEventKey("penglai"), session.Snapshot.Simulation.ConsumedEventKeys);
        StateAssert.Equal(session.Capture(), SaveService.Decode(SaveService.Encode(session), d).Capture());
        // Today's opening selection no longer offers this force; the saved selection is history.
        var revised = d with { PlayerForceId = 1, SearchEvents = [] };
        StateAssert.Equal(session.Capture(), SaveService.Decode(SaveService.Encode(session), revised).Capture());
        Assert.Equal(new[] { 2 }, SaveService.Load(SaveService.Encode(session), revised).InitialPlayerForceIds);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReservedCityDeploymentFollowsPlayerSelectionThroughDraftAndSession(bool joinForce)
    {
        var source = Definition();
        var person = OfficerDefaults.Create(10000);
        source = source with { OfficerCatalog = new[] { person }.ToDictionary(p => p.Id) };
        var draft = new NewGameDraft(source, new(TextCatalog.CurrentSchemaVersion, new()), new Dictionary<string, string>());
        Assert.True(draft.Compose().Settlements.Single(s => s.Id == 3).IsDormant);
        Assert.Throws<InvalidDataException>(() => draft.Deploy([person.Id], 3, joinForce));

        draft.SetPlayerForces([1, 2]);
        Assert.False(draft.Compose().Settlements.Single(s => s.Id == 3).IsDormant);
        Assert.False(draft.Compose().Settlements.Single(s => s.Id == 4).IsDormant);
        draft.Deploy([person.Id], 3, joinForce);
        int id = person.Id;
        var preview = draft.Preview();
        Assert.Contains(preview.Settlements, s => s.Id == 3);
        Assert.Equal(joinForce ? (int?)2 : null, preview.World.Officers.Single(p => p.Id == id).OwnerId);
        var session = GameSession.Start(source, 17, playerForceIds: draft.PlayerForceIds, setup: draft.Setup);
        var deployed = session.Snapshot.Officers.Single(p => p.Id == id);
        Assert.Equal(3, deployed.SettlementId);
        Assert.Equal(joinForce ? OfficerStatus.Serving : OfficerStatus.Free, deployed.Status);
        Assert.False(deployed.IsDormant);
        Assert.Equal(100000u, session.Snapshot.Settlements.Single(s => s.Id == 3).Soldiers);
        StateAssert.Equal(session.Capture(), SaveService.Decode(SaveService.Encode(session), source).Capture());
        Assert.Throws<InvalidDataException>(() => GameSession.Start(source, 17, playerForceIds: [1], setup: draft.Setup));

        draft.SetPlayerForces([1]);
        Assert.Empty(draft.Setup.Placements);
        Assert.True(draft.Compose().Settlements.Single(s => s.Id == 3).IsDormant);
        draft.SetPlayerForces([2]);
        draft.Deploy([person.Id], 3, joinForce);
        Assert.Single(draft.Setup.Placements);
        Assert.True(source.Forces.Single(f => f.Id == 2).IsDormant);
        Assert.True(source.Settlements.Single(s => s.Id == 3).IsDormant);
        Assert.DoesNotContain(source.Officers, p => p.State.Id == id);
    }

    [Fact]
    public void RestoringExplicitSelectionDoesNotActivateTheScenarioDefaultForce()
    {
        var definition = Definition() with { PlayerForceId = 2 };
        var session = GameSession.Start(definition, 17, playerForceIds: [1]);
        var snapshot = session.Capture();
        Assert.True(session.Snapshot.Forces.Single(f => f.Id == 2).IsDormant);
        StateAssert.Equal(snapshot, GameSession.Restore(definition, snapshot).Capture());
        StateAssert.Equal(snapshot, SaveService.Decode(SaveService.Encode(session), definition).Capture());
        var loaded = SaveService.Load(SaveService.Capture(session), definition);
        Assert.True(loaded.Snapshot.Forces.Single(f => f.Id == 2).IsDormant);
        Assert.Equal(new[] { 1 }, loaded.InitialPlayerForceIds);
    }

    [Fact]
    public void ActivationDefinitionsAndCurrentParticipationAreValidatedIndependentlyOfHistory()
    {
        var d = Definition(); var activation = (ForceActivationSearchEventDefinition)d.SearchEvents[0];
        foreach (var bad in new[] { activation with { ForceId = 99 }, activation with { CityId = 99 },
            activation with { AttachedSettlementIds = [99] }, activation with { OfficerIds = [99] },
            activation with { CitySoldiers = 500001 }, activation with { MinimumTrust = -1 },
            activation with { ConsumptionKey = default } })
            Assert.Throws<InvalidDataException>(() => (d with { SearchEvents = [bad] }).Validate());
        Assert.Throws<InvalidDataException>(() => (d with { Military = null }).Validate());

        var session = GameSession.Start(d, 17); var saved = session.Capture();
        var consumed = saved with
        {
            World = saved.World with { Simulation = saved.World.Simulation with { ConsumedEventKeys = [activation.ConsumptionKey] } }
        };
        StateAssert.Equal(consumed, GameSession.Restore(d, consumed).Capture());
        Assert.True(GameSession.Restore(d, consumed).Snapshot.Forces.Single(f => f.Id == 2).IsDormant);

        // Actual participation relationships, unlike history, remain mandatory.
        Assert.Throws<ArgumentException>(() => GameSession.Restore(d, saved with
        {
            World = saved.World with { Forces = saved.World.Forces.Select(f => f.Id == 2 ? f with { IsDormant = false } : f).ToArray() }
        }));
        StateAssert.Equal(saved.World, session.Snapshot);
    }
}
