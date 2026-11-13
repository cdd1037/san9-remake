using System.Collections.Immutable;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Application.Tests;

public sealed class FormationTeachingSessionTests
{
    [Fact]
    public void NewGameRequiresLearningAdvancedFormationsButLegacyMigrationPreservesPriorRule()
    {
        var formation = new FormationRule(7, 1, 0, 10000, 5, new(1, 1), TravelMode.Land)
        { Combat = new(10, 10, 10, 10, 50), Attack = new(1, true),
            Positions = FormationTestData.Layout([BattleRank.Middle, BattleRank.Middle, BattleRank.Middle, BattleRank.Middle, BattleRank.Middle]) };
        var rules = new MilitaryRulebook(new(1, 1, [0], [new(0, 1, null)],
            [formation, formation with { Id = 28, MixedTroopsOnly = true }], []));
        var person = new OfficerState(1, 1, 1, new(90, 90, 90, 90), false);
        var force = new ForceState(1, 0);
        var fresh = Assert.Single(FormationKnowledgeMigration.Initialize([person], [force], [], rules, newGame: true));
        var legacy = Assert.Single(FormationKnowledgeMigration.Initialize([person], [force], [], rules));
        Assert.True(FormationKnowledge.Knows(fresh, 7));
        Assert.False(FormationKnowledge.Knows(fresh, 28));
        Assert.True(FormationKnowledge.Knows(legacy, 28));
        var authored = person with { FormationProficiencies = ImmutableSortedDictionary<int, int>.Empty.Add(28, 10) };
        Assert.Equal(10, Assert.Single(FormationKnowledgeMigration.Initialize([authored], [force], [], rules, newGame: true)).FormationProficiencies![28]);
    }

    [Fact]
    public void TeachingContinuesAcrossSaveAndCompletesAfterThirtyDays()
    {
        var known = ImmutableSortedDictionary<int, int>.Empty.Add(7, 500);
        var basis = G01ScenarioTests.Fixture();
        var scenario = basis with
        {
            Officers = basis.Officers.Select(p => p with { InitialFormationEvidence = p.State.Id == 1
                ? [new(7, 600, FormationEvidenceKind.ExperienceInference, "测试来源", "游戏推定"),
                    new(28, 500, FormationEvidenceKind.ExperienceInference, "测试来源", "游戏推定")] : null, State = p.State with
            {
                FormationProficiencies = p.State.Id == 1 ? known : ImmutableSortedDictionary<int, int>.Empty
            } }).ToArray(),
            Military = new MilitaryDefinition(3, 3, Enumerable.Repeat(0, 9).ToImmutableArray(), [new(0, 1, null)],
                [new(7, 1, 0, 10000, 5, new(1, 1), TravelMode.Land)
                { Combat = new(10, 10, 10, 10, 50), Attack = new(1, true),
                    Positions = FormationTestData.Layout([BattleRank.Middle, BattleRank.Middle, BattleRank.Middle, BattleRank.Middle, BattleRank.Middle]) },
                 new(28, 1, 0, 10000, 5, new(1, 1), TravelMode.Land) { MixedTroopsOnly = true }],
                [new(1, new(7, 7), new(1, 1)), new(2, new(12, 12), new(1, 1))])
        };
        var session = GameSession.Start(scenario, 42, settings: new() { FormationLearningEnabled = true });
        Assert.Equal(600, session.Snapshot.Officers.Single(p => p.Id == 1).FormationProficiencies![7]);
        Assert.Equal(500, session.Snapshot.Officers.Single(p => p.Id == 1).FormationProficiencies![28]);
        var command = new StartFormationTeachingCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, 1, 1, 2, 7);
        Assert.True(session.Execute(new("player"), command).Submission.Accepted);
        session.AdvanceXun();
        var restored = SaveService.Decode(SaveService.Encode(session), scenario);
        Assert.Equal(20, restored.Snapshot.Officers.Single(p => p.Id == 2).FormationTeaching!.DaysRemaining);
        var capture = restored.Capture();
        var changed = capture with { World = capture.World with { Officers = capture.World.Officers.Select(p => p.Id == 1
            ? p with { FormationProficiencies = p.FormationProficiencies!.SetItem(7, 123) } : p).ToArray() } };
        Assert.Equal(123, GameSession.Restore(scenario, changed).Snapshot.Officers.Single(p => p.Id == 1).FormationProficiencies![7]);
        for (int i = 0; i < 2; i++)
        {
            session.AdvanceXun();
            restored.AdvanceXun();
        }
        var student = restored.Snapshot.Officers.Single(p => p.Id == 2);
        Assert.Null(student.FormationTeaching);
        Assert.Equal(0, student.FormationProficiencies![7]);
        StateAssert.Equal(session.Capture(), restored.Capture());
    }
}
