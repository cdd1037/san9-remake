using San9.Client.Godot;

namespace San9.Client.Godot.Tests;

public sealed class ClientOptionsTests
{
    [Fact]
    public void OpeningStartupHasNoImplicitScenarioAndExplicitSelectionHasNoYearConvention()
    {
        var opening = ClientOptions.Parse([], Environment.CurrentDirectory);
        Assert.Null(opening.PackageRoot);
        Assert.Null(opening.ScenarioId);
        Assert.Null(opening.PlayerForceId);
        var selected = ClientOptions.Parse(["--scenario", "custom-world", "--player-force", "73"], Environment.CurrentDirectory);
        Assert.Equal("custom-world", selected.ScenarioId);
        Assert.Equal(73, selected.PlayerForceId);
        Assert.Throws<ArgumentException>(() => ClientOptions.Parse(["--scenario", "custom", "--content-package", "package"], Environment.CurrentDirectory));
    }
    [Fact]
    public void IndependentUserDataIsAnExplicitStartupInput()
    {
        var root = Path.Combine(Environment.CurrentDirectory, "preview-userdata");
        var options = ClientOptions.Parse(["--user-data-root", root], Environment.CurrentDirectory);
        Assert.Equal(Path.GetFullPath(root), options.UserDataRoot);
        Assert.Null(ClientOptions.Parse([], Environment.CurrentDirectory).UserDataRoot);
    }

    [Fact]
    public void MapCenterIsResolvedAgainstLoadedDimensionsInsteadOfTheOriginalMap()
    {
        var expanded = ClientOptions.Parse(["--map-center", "254,281"], Environment.CurrentDirectory);
        Assert.Equal(new(254, 281), expanded.ResolveMapCenter(300, 300));
        Assert.Equal(new(254, 281), expanded.ResolveMapCenter(300, 300, new(7, 9)));
        Assert.Throws<ArgumentException>(() => expanded.ResolveMapCenter(200, 200));
        Assert.Throws<ArgumentException>(() => ClientOptions.Parse(["--map-center", "-1,281"], Environment.CurrentDirectory));
        var normal = ClientOptions.Parse([], Environment.CurrentDirectory);
        Assert.Equal(new(0, 0), normal.ResolveMapCenter(1, 1));
        Assert.Equal(new(20, 15), normal.ResolveMapCenter(40, 30));
        Assert.Equal(new(7, 9), normal.ResolveMapCenter(40, 30, new(7, 9)));
    }

    [Fact]
    public void NormalStartupDoesNotEnableHistoricalVerification()
    {
        var options = ClientOptions.Parse([], Environment.CurrentDirectory);
        Assert.False(options.RequiresWorldVerification);
        Assert.False(options.TextSmoke);
    }

    [Fact]
    public void InterfaceVerificationDoesNotImplicitlyRunMapChecks()
    {
        var ui = ClientOptions.Parse(["--ui-smoke", "--map-quit"], Environment.CurrentDirectory);
        Assert.True(ui.RequiresWorldVerification);
        Assert.True(ui.UiSmoke);
        Assert.False(ui.MapSmoke);
        var combined = ClientOptions.Parse(["--ui-smoke", "--map-smoke"], Environment.CurrentDirectory);
        Assert.True(combined.UiSmoke && combined.MapSmoke);
    }

    [Theory]
    [InlineData("--map-smoke")]
    [InlineData("--ui-smoke")]
    [InlineData("--runtime-smoke")]
    [InlineData("--information-smoke")]
    public void TextVerificationCannotSilentlySuppressWorldChecks(string worldCheck)
    {
        Assert.Throws<ArgumentException>(() => ClientOptions.Parse(["--text-smoke", worldCheck], Environment.CurrentDirectory));
    }

    [Fact]
    public void ComputerDamageBonusIsAnExplicitNewGameOption()
    {
        Assert.Equal(0, ClientOptions.Parse([], Environment.CurrentDirectory).ComputerDamageBonusPercent);
        Assert.Equal(100, ClientOptions.Parse(["--computer-damage-bonus", "100"], Environment.CurrentDirectory).ComputerDamageBonusPercent);
        Assert.Throws<ArgumentOutOfRangeException>(() => ClientOptions.Parse(["--computer-damage-bonus", "-1"], Environment.CurrentDirectory));
        Assert.Throws<ArgumentException>(() => ClientOptions.Parse(["--computer-damage-bonus"], Environment.CurrentDirectory));
    }
}
