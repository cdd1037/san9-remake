using San9.Application.Presentation;
using San9.Core.Simulation;

namespace San9.Client.Godot.Tests;

public sealed class OpeningForceCatalogTests
{
    [Fact]
    public void OrdinaryForceValuesRemainVisibleWhileBarbarianSecretsStayHidden()
    {
        var force = new OpeningForce(71, "张角", "张宝", "", 180, 22, 2115000, 93000, 275000, [7])
        { Outlook = new(new ResourceOutlook(18000, 300, 14800, 32500), new(122500, 100, 400, 122800)) };
        Assert.Equal(180, OpeningForceCatalog.Cell(force, "trust").Number);
        Assert.Equal(18000, OpeningForceCatalog.Cell(force, "money").Number);
        Assert.Equal(14500, OpeningForceCatalog.Cell(force, "money-balance").Number);
        var barbarian = force with { IsBarbarian = true };
        Assert.True(OpeningForceCatalog.Cell(barbarian, "trust").IsUnavailable);
        Assert.True(OpeningForceCatalog.Cell(barbarian, "food-income").IsUnavailable);
        Assert.Equal(275000, OpeningForceCatalog.Cell(barbarian, "soldiers").Number);
        Assert.Equal(2115000, OpeningForceCatalog.Cell(barbarian, "population").Number);
    }
}
