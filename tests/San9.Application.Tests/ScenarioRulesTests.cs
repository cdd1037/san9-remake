using San9.Application.Persistence;
using San9.Application.Presentation;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class ScenarioRulesTests
{
    [Fact]
    public void OfficePreviewCommitAndRestoreUseCompleteScenarioRules()
    {
        var baseline = G01ScenarioTests.Fixture();
        var definition = baseline with
        {
            Information = baseline.Information with
            {
                Ranks = [new(8, "rank", 20000, 0, 0, 12)],
                Offices = [new(400, "office", 20000, 7)
                    { RequiredRankId = 8, AbilityBonuses = new(2, 3, 4, 5) }]
            },
            Forces = baseline.Forces.Select(f => f with { RankId = 8 }).ToArray()
        };
        var session = GameSession.Start(definition, 190);
        var view = session.View;
        var rules = view.Rules;
        var projected = view.ForWorld(view.World with { Clock = new(190, 1, 2) });
        Assert.Same(rules, projected.Rules);
        Assert.Equal(8, rules.Governance.Offices[400].RequiredRankId);
        Assert.Equal(7, rules.Governance.Offices[400].Salary);
        Assert.True(rules.Governance.CanAppoint(8, rules.Governance.Offices[400]));
        Assert.False(rules.Governance.CanAppoint(null, rules.Governance.Offices[400]));
        var plan = new OfficeAppointmentPlan(view, [2], [400]);
        Assert.True(plan.Assign(2, 400));
        var expected = plan.Abilities(2);
        Assert.NotEqual(view.Officers.Single(p => p.Id == 2).Abilities, expected);
        var command = new AssignOfficesCommand(Guid.NewGuid(), 1, 0, plan.Changes);
        Assert.True(session.Execute(new("player"), command).Submission.Accepted);
        Assert.Equal(expected, session.View.Officers.Single(p => p.Id == 2).Abilities);
        Assert.Same(rules, session.View.Rules);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        Assert.Equal(expected, restored.View.Officers.Single(p => p.Id == 2).Abilities);
        Assert.NotSame(rules, restored.View.Rules);
    }
}
