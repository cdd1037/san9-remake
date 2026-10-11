using System.Collections.Immutable;
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
    public void ExplorationActivatesQueriesAndSurvivesSaveAndFullRecording()
    {
        var d = Definition(); var session = GameSession.Start(d, 17); session.BeginRecording();
        var texts = new TextCatalog(San9.Content.Text.TextCatalog.CurrentSchemaVersion, d.Forces.Select(f => f.NameKey).Concat(d.Officers.Select(p => p.NameKey)).Concat(d.RegionNameKeys.Values).Distinct().ToDictionary(k => k, k => k));
        var query = new InformationQuery(session, texts);
        Assert.DoesNotContain(query.Read(InformationCategory.Corps).Rows, r => r.Key.Id == 2);
        Assert.DoesNotContain(session.View.Settlements, s => s.Id is 3 or 4);
        Assert.False(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, 1, 3)).Submission.Accepted);
        Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, 1, 5)).Submission.Accepted);
        var before = SaveService.Decode(SaveService.Encode(session), d);
        var result = session.AdvanceXun(); var repeated = before.AdvanceXun();
        Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(repeated.Events));
        Assert.Single(result.Events.OfType<ForceActivatedBySearchEvent>());
        Assert.DoesNotContain(result.Events.OfType<SearchResolvedEvent>(), e => e.OfficerId == 1);
        Assert.Null(session.Snapshot.Simulation.PendingDailyTask);
        Assert.Contains(session.View.Settlements, s => s.Id == 3 && s.Soldiers == 500000);
        Assert.Contains(session.View.Settlements, s => s.Id == 4 && s.OwnerId == 2 && s.Soldiers == 0);
        Assert.Equal(5, session.View.Officers.Count(p => p.OwnerId == 2));
        Assert.Contains(query.Read(InformationCategory.Corps).Rows, r => r.Key.Id == 2);
        Assert.Equal(810, session.Snapshot.Forces.Single(f => f.Id == 1).BaseTrust);
        var restored = SaveService.Decode(SaveService.Encode(session), d);
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(restored));
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), d, GameSession.EngineFactory(d));
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(replay.Session));
        Assert.DoesNotContain(session.AdvanceXun().Events, e => e is ForceActivatedBySearchEvent);
        Assert.True(session.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, 1, 5)).Submission.Accepted);
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
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(SaveService.Decode(SaveService.Encode(session), d)));
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
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(SaveService.Decode(SaveService.Encode(session), d)));
    }

    [Fact]
    public void ActivationReferencesAndDormantSaveStateAreValidated()
    {
        var d = Definition(); var activation = (ForceActivationSearchEventDefinition)d.SearchEvents[0];
        foreach (var bad in new[] { activation with { ForceId = 99 }, activation with { CityId = 99 },
            activation with { AttachedSettlementIds = [99] }, activation with { OfficerIds = [99] },
            activation with { CitySoldiers = 500001 }, activation with { MinimumTrust = -1 },
            activation with { ConsumptionKey = default } })
            Assert.Throws<InvalidDataException>(() => (d with { SearchEvents = [bad] }).Validate());
        Assert.Throws<InvalidDataException>(() => (d with { Military = null }).Validate());
        Assert.Equal(GameSession.Fingerprint(d), GameSession.Fingerprint(d with
            { SearchEvents = [activation with { OfficerIds = activation.OfficerIds.Reverse().ToImmutableArray() }] }));
        Assert.NotEqual(GameSession.Fingerprint(d), GameSession.Fingerprint(d with
            { SearchEvents = [activation with { MinimumTrust = 701 }] }));
        var session = GameSession.Start(d, 17); var saved = session.Capture();
        Assert.Throws<ArgumentException>(() => GameSession.Restore(d, saved with
        {
            World = saved.World with { Simulation = saved.World.Simulation with { ConsumedEventKeys = [activation.ConsumptionKey] } }
        }));
        Assert.Equal(saved.World.StateHash, session.Snapshot.StateHash);
    }
}
