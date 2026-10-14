using San9.Tests;
using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class FormationEligibilityTests
{
    private static readonly CommandContext Player = new("player");
    private const int RequiredTactic = 8000;

    [Theory]
    [InlineData(false, "none", false)]
    [InlineData(false, "member", true)]
    [InlineData(false, "force", true)]
    [InlineData(false, "treasure", true)]
    [InlineData(false, "nonmember", false)]
    [InlineData(false, "buried", false)]
    [InlineData(true, "none", false)]
    [InlineData(true, "member", true)]
    [InlineData(true, "force", true)]
    [InlineData(true, "treasure", true)]
    [InlineData(true, "nonmember", false)]
    [InlineData(true, "buried", false)]
    public void DeploymentAndReformationUseOnlyCurrentForceAndParticipatingMembers(bool reform, string grant, bool accepted)
    {
        var world = WorldState.Create(new(190, 1, 1), 42,
            [new(1, 0) { ProvidedTactics = grant == "force" ? [RequiredTactic] : [] }],
            [new(1, 1, 1000, 100, 100, 70, 0, 0, 100, 0, 100)],
            [new(11, 1, reform ? null : 1, new(70, 70, 70, 70), false)
                { UnitId = reform ? 0 : null, LearnedTactics = grant == "member" ? [RequiredTactic] : [] },
             new(12, 1, 1, new(70, 70, 70, 70), false)
                { LearnedTactics = grant == "nonmember" ? [RequiredTactic] : [] }],
            units: reform ? [new(0, 1, new(4, 2), 500) { FormationId = 10, Morale = 70 }] : [],
            treasures: [new(1, grant == "nonmember" ? 12 : 11,
                grant == "buried" ? TreasureDiscovery.Buried : TreasureDiscovery.Discovered, null)],
            controls: [new("player", 1, ControllerKind.Player)], corps: [new San9.Core.Domain.CorpsState(1, 1, null, new(1000, 10000)) { IsRoot = true }]);
        var military = new MilitaryRulebook(new(3, 1, [0, 0, 0], [new(0, 1, null)],
            [new(10, 5, 0, 10000, 1, new(1, 1), TravelMode.Land),
             new(999, 5, 50, 10000, 1, new(1, 1), TravelMode.Land) { RequiredTacticId = RequiredTactic }],
            [new(1, new(2, 2), new(1, 1))])
        { Tactics = [new(RequiredTactic, 4, TacticEffect.FormationTechnology, 0, 0, 0, false, false, [999])] });
        var treasures = new TreasureRulebook([new(1, default, 0)
            { TacticId = grant is "treasure" or "buried" or "nonmember" ? RequiredTactic : null }]);
        var engine = new SimulationEngine(world, EconomyRulebook.Empty, PersonnelRulebook.Empty, treasures, military: military);
        var before = engine.Snapshot;
        GameCommand command = reform
            ? new ReformUnitCommand(Guid.NewGuid(), 1, 0, 0, 999, [11], null)
            : new DeployUnitCommand(Guid.NewGuid(), 1, 0, 1, [11], 500, 999, new(MilitaryOrderKind.Move, new(8, 2), null));
        var result = engine.Execute(Player, command);
        accepted = accepted && !reform;
        Assert.Equal(accepted, result.Submission.Accepted);
        if (accepted)
        {
            Assert.Equal(999, Assert.Single(engine.Snapshot.Units).FormationId);
            Assert.Equal(before.Corps.Where(c => c.IsRoot).OrderBy(c => c.OwnerId).ElementAt(0).Treasury.Money - 50, engine.Snapshot.Corps.Where(c => c.IsRoot).OrderBy(c => c.OwnerId).ElementAt(0).Treasury.Money);
        }
        else
        {
            Assert.Equal(reform ? CommandRejection.UnsupportedCommand : CommandRejection.MissingFormationTactic, result.Submission.Rejection);
            StateAssert.Equal(before, engine.Snapshot);
        }
    }

    [Fact]
    public void MissingRequiredFieldAndNegativeRequirementAreRejected()
    {
        var formation = new FormationRule(1, 1, 0, 1000, 1, new(1, 1), TravelMode.Land) { RequiredTacticId = -1 };
        Assert.Throws<ArgumentException>(() => new MilitaryRulebook(new(1, 1, [0], [new(0, 1, null)], [formation], [])));
        var json = System.Text.Json.JsonSerializer.Serialize(formation);
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        node.Remove(nameof(FormationRule.RequiredTacticId));
        Assert.Throws<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<FormationRule>(node.ToJsonString()));
    }
}
