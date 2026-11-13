using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class ResearchProjectTests
{
    [Fact]
    public void ResearchCatalogRejectsMissingAndCyclicPrerequisites()
    {
        var definition = new MilitaryDefinition(1, 1, [0], [new(0, 1, null)], [], []);
        Assert.Throws<ArgumentException>(() => new MilitaryRulebook(definition with
        {
            ResearchProjects = [new("advanced", 100, 3, ["basic"])],
        }));
        Assert.Throws<ArgumentException>(() => new MilitaryRulebook(definition with
        {
            ResearchProjects = [new("basic", 100, 3, ["advanced"]), new("advanced", 100, 3, ["basic"])],
        }));
        var rules = new MilitaryRulebook(definition with
        {
            ResearchProjects = [new("basic", 100, 3, []), new("advanced", 200, 5, ["basic"])],
        });
        Assert.Equal(2, rules.ResearchProjects.Count);
    }

    [Fact]
    public void WorldValidationRejectsUnknownAndUnmetResearchState()
    {
        var definition = new MilitaryDefinition(1, 1, [0], [new(0, 1, null)], [], [])
        {
            ResearchProjects = [new("basic", 100, 3, []), new("advanced", 200, 5, ["basic"])]
        };
        var rules = new MilitaryRulebook(definition);
        var world = new WorldSnapshot(new(190, 1, 1), new(new(190, 1, 1), 42))
        {
            Forces = [new(1, 100) { CompletedResearch = ["unknown"] }]
        };
        Assert.Throws<ArgumentException>(() => MilitaryMovement.Validate(world, rules));
        Assert.Throws<ArgumentException>(() => MilitaryMovement.Validate(world with
        {
            Forces = [new(1, 100) { CompletedResearch = ["advanced"] }]
        }, rules));
        Assert.Throws<ArgumentException>(() => MilitaryMovement.Validate(world with
        {
            Forces = [new(1, 100) { ActiveResearch = new("basic", 99, 2) }]
        }, rules));
    }
}
