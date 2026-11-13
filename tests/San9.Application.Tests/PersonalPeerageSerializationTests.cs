using System.Text.Json;
using System.Collections.Immutable;
using San9.Application.Persistence;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class PersonalPeerageSerializationTests
{
    [Fact]
    public void HistoricalTimelineSelectsTheRecordForTheLoadDateAndRejectsOverlaps()
    {
        var person = new OfficerState(2, 1, 1, new(50, 50, 50, 50), false);
        var early = new PersonalPeerageEvidence(new(PersonalPeerageGrade.VillageMarquis, "都亭侯"),
            new(224, 1, 1), new(229, 12, 30), PersonalPeerageEvidenceKind.Historical, "早期来源");
        var late = new PersonalPeerageEvidence(new(PersonalPeerageGrade.CountyMarquis, "南郑侯"),
            new(231, 1, 1), new(233, 12, 30), PersonalPeerageEvidenceKind.Historical, "后期来源");
        OfficerInitialState[] content = [new("person", person) { InitialPersonalPeerageHistory = [early, late] }];
        Assert.Equal("都亭侯", PersonalPeerageMigration.Initialize([person], content, new(225, 1, 1))[0].PersonalPeerage!.Title);
        Assert.Null(PersonalPeerageMigration.Initialize([person], content, new(230, 1, 1))[0].PersonalPeerage);
        Assert.Equal("南郑侯", PersonalPeerageMigration.Initialize([person], content, new(232, 1, 1))[0].PersonalPeerage!.Title);
        var basis = G01ScenarioTests.Fixture();
        var bad = basis with { Officers = basis.Officers.Select(o => o.State.Id == 2 ? o with
            { InitialPersonalPeerageHistory = [early, late with { From = new(229, 12, 30) }] } : o).ToArray() };
        Assert.Throws<InvalidDataException>(bad.Validate);
        var scenario = basis with { Year = 225, Officers = basis.Officers.Select(o => o.State.Id == 2 ? o with
            { InitialPersonalPeerageHistory = [early, late] } : o).ToArray() };
        var session = GameSession.Start(scenario, 42);
        var saved = session.Capture();
        var legacy = saved with { PersonalPeerageInitialized = null, World = saved.World with
        {
            Clock = new(232, 1, 1), Officers = saved.World.Officers.Select(p => p.Id == 2 ? p with { PersonalPeerage = null } : p).ToArray()
        } };
        var migrated = GameSession.Restore(scenario, legacy);
        Assert.Equal("南郑侯", migrated.Snapshot.Officers.Single(p => p.Id == 2).PersonalPeerage!.Title);
    }

    [Fact]
    public void MeritIsSavedPerForceAndOldSaveWithoutItLoads()
    {
        var basis = G01ScenarioTests.Fixture();
        var scenario = basis with { Officers = basis.Officers.Select(o => o.State.Id == 2
            ? o with { State = o.State with { BattleMerit = ImmutableSortedDictionary<int, int>.Empty.Add(1, 4).Add(2, 7), ServiceMerit = ImmutableSortedDictionary<int, int>.Empty.Add(1, 3) } } : o).ToArray() };
        var session = GameSession.Start(scenario, 42);
        var saved = SaveService.Encode(session);
        var restored = SaveService.Decode(saved, scenario);
        Assert.Equal(4, restored.Snapshot.Officers.Single(p => p.Id == 2).BattleMeritFor(1));
        Assert.Equal(7, restored.Snapshot.Officers.Single(p => p.Id == 2).BattleMeritFor(2));
        Assert.Equal(3, restored.Snapshot.Officers.Single(p => p.Id == 2).ServiceMeritFor(1));
        var json = System.Text.Json.Nodes.JsonNode.Parse(saved)!;
        foreach (var item in json["session"]!["world"]!["officers"]!.AsArray())
        {
            item!.AsObject().Remove("battleMerit");
            item.AsObject().Remove("serviceMerit");
        }
        var old = SaveService.Decode(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()), scenario);
        Assert.Equal(0, old.Snapshot.Officers.Single(p => p.Id == 2).BattleMeritFor(1));
        Assert.Equal(0, old.Snapshot.Officers.Single(p => p.Id == 2).ServiceMeritFor(1));
    }

    [Fact]
    public void HistoricalEvidenceRespectsDatesAndDoesNotCreateTitleForDeadOfficer()
    {
        var person = new OfficerState(2, 1, 1, new(50, 50, 50, 50), false);
        var evidence = new PersonalPeerageEvidence(new(PersonalPeerageGrade.VillageMarquis, "例亭侯"),
            new(200, 1, 1), new(210, 12, 30), PersonalPeerageEvidenceKind.Romance, "测试来源");
        OfficerInitialState[] content = [new("person", person) { InitialPersonalPeerage = evidence }];
        Assert.Null(PersonalPeerageMigration.Initialize([person], content, new(199, 12, 30))[0].PersonalPeerage);
        Assert.Equal("例亭侯", PersonalPeerageMigration.Initialize([person], content, new(200, 1, 1))[0].PersonalPeerage!.Title);
        Assert.Null(PersonalPeerageMigration.Initialize([person], content, new(211, 1, 1))[0].PersonalPeerage);
        Assert.Null(PersonalPeerageMigration.Initialize([person with { Status = OfficerStatus.Dead }], content, new(205, 1, 1))[0].PersonalPeerage);
    }

    [Fact]
    public void HistoricalInitialTitleIsAppliedOnceAndRevocationSurvivesRestore()
    {
        var basis = G01ScenarioTests.Fixture();
        var evidence = new PersonalPeerageEvidence(new(PersonalPeerageGrade.VillageMarquis, "例亭侯"),
            new(189, 1, 1), new(195, 12, 30), PersonalPeerageEvidenceKind.Historical, "测试来源");
        var content = basis with { Officers = basis.Officers.Select(o => o.State.Id == 2 ? o with { InitialPersonalPeerage = evidence } : o).ToArray() };
        var session = GameSession.Start(content, 42);
        Assert.Equal("例亭侯", session.Snapshot.Officers.Single(p => p.Id == 2).PersonalPeerage!.Title);
        var snapshot = session.Capture();
        var cleared = snapshot with { World = snapshot.World with { Officers = snapshot.World.Officers.Select(p => p.Id == 2
            ? p with { PersonalPeerage = null } : p).ToArray() } };
        var revoked = GameSession.Restore(content, cleared);
        Assert.Null(revoked.Snapshot.Officers.Single(p => p.Id == 2).PersonalPeerage);
        Assert.Null(SaveService.Decode(SaveService.Encode(revoked), content).Snapshot.Officers.Single(p => p.Id == 2).PersonalPeerage);
        var legacy = cleared with { PersonalPeerageInitialized = null };
        var migrated = GameSession.Restore(content, legacy);
        Assert.Equal("例亭侯", migrated.Snapshot.Officers.Single(p => p.Id == 2).PersonalPeerage!.Title);
        Assert.True(migrated.Capture().PersonalPeerageInitialized);
    }

    [Fact]
    public void AuthoredRegimeDrivesCommandAndSurvivesSave()
    {
        var original = G01ScenarioTests.Fixture();
        var period = new PersonalPeeragePeriod(180, 220,
            ImmutableDictionary<PersonalPeerageGrade, ImmutableArray<int>>.Empty.Add(PersonalPeerageGrade.VillageMarquis, [3]), [])
        { GradeRules = PersonalPeeragePolicy.Default.Grades.Values.OrderBy(r => r.Grade)
            .Select(r => r.Grade == PersonalPeerageGrade.VillageMarquis ? r with { Limit = 1, Salary = 8, LoyaltyBonus = 7 } : r).ToImmutableArray() };
        var content = original with
        {
            Forces = original.Forces.Select(f => f.Id == 1 ? f with { RankId = 3, PersonalPeerageRegimeId = "han" } : f).ToArray(),
            Information = original.Information with { Ranks = [new(3, "州牧", 10000, 0, 0, 1)] },
            PersonalPeerageRegimes = [new("han", [period])]
        };
        var session = GameSession.Start(content, 42);
        int previousLoyalty = session.Snapshot.Officers.Single(p => p.Id == 2).Loyalty;
        Assert.Contains(MapCommandMenu.Settlement(session.View, 1).Single(g => g.Id == "appointments").Children,
            entry => entry.Id == "personal-peerage" && entry.Enabled);
        var result = session.Execute(new("player"), new SetPersonalPeerageCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, 2, new(PersonalPeerageGrade.VillageMarquis, "亭侯")));
        Assert.True(result.Submission.Accepted);
        Assert.Equal(Math.Min(255, previousLoyalty + 7), session.Snapshot.Officers.Single(p => p.Id == 2).Loyalty);
        Assert.Equal(8, session.View.PersonalPeeragePolicy(1).Salary(new(PersonalPeerageGrade.VillageMarquis, null)));
        var overQuota = session.Execute(new("player"), new SetPersonalPeerageCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, 3, new(PersonalPeerageGrade.VillageMarquis, null)));
        Assert.Equal(CommandRejection.PeerageQuotaFull, overQuota.Submission.Rejection);
        var restored = SaveService.Decode(SaveService.Encode(session), content);
        Assert.Equal("han", restored.Snapshot.Forces.Single(f => f.Id == 1).PersonalPeerageRegimeId);
        Assert.Equal(new PersonalPeerage(PersonalPeerageGrade.VillageMarquis, "亭侯"), restored.Snapshot.Officers.Single(p => p.Id == 2).PersonalPeerage);
        var saved = session.Capture();
        var legacy = saved with { World = saved.World with
        {
            Forces = saved.World.Forces.Select(f => f.Id == 1 ? f with { PersonalPeerageRegimeId = null } : f).ToArray(),
        }, PersonalPeerageInitialized = null };
        Assert.Equal("han", GameSession.Restore(content, legacy).Snapshot.Forces.Single(f => f.Id == 1).PersonalPeerageRegimeId);
    }

    [Fact]
    public void PeerageCommandAndEventRoundTripWithPolymorphicDiscriminators()
    {
        var peerage = new PersonalPeerage(PersonalPeerageGrade.VillageMarquis, "汉寿亭侯");
        GameCommand command = new SetPersonalPeerageCommand(Guid.NewGuid(), 1, 5, 2, peerage);
        DomainEvent result = new PersonalPeerageChangedEvent(new(190, 1, 1), 1, 2, null, peerage);
        Assert.Equal(command, StrictJson.Deserialize<GameCommand>(JsonSerializer.SerializeToUtf8Bytes(command, StrictJson.Options)));
        Assert.Equal(result, StrictJson.Deserialize<DomainEvent>(JsonSerializer.SerializeToUtf8Bytes(result, StrictJson.Options)));
    }

    [Fact]
    public void StrictSaveRoundTripsTitleAndReadsLegacyOfficerWithoutTitle()
    {
        var person = new OfficerState(1, 1, 1, new(50, 50, 50, 50), false)
        { OfficeId = 7, PersonalPeerage = new(PersonalPeerageGrade.VillageMarquis, "汉寿亭侯") };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(person, StrictJson.Options);
        var restored = StrictJson.Deserialize<OfficerState>(bytes);
        Assert.Equal(person.PersonalPeerage, restored.PersonalPeerage);
        Assert.Equal(person.OfficeId, restored.OfficeId);
        var legacy = person with { PersonalPeerage = null };
        Assert.Null(StrictJson.Deserialize<OfficerState>(JsonSerializer.SerializeToUtf8Bytes(legacy, StrictJson.Options)).PersonalPeerage);
    }
}
