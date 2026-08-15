using San9.Content.Serialization;
using San9.Content.Validation;

namespace San9.Content.Tests;

public sealed class ContentPackageTests
{
    [Fact]
    public async Task DevFixture_LoadsAndPassesValidation()
    {
        string fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "dev-fixture");

        ContentPackage package = await ContentPackageLoader.LoadAsync(fixtureDirectory);
        ContentValidationResult validation = ContentValidator.Validate(package);

        Assert.Equal("dev-fixture-m0", package.Manifest.PackageId);
        Assert.Equal("dev-m0", package.Scenario.ScenarioId);
        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Issues));
    }
}
