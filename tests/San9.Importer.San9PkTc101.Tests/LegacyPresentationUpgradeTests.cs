using San9.Content.Interface;
using San9.Importer.San9PkTc101.Packages;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class LegacyPresentationUpgradeTests
{
    [Fact]
    public void AddsMissingRolesWithoutReplacingExistingBindings()
    {
        var original = new DecisionMessageDefinition("custom.message", []);
        var progress = new ProgressPresentationDefinition(new() { ["unit-contact"] = original }, []);
        var source = CommandPresentation.Empty with { Progress = progress };

        var result = LegacyPresentationUpgrade.Prepare(source);

        Assert.Same(original, result.Progress!.Records["unit-contact"]);
        Assert.Contains("settlement-contact", result.Progress.Records.Keys);
        Assert.Single(progress.Records);
    }
}
