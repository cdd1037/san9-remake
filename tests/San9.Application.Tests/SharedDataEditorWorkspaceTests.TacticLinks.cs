using System.Text.Json;
using San9.Application.Authoring;
using San9.Content.Packages;
using San9.Content.Scenarios;

namespace San9.Application.Tests;

public sealed partial class SharedDataEditorWorkspaceTests
{
    private async Task CreateTacticLinkPeople()
    {
        await CreatePeopleAndTactics();
        string path = Path.Combine(_root, "shared/officers.json");
        var people = JsonSerializer.Deserialize<OfficerDefaultsCatalog>(await File.ReadAllTextAsync(path), ScenarioContentFiles.DataJsonOptions)!;
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(people with
        { Officers = [.. people.Officers, people.Officers[0] with { Id = 9002, Name = "乙将" }], NextOfficerId = 9003 }, ScenarioContentFiles.DataJsonOptions));
        foreach (string id in new[] { "a", "private" })
        {
            path = Path.Combine(_root, id, "scenario.json");
            var document = JsonSerializer.Deserialize<ScenarioDocument>(await File.ReadAllTextAsync(path), ScenarioContentFiles.DataJsonOptions)!;
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(document with
            { Officers = [.. document.Officers, document.Officers[0] with { Id = 9002 }] }, ScenarioContentFiles.DataJsonOptions));
        }
        path = Path.Combine(_root, "shared/definitions.json");
        var definitions = JsonSerializer.Deserialize<ScenarioDefinitions>(await File.ReadAllTextAsync(path), ScenarioContentFiles.DataJsonOptions)!;
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(definitions with { Military = definitions.Military! with
        { LinkRules = definitions.Military.LinkRules with { RelationshipsEnabled = true } } }, ScenarioContentFiles.DataJsonOptions));
    }

    [Fact]
    public async Task PairEditingIsSymmetricAndRoundTripsToRuntimeWithoutChangingOtherLinkRules()
    {
        await CreateTacticLinkPeople();
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        var original = workspace.Definitions.Military!.LinkRules;
        Assert.Throws<InvalidDataException>(() => workspace.AddTacticLinkPair(9001, 9001, 20));
        Assert.Throws<InvalidDataException>(() => workspace.AddTacticLinkPair(9001, 9999, 20));
        Assert.Throws<ArgumentOutOfRangeException>(() => workspace.AddTacticLinkPair(9001, 9002, 101));
        workspace.AddTacticLinkPair(9002, 9001, 35);
        Assert.Throws<InvalidDataException>(() => workspace.AddTacticLinkPair(9001, 9002, 40));
        var pair = Assert.Single(workspace.TacticLinkPairs);
        Assert.Equal(9001, pair.FirstId);
        workspace.SetTacticLinkPair(pair with { Bonus = 45, SuppressesSiblingBonus = true });
        await workspace.SaveAsync();
        var reopened = await SharedDataEditorWorkspace.OpenAsync(_root);
        var scenario = await ScenarioContentFiles.LoadScenarioAsync(Path.Combine(_root, "a"));
        Assert.Equal(Assert.Single(reopened.TacticLinkPairs), Assert.Single(scenario.Military!.LinkRules.Pairs));
        var withoutPartner = await ScenarioContentFiles.LoadScenarioAsync(Path.Combine(_root, "b"));
        Assert.DoesNotContain(withoutPartner.Officers, o => o.State.Id == 9002);
        Assert.Equal(pair.FirstId, Assert.Single(withoutPartner.Military!.LinkRules.Pairs).FirstId);
        var first = scenario.Officers.Single(o => o.State.Id == 9001).State;
        var second = scenario.Officers.Single(o => o.State.Id == 9002).State;
        Assert.Equal((scenario.Military.LinkRules with { Pairs = [] }).Chance(first, second, 2) + 45,
            scenario.Military.LinkRules.Chance(first, second, 2));
        Assert.Equal(original with { Pairs = reopened.Definitions.Military!.LinkRules.Pairs }, reopened.Definitions.Military.LinkRules);
        reopened.RemoveTacticLinkPair(9002, 9001);
        await reopened.SaveAsync();
        Assert.Empty((await SharedDataEditorWorkspace.OpenAsync(_root)).TacticLinkPairs);
    }

    [Fact]
    public async Task PairValuesSaveLocallyButRemovingAReferenceStillReadsCurrentWorlds()
    {
        await CreateTacticLinkPeople();
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        workspace.AddTacticLinkPair(9001, 9002, 20);
        await workspace.SaveAsync();
        workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, "a/scenario.json"), "unrelated world");
        await File.WriteAllTextAsync(Path.Combine(_root, "shared/geography.json"), "unrelated geography");
        workspace.SetTacticLinkPair(workspace.TacticLinkPairs[0] with { Bonus = 55, SuppressesSiblingBonus = true });
        await workspace.SaveAsync();
        var reopened = await SharedDataEditorWorkspace.OpenAsync(_root);
        Assert.Equal(55, reopened.TacticLinkPairs[0].Bonus);
        Assert.True(reopened.TacticLinkPairs[0].SuppressesSiblingBonus);
        reopened.RemoveTacticLinkPair(9001, 9002);
        await Assert.ThrowsAsync<JsonException>(() => reopened.SaveAsync());
        Assert.True(reopened.HasChanges);
    }

    [Fact]
    public async Task PairReferencesProtectCreatedOfficersFromDeletion()
    {
        await CreatePeopleAndTactics();
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        var created = await workspace.CreateOfficerAsync();
        workspace.AddTacticLinkPair(9001, created.Id, 20);
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => workspace.DeleteOfficerAsync(created.Id));
        Assert.Contains("连携关系", failure.Message);
        Assert.Single(workspace.TacticLinkPairs);
    }
}
