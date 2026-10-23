using System.Text.Json;
using San9.Application.Presentation;
using San9.Content.Packages;
using San9.Content.Text;

namespace San9.Application.Tests;

public sealed class ScenarioForceSelectionTests
{
    [Fact]
    public void SelectedScenarioDetailsFollowAuthoredContentWithoutStartingOrMutatingIt()
    {
        var scenario = G01ScenarioTests.Fixture();
        var text = new TextCatalog(TextCatalog.CurrentSchemaVersion, new()
        {
            ["scenario.title"] = "真实标题", ["force.1"] = "甲", ["force.2"] = "乙",
            ["officer.1"] = "甲君", ["officer.4"] = "乙君"
        });
        Dictionary<string, string> illustrations = new() { ["catalog-person-1"] = "first", ["catalog-person-4"] = "second" };
        string before = JsonSerializer.Serialize(scenario, ScenarioContentFiles.DataJsonOptions);
        var selection = ScenarioForceSelection.Read(scenario, text, illustrations);
        var first = selection.Forces.Single(f => f.Id == 1);
        Assert.Equal("真实标题", selection.Title);
        Assert.Equal("甲", first.Name);
        Assert.Equal("first", first.Portrait);
        Assert.Equal(100, first.Soldiers);
        Assert.Equal(1000, first.Outlook.Money.Balance);
        Assert.Equal(before, JsonSerializer.Serialize(scenario, ScenarioContentFiles.DataJsonOptions));

        var changed = scenario with
        {
            Settlements = scenario.Settlements.Select(s => s.Id == 1 ? s with { Soldiers = 777, Population = 500 } : s).ToArray(),
            Information = scenario.Information with
            {
                Corps = scenario.Information.Corps.Select(c => c.OwnerId == 1 ? c with { Treasury = new(4321, 2000) } : c).ToArray()
            }
        };
        first = ScenarioForceSelection.Read(changed, text, illustrations).Forces.Single(f => f.Id == 1);
        Assert.Equal(777, first.Soldiers);
        Assert.Equal(500, first.Population);
        Assert.Equal(4321, first.Outlook.Money.Balance);

        var reserved = scenario with
        {
            Forces = scenario.Forces.Select(f => f with { IsDormant = true }).ToArray(),
            Settlements = scenario.Settlements.Select(s => s with { IsDormant = true }).ToArray(),
            Officers = scenario.Officers.Select(o => o with { State = o.State with { IsDormant = true } }).ToArray()
        };
        first = ScenarioForceSelection.Read(reserved, text, illustrations).Forces.Single(f => f.Id == 1);
        var active = selection.Forces.Single(f => f.Id == 1);
        Assert.True(active.Outlook.Money.Expenditure > 0);
        Assert.Equal(active.Outlook, first.Outlook);
        Assert.Equal(active.Officers, first.Officers);
        Assert.Equal(active.Soldiers, first.Soldiers);
        Assert.All(reserved.Officers, o => Assert.True(o.State.IsDormant));
    }
}
