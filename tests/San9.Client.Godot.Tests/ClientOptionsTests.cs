using San9.Client.Godot;

namespace San9.Client.Godot.Tests;

public sealed class ClientOptionsTests
{
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
