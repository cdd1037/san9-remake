using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Importer.San9PkTc101.Scenarios;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class InitialLeadershipCompilerTests
{
    [Fact]
    public void SourceWorkingFlagsDoNotProduceDuplicateCommandersOrOverrideRulerPriority()
    {
        var scenario = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "custom", 281, 1, 1, null,
            [], [new(29, "place", 6, 0, 0, 0, 100, 100, 0, [])], [], [])
        { Officers = [new("ruler", new(208, 6, 29, new(1, 1, 1, 1), false) { Status = OfficerStatus.Ruler, IsCommander = true }),
            new("officer", new(622, 6, 29, new(99, 99, 99, 99), false) { IsCommander = true })] };
        var compiled = InitialLeadershipCompiler.Compile(scenario);
        Assert.Equal(208, Assert.Single(compiled.Officers, o => o.State.IsCommander).State.Id);
        Assert.All(scenario.Officers, o => Assert.True(o.State.IsCommander));
    }
}
