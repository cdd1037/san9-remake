using San9.Application;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class BuildingHoverTests
{
    [Theory]
    [InlineData(5, 0, BuildingHoverTheme.Own)]
    [InlineData(null, 100, BuildingHoverTheme.Other)]
    [InlineData(6, 39, BuildingHoverTheme.Other)]
    [InlineData(6, 40, BuildingHoverTheme.Friendly)]
    [InlineData(6, 100, BuildingHoverTheme.Friendly)]
    public void ThemeFollowsOwnershipAndRelationBoundary(int? owner, int relation, BuildingHoverTheme expected) =>
        Assert.Equal(expected, BuildingHoverInfo.ThemeFor(owner, 5, relation));

    [Theory]
    [InlineData(643, 200, 316, 643, 200)]
    [InlineData(644, 200, 248, 484, 200)]
    [InlineData(1023, 760, 316, 863, 420)]
    [InlineData(-2, -5, 248, 0, 0)]
    public void CardFitsOriginalLogicalBounds(float x, float y, int height, float expectedX, float expectedY) =>
        Assert.Equal((expectedX, expectedY), BuildingHoverGeometry.Place(x, y, height));

    [Fact]
    public void CardUsesCurrentServingPeopleAndIndependentCommanderState()
    {
        var initial = new ScenarioInitialState(16, "hover", 190, 1, 1, 5,
            [new(5, "force", 5, 500, 100, 100) { LeaderId = 1 }],
            [new(15, "city", 5, 89, 64, 32000, 440, 800, 50, ["merchant", "emperor"])
                { CorpsId = 5, Commerce = 560, MaxCommerce = 1000, Cultivation = 150, MaxCultivation = 400, PublicOrder = 150 },
             new(99, "gate", null, 90, 64, 0, 900, 1000, 0, []) { Kind = SettlementKind.Facility }], [], [])
        {
            Officers = [new("leader", new(1, 5, 15, new(1, 1, 100, 1), false) { IsCommander = true }),
                new("other", new(2, 5, 15, new(99, 99, 99, 99), false)),
                new("free", new(3, null, 15, new(1, 1, 1, 1), false) { Status = OfficerStatus.Free, IsCommander = true })],
            Information = InformationDefinitions.Empty with { Corps = [new(5, 5, 1, null)] }
        };
        var texts = new TextCatalog(3, new[] { "city", "gate", "force", "leader", "other", "free" }.ToDictionary(k => k, k => new TextEntry(k, k)));
        var session = GameSession.Start(initial, 190);
        var before = BuildingHoverInfo.Resolve(session.View, texts, 15)!;
        Assert.Equal(10, before.Rows.Count);
        Assert.Equal("leader", before.Rows.Single(r => r.Label == "大将").Value);
        Assert.Equal("  2/  2", before.Rows.Single(r => r.Label == "在任").Value);
        Assert.True(session.Develop(new(5, 15, [1], DomesticActivity.Commerce)).Accepted);
        var after = BuildingHoverInfo.Resolve(session.View, texts, 15)!;
        Assert.Equal("  1/  2", after.Rows.Single(r => r.Label == "在任").Value);
        Assert.NotEqual(before.Rows.Single(r => r.Label == "收益").Value, after.Rows.Single(r => r.Label == "收益").Value);
        Assert.Equal("  2/  2", before.Rows.Single(r => r.Label == "在任").Value);
        var gate = BuildingHoverInfo.Resolve(session.View, texts, 99)!;
        Assert.Equal(7, gate.Rows.Count); Assert.Empty(gate.Icons);
        Assert.Equal("----", gate.Rows.Single(r => r.Label == "士气").Value);
        Assert.Equal("  0/  0", gate.Rows.Single(r => r.Label == "在任").Value);
        Assert.Null(BuildingHoverInfo.Resolve(session.View, texts, 999));
    }
}
