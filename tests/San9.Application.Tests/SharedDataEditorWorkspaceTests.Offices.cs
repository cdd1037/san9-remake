using System.Text;
using System.Text.Json;
using San9.Application.Authoring;
using San9.Application.Persistence;
using San9.Application.Presentation;
using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Application.Tests;

public sealed partial class SharedDataEditorWorkspaceTests
{
    [Fact]
    public async Task AuthoredOfficesAreSharedAndCopiesKeepRulesWithoutReplacingOriginals()
    {
        await Create();
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        byte[] world = await File.ReadAllBytesAsync(Path.Combine(_root, "a/scenario.json"));
        var original = workspace.GetOffice(9000) with { RequiredRankId = 8000, AbilityBonuses = new(1, 2, 3, 4),
            AppointmentGroup = OfficeAppointmentGroup.Civil, GovernorPriority = 8, FillBeforeOrdinaryOffices = true };
        workspace.SetOffice(original);
        var created = await workspace.CreateOfficeAsync();
        var copy = await workspace.CreateOfficeAsync(original.Id);
        Assert.Equal(original with { Id = copy.Id, Name = original.Name + "副本", GovernorPriority = copy.GovernorPriority }, copy);
        Assert.True(workspace.IsCreatedOffice(created.Id));
        Assert.False(workspace.IsCreatedOffice(original.Id));
        Assert.True(workspace.IsOfficeChanged(created.Id));
        Assert.Equal(10000u, created.CommandCapacity);
        Assert.Null(created.RequiredRankId);
        await workspace.SaveAsync();
        Assert.Equal(world, await File.ReadAllBytesAsync(Path.Combine(_root, "a/scenario.json")));
        var reopened = await SharedDataEditorWorkspace.OpenAsync(_root);
        Assert.False(reopened.HasChanges);
        Assert.Equal(copy, reopened.GetOffice(copy.Id));
        Assert.True(reopened.IsCreatedOffice(copy.Id));
        await Assert.ThrowsAsync<InvalidDataException>(() => reopened.DeleteOfficeAsync(original.Id));
        foreach (string id in new[] { "a", "b", "private" })
        {
            var loaded = await ScenarioContentFiles.LoadScenarioAsync(Path.Combine(_root, id));
            Assert.Contains(loaded.Information.Offices, o => o.Id == copy.Id);
        }
    }

    [Fact]
    public async Task CreatedOfficeCanBeAppointedWithItsRankGateAndSurviveSaveWithoutAuthoringMetadata()
    {
        await Create();
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        var office = await workspace.CreateOfficeAsync();
        workspace.SetOffice(office with { RequiredRankId = 8000, AbilityBonuses = new(0, 0, 7, 0) });
        await workspace.SaveAsync();
        var loaded = await ScenarioContentFiles.LoadScenarioAsync(Path.Combine(_root, "a"));
        var baseline = G01ScenarioTests.Fixture();
        var definition = baseline with { Information = baseline.Information with
            { Offices = loaded.Information.Offices, Ranks = loaded.Information.Ranks } };
        var session = GameSession.Start(definition, 190);
        Assert.DoesNotContain(office.Id, new AppointmentDraft(session, 1, "rank").OfficePlan!.AvailableOffices);
        definition = definition with { Forces = definition.Forces.Select(f => f with { RankId = 8000 }).ToArray() };
        session = GameSession.Start(definition, 190);
        Assert.Contains(office.Id, new AppointmentDraft(session, 1, "rank").OfficePlan!.AvailableOffices);
        Assert.True(session.Execute(new("player"), new AssignOfficesCommand(Guid.NewGuid(), 1, 0, [new(2, office.Id)])).Submission.Accepted);
        var person = session.Snapshot.Officers.Single(p => p.Id == 2);
        Assert.Equal(person.BaseAbilities.Politics + 7, session.View.Officers.Single(p => p.Id == 2).Abilities.Politics);
        byte[] saved = SaveService.Encode(session);
        Assert.DoesNotContain("nextOfficeId", Encoding.UTF8.GetString(saved), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(office.Id, SaveService.Decode(saved, definition).Snapshot.Officers.Single(p => p.Id == 2).OfficeId);
    }

    [Fact]
    public async Task DeletedOfficeIdsRemainReservedAcrossSavesAndRestoreUsesCreationBaseline()
    {
        await Create();
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        var office = await workspace.CreateOfficeAsync(9000);
        workspace.SetOffice(office with { Salary = 100 });
        await workspace.SaveAsync();
        workspace.RestoreOffice(office.Id);
        Assert.Equal(office, workspace.GetOffice(office.Id));
        Assert.True(workspace.IsOfficeChanged(office.Id));
        await workspace.DeleteOfficeAsync(office.Id);
        await workspace.SaveAsync();
        var reopened = await SharedDataEditorWorkspace.OpenAsync(_root);
        Assert.DoesNotContain(reopened.Offices, o => o.Id == office.Id);
        Assert.True((await reopened.CreateOfficeAsync()).Id > office.Id);
    }

    [Fact]
    public async Task OfficeDeletionRejectsInitialAssignmentsAndSaveRechecksLateAssignments()
    {
        await CreatePeopleAndTactics();
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        var office = await workspace.CreateOfficeAsync();
        await EditMilitaryWorld("b", d => d with { Officers = d.Officers.Select(p => p with { OfficeId = office.Id }).ToArray() });
        var draft = workspace.Definitions;
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => workspace.DeleteOfficeAsync(office.Id));
        Assert.Contains("剧本 b", error.Message);
        Assert.Contains("9001", error.Message);
        Assert.Same(draft, workspace.Definitions);
        await EditMilitaryWorld("b", d => d with { Officers = d.Officers.Select(p => p with { OfficeId = (int?)null }).ToArray() });
        await workspace.SaveAsync();
        await workspace.DeleteOfficeAsync(office.Id);
        byte[] saved = await File.ReadAllBytesAsync(workspace.PlayerDefinitionPath);
        await EditMilitaryWorld("b", d => d with { Officers = d.Officers.Select(p => p with { OfficeId = office.Id }).ToArray() });
        await Assert.ThrowsAsync<InvalidDataException>(() => workspace.SaveAsync());
        Assert.Equal(saved, await File.ReadAllBytesAsync(workspace.PlayerDefinitionPath));
    }

    [Fact]
    public async Task OfficeAllocationReservesPrivateDefinitionsAndInitialAssignmentReferences()
    {
        await CreatePeopleAndTactics();
        var basis = await SharedDataEditorWorkspace.OpenAsync(_root);
        await EditMilitaryWorld("private", d => d with { DefinitionOverrides = d.DefinitionOverrides with
            { Information = new(ScenarioInformationCatalog.From(basis.Definitions.Information) with { Offices = [new(20000, "剧本专属官职", 10000, 0)] }) } });
        await EditMilitaryWorld("b", d => d with { Officers = d.Officers.Select(p => p with { OfficeId = 30000 }).ToArray() });
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        Assert.True((await workspace.CreateOfficeAsync()).Id > 30000);
    }

    [Fact]
    public async Task OfficeAllocationKeepsTheBaseReservationEvenWhenThePlayerCopyOmitsIt()
    {
        await Create();
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        var baseline = workspace.Definitions with { Information = workspace.Definitions.Information with
            { Offices = [.. workspace.Offices, new(20000, "基础保留官职", 10000, 0)] }, NextOfficeId = 30000 };
        await File.WriteAllTextAsync(workspace.DefinitionPath, JsonSerializer.Serialize(baseline, ScenarioContentFiles.DataJsonOptions));
        await File.WriteAllTextAsync(workspace.PlayerDefinitionPath, JsonSerializer.Serialize(workspace.Definitions, ScenarioContentFiles.DataJsonOptions));
        workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        Assert.Equal(30000, (await workspace.CreateOfficeAsync()).Id);
    }

    [Fact]
    public async Task ExhaustedOfficeIdentitySpaceLeavesTheDraftUntouched()
    {
        await Create();
        var basis = await SharedDataEditorWorkspace.OpenAsync(_root);
        await File.WriteAllTextAsync(basis.DefinitionPath, JsonSerializer.Serialize(basis.Definitions with
            { NextOfficeId = (long)int.MaxValue + 1 }, ScenarioContentFiles.DataJsonOptions));
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        var draft = workspace.Definitions;
        await Assert.ThrowsAsync<InvalidDataException>(() => workspace.CreateOfficeAsync());
        Assert.Same(draft, workspace.Definitions);
        Assert.False(workspace.HasChanges);
    }
}
