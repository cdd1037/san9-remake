using San9.Client.Godot;

namespace San9.Client.Godot.Tests;

public sealed class ClientOptionsTests
{
    [Fact]
    public void ComputerDamageBonusIsAnExplicitNewGameOption()
    {
        Assert.Equal(0, ClientOptions.Parse([], Environment.CurrentDirectory).ComputerDamageBonusPercent);
        Assert.Equal(100, ClientOptions.Parse(["--computer-damage-bonus", "100"], Environment.CurrentDirectory).ComputerDamageBonusPercent);
        Assert.Throws<ArgumentOutOfRangeException>(() => ClientOptions.Parse(["--computer-damage-bonus", "-1"], Environment.CurrentDirectory));
        Assert.Throws<ArgumentException>(() => ClientOptions.Parse(["--computer-damage-bonus"], Environment.CurrentDirectory));
    }
}
