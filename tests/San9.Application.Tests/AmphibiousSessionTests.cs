using San9.Tests;
using System.Collections.Immutable;
using San9.Application.Persistence;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class AmphibiousSessionTests
{
    private static ScenarioInitialState Fixture() => new(ScenarioInitialState.CurrentSchemaVersion, "amphibious", 190, 1, 1, 1,
        [new(1, "force", 1, 100)],
        [new(1, "origin", 1, 0, 1, 1000, 100, 100, 70, []), new(2, "destination", 1, 7, 1, 100, 100, 100, 30, [])], [], [])
    {
        Officers = [new("officer", new(1, 1, 1, new(70, 70, 70, 70), false))],
        Military = new(8, 3, Enumerable.Range(0, 24).Select(i => i % 8 is >= 2 and <= 4 ? 1 : 0).ToImmutableArray(),
            [new(0, 2, null), new(1, null, 3) { IsWater = true }],
            [new(100, 2, 50, 10000, 5, new(1, 1), TravelMode.Land),
             new(101, 2, 50, 10000, 5, new(1, 1), TravelMode.Land),
             new(200, 3, 0, 10000, 5, new(1, 1), TravelMode.Water),
             new(300, 6, 100, 10000, 5, new(3, 3), TravelMode.Water)],
            [new(1, new(2, 7), new(1, 1)), new(2, new(37, 7), new(1, 1))]) { BasicNavalFormationId = 200 },
        Information = new([], [], [], [new CorpsState(1, 1, null, new(10000, 10000)) { IsRoot = true }], [])
    };

    public static IEnumerable<object[]> CandidateCases() =>
        from reform in new[] { false, true }
        from naval in new[] { false, true }
        from additional in new[] { false, true }
        from common in new[] { false, true }
        from learned in new[] { false, true }
        from barbarian in new[] { false, true }
        select new object[] { reform, naval, additional, common, learned, barbarian };

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FormationCatalogsIncludeOnlyForceCandidatesWithRequiredTactics(bool additional, bool learned)
    {
        var source = Fixture();
        var definition = source with
        {
            Forces = source.Forces.Select(f => f with { AdditionalFormationIds = additional ? [100, 101, 300] : [] }).ToArray(),
            Officers = source.Officers.Select(o => o with { State = o.State with { LearnedTactics = learned ? [8000] : [] } }).ToArray(),
            Military = source.Military! with
            {
                Formations = source.Military.Formations.Select(f => f with
                {
                    IsCommon = f.Id is 100 or 200,
                    RequiredTacticId = f.Id is 101 or 300 ? 8000 : null
                }).ToImmutableArray(),
                Tactics = [new(8000, 4, TacticEffect.FormationTechnology, 0, 0, 0, false, false, [101, 300])]
            },
            Information = source.Information with
            {
                Formations = source.Military.Formations.Select(f => new FormationCatalogDefinition(f.Id, $"formation.{f.Id}")).ToArray(),
                Tactics = [new(8000, "technology")]
            }
        };
        var texts = new TextCatalog(San9.Content.Text.TextCatalog.CurrentSchemaVersion, definition.Information.Formations.Select(f => f.NameKey).Append("technology")
            .ToDictionary(key => key, key => key));
        var session = GameSession.Start(definition, 42);
        var query = new InformationQuery(session, texts);
        var hash = session.Snapshot;
        Assert.Equal(additional && learned ? [100, 101] : new[] { 100 },
            query.Read(InformationCategory.Formations, force: 1).Rows.Select(row => row.Key.Id));
        Assert.Equal(additional && learned ? [200, 300] : new[] { 200 },
            query.Read(InformationCategory.Ships, force: 1).Rows.Select(row => row.Key.Id));
        StateAssert.Equal(hash, session.Snapshot);
    }

    [Theory]
    [MemberData(nameof(CandidateCases))]
    public void CandidateSourcesDoNotDependOnIdentityOrForceSelection(bool reform, bool naval, bool additional, bool common, bool learned, bool barbarian)
    {
        var source = Fixture();
        int special = naval ? 300 : 101;
        var definition = source with
        {
            Forces = source.Forces.Select(f => f with { IsBarbarian = barbarian,
                AdditionalFormationIds = additional ? [special] : [] }).ToArray(),
            Officers = source.Officers.Select(o => o with { State = o.State with { LearnedTactics = learned ? [8000] : [] } }).ToArray(),
            Military = source.Military! with { Tactics = [new(8000, 4, TacticEffect.FormationTechnology, 0, 0, 0, false, false, [special])],
                Formations = source.Military.Formations.Select(f =>
                f with { IsCommon = f.Id != special || common, RequiredTacticId = f.Id == special ? 8000 : null }).ToImmutableArray() }
        };
        var session = GameSession.Start(definition, 42);
        session.BeginRecording();
        if (reform)
        {
            Assert.True(session.Execute(new("player"), new DeployUnitCommand(Guid.NewGuid(), 1, 0, 1, [1], 400, 100,
                new(MilitaryOrderKind.Move, new(32, 7), null))).Submission.Accepted);
            Assert.Equal(100, Assert.Single(session.Snapshot.Units).FormationId);
            Assert.Equal(200, Assert.Single(session.Snapshot.Units).NavalFormationId);
        }
        var before = session.Capture();
        var order = new MilitaryOrder(MilitaryOrderKind.Move, new(32, 7), null);
        GameCommand command = reform
            ? new ReformUnitCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence,
                session.Snapshot.Units.Single().Id, naval ? 100 : special, [1], order) { NavalFormationId = naval ? special : null }
            : new DeployUnitCommand(Guid.NewGuid(), 1, 0, 1, [1], 400, naval ? 100 : special, order) { NavalFormationId = naval ? special : null };
        var result = session.Execute(new("player"), command);
        bool candidate = common || additional;
        bool accepted = !reform && candidate && learned;
        Assert.Equal(accepted, result.Submission.Accepted);
        if (!accepted)
        {
            Assert.Equal(reform ? CommandRejection.UnsupportedCommand : candidate ? CommandRejection.MissingFormationTactic : CommandRejection.FormationUnavailable, result.Submission.Rejection);
            StateAssert.Equal(before, session.Capture());
        }
        else
        {
            var unit = Assert.Single(session.Snapshot.Units);
            Assert.Equal(special, naval ? unit.NavalFormationId : unit.FormationId);
            var restored = SaveService.Decode(SaveService.Encode(session), definition);
            StateAssert.Equal(session.Capture(), restored.Capture());
            Assert.Equal(additional ? new[] { special } : [], restored.Snapshot.Forces.Single().AdditionalFormationIds);
        }
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(99999)]
    public void UnknownAdditionalFormationReferencesAreRejected(int id)
    {
        var source = Fixture();
        var invalid = source with { Forces = source.Forces.Select(f => f with { AdditionalFormationIds = [id] }).ToArray() };
        Assert.Throws<InvalidDataException>(() => invalid.Validate());
    }

    [Fact]
    public void NavalSelectionMovementForecastSaveAndReplayShareTheSameState()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 42);
        session.BeginRecording();
        Assert.True(session.Execute(new("player"), new DeployUnitCommand(Guid.NewGuid(), 1, 0, 1, [1], 400, 100,
            new(MilitaryOrderKind.Move, new(32, 7), null))).Submission.Accepted);
        var unit = Assert.Single(session.Snapshot.Units);
        Assert.Equal(200, unit.NavalFormationId);
        var forecast = session.View.UnitArrival(unit.Id);
        Assert.Equal(ArrivalEstimateStatus.Known, forecast.Status);
        session.AdvanceXun();
        unit = Assert.Single(session.Snapshot.Units);
        Assert.Equal(200, session.View.FormationId(unit));
        Assert.Equal(forecast.Days - 10, session.View.UnitArrival(unit.Id).Days);
        var before = session.Capture();
        Assert.Equal(CommandRejection.UnsupportedCommand, session.Execute(new("player"), new ReformUnitCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, unit.Id, 100, [1], unit.Order) { NavalFormationId = 100 }).Submission.Rejection);
        StateAssert.Equal(before, session.Capture());
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        StateAssert.Equal(session.Capture(), restored.Capture());
        Assert.Equal(session.View.UnitArrival(unit.Id), restored.View.UnitArrival(unit.Id));
        for (int i = 0; i < 5 && session.Snapshot.Units.Single().Order is not null; i++)
        {
            EventAssert.Equal(session.AdvanceXun().Events, restored.AdvanceXun().Events);
            StateAssert.Equal(session.Capture(), restored.Capture());
        }
        unit = Assert.Single(session.Snapshot.Units);
        Assert.Equal(new WorldPosition(32, 7), unit.Position);
        Assert.Equal(100, session.View.FormationId(unit));
        Assert.Equal(200, unit.NavalFormationId);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }
}
