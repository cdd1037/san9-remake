using System.Collections.Immutable;
using San9.Application.Persistence;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Application.Tests;

public sealed class DeploymentPresetTests
{
    [Fact]
    public void ApplyingRestoresCompleteConfigurationWithoutResourcesAndRecalculatesCapacity()
    {
        var view = DeploymentDraftTests.View();
        var original = new DeploymentDraft(view, 1);
        Assert.True(original.SetMembers([1, 2, 3]));
        Assert.True(original.SetFormation(200, false));
        Assert.True(original.SetSoldiers(12000));
        Assert.True(original.Move(1, original.Slots[^1]));
        var fixedPreset = original.CreatePreset(Guid.NewGuid(), "突击队", DeploymentSoldierMode.Fixed);
        var target = new DeploymentDraft(view, 1);
        Assert.True(target.SetMembers([3]));
        Assert.True(target.ApplyPreset(fixedPreset).CanApply);
        Assert.Equal(original.Members.ToArray(), target.Members.ToArray());
        Assert.Equal(original.Positions.OrderBy(p => p.Key), target.Positions.OrderBy(p => p.Key));
        Assert.Equal(original.Tactics.OrderBy(p => p.Key), target.Tactics.OrderBy(p => p.Key));
        Assert.Equal(200, target.FormationId); Assert.Equal(700, target.NavalFormationId);
        Assert.Equal(12000u, target.Soldiers);
        var reduced = DeploymentDraftTests.View(world => world with
        {
            Settlements = world.Settlements.Select(s => s.Id == 1 ? s with { Soldiers = 6000 } : s).ToArray()
        });
        var limited = new DeploymentDraft(reduced, 1);
        limited.SetMembers([3]); limited.SetSoldiers(123);
        Assert.True(limited.CheckPreset(fixedPreset).CanApply);
        Assert.Equal(new[] { 3 }, limited.Members.ToArray()); Assert.Equal(123u, limited.Soldiers);
        Assert.True(limited.ApplyPreset(fixedPreset).CanApply);
        Assert.Equal(6000u, limited.Soldiers); Assert.Equal(12000u, fixedPreset.Soldiers);
        var maximum = original.CreatePreset(Guid.NewGuid(), "满兵队", DeploymentSoldierMode.Maximum);
        Assert.Equal(0u, maximum.Soldiers);
        Assert.True(limited.ApplyPreset(maximum).CanApply); Assert.Equal(6000u, limited.Soldiers);
        StateAssert.Equal(view.World, original.View.World);
        Assert.Empty(view.World.Units);
    }

    [Theory]
    [InlineData("dead", DeploymentPresetFailure.OfficerDead)]
    [InlineData("captive", DeploymentPresetFailure.OfficerCaptured)]
    [InlineData("transferred", DeploymentPresetFailure.OfficerTransferred)]
    [InlineData("elsewhere", DeploymentPresetFailure.OfficerElsewhere)]
    public void UnavailableCrewExplainsItsAbsenceAndIsOmittedWithoutChangingThePreset(string change, DeploymentPresetFailure reason)
    {
        var original = new DeploymentDraft(DeploymentDraftTests.View(), 1);
        original.SetMembers([1, 2, 3]);
        var preset = original.CreatePreset(Guid.NewGuid(), "测试", DeploymentSoldierMode.Maximum);
        var changed = DeploymentDraftTests.View(world => world with
        {
            Officers = world.Officers.Select(o => o.Id != 2 ? o : change switch
            {
                "dead" => o with { Status = OfficerStatus.Dead },
                "captive" => o with { Status = OfficerStatus.Captive },
                "transferred" => o with { OwnerId = 2 },
                "elsewhere" => o with { SettlementId = 2 },
                _ => o
            }).ToArray()
        });
        var target = new DeploymentDraft(changed, 1);
        var check = target.ApplyPreset(preset);
        Assert.True(check.CanApply);
        Assert.Equal(new DeploymentPresetMissingMember(2, reason), Assert.Single(check.MissingMembers));
        Assert.Equal(new[] { 1, 3 }, target.Members.ToArray());
        Assert.Equal(3, preset.Members.Length); Assert.False(target.HasPresetChanges);
    }

    [Fact]
    public void MissingCrewRecalculatesCommanderAndCapsTroopsWithoutChangingThePreset()
    {
        var original = new DeploymentDraft(DeploymentDraftTests.View(), 1);
        original.SetMembers([1, 2, 3]); original.SetSoldiers(15000);
        original.Move(3, original.Slots[^1]);
        var preset = original.CreatePreset(Guid.NewGuid(), "缺员队", DeploymentSoldierMode.Fixed);
        var changed = DeploymentDraftTests.View(world => world with
        {
            Officers = world.Officers.Select(o => o.Id is 1 or 2 ? o with { Acted = true } : o).ToArray()
        });
        var target = new DeploymentDraft(changed, 1);
        target.SetMembers([3]); target.SetSoldiers(123);
        var before = changed.World;
        var check = target.CheckPreset(preset);
        Assert.Equal(3, target.PresetCommanderId(preset));
        Assert.True(check.CanApply);
        Assert.Equal(new[] { 1, 2 }, check.MissingMembers.Select(m => m.OfficerId));
        Assert.All(check.MissingMembers, m => Assert.Equal(DeploymentPresetFailure.OfficerBusy, m.Reason));
        Assert.Equal(123u, target.Soldiers);
        Assert.True(target.ApplyPreset(preset).CanApply);
        Assert.False(target.HasPresetChanges); Assert.Equal(preset.Id, target.SourcePresetId);
        Assert.Equal(3, target.CommanderId); Assert.Equal(new[] { 3 }, target.Members.ToArray());
        Assert.Equal(preset.Members.Single(m => m.OfficerId == 3).Position, target.Positions[3]);
        Assert.Equal(10000u, target.Soldiers); Assert.Single(target.Tactics);
        Assert.Equal(3, preset.Members.Length); Assert.Equal(15000u, preset.Soldiers);
        StateAssert.Equal(before, target.View.World);
    }

    [Theory]
    [InlineData("technology", DeploymentPresetFailure.FormationUnavailable)]
    [InlineData("missing-technology", DeploymentPresetFailure.FormationUnavailable)]
    [InlineData("tactic", DeploymentPresetFailure.TacticUnavailable)]
    [InlineData("money", DeploymentPresetFailure.InsufficientMoney)]
    [InlineData("empty", DeploymentPresetFailure.NoAvailableMembers)]
    public void InvalidRemainingConfigurationNeverPartiallyReplacesTheDraft(string change, DeploymentPresetFailure failure)
    {
        var original = new DeploymentDraft(DeploymentDraftTests.View(), 1);
        original.SetMembers([1, 2]); original.SetFormation(200, false);
        var preset = original.CreatePreset(Guid.NewGuid(), "攻城队", DeploymentSoldierMode.Maximum);
        var changed = DeploymentDraftTests.View(world => world with
        {
            Officers = world.Officers.Select(o => change switch
            {
                "technology" when o.Id == 2 => o with { LearnedTactics = [] },
                "missing-technology" when o.Id == 2 => o with { Acted = true },
                "tactic" when o.Id == 1 => o with { LearnedTactics = [] },
                "empty" when o.Id is 1 or 2 => o with { Acted = true },
                _ => o
            }).ToArray(),
            Corps = change == "money" ? world.Corps.Select(c => c.OwnerId == 1 ? c with { Treasury = new(0, 0) } : c).ToArray() : world.Corps
        });
        var target = new DeploymentDraft(changed, 1);
        target.SetMembers([3]); target.SetSoldiers(456);
        var before = target.CreatePreset(Guid.NewGuid(), "不变", DeploymentSoldierMode.Fixed);
        var check = target.ApplyPreset(preset);
        Assert.Equal(failure, check.Failure);
        Assert.Equal(before.FormationId, target.FormationId); Assert.Equal(before.NavalFormationId, target.NavalFormationId);
        Assert.Equal(before.Soldiers, target.Soldiers);
        Assert.Equal(before.Members.ToArray(), target.Members.Select(id => new DeploymentPresetMember(id, target.Positions[id], target.Tactics[id])).ToArray());
        Assert.Null(target.SourcePresetId);
    }

    [Fact]
    public void PresetSourceTracksActualConfigurationAndSurvivesFailedApplication()
    {
        var draft = new DeploymentDraft(DeploymentDraftTests.View(), 1);
        Assert.Null(draft.SourcePresetId); Assert.False(draft.HasPresetChanges);
        draft.SetMembers([1, 2, 3]);
        var preset = draft.CreatePreset(Guid.NewGuid(), "来源队", DeploymentSoldierMode.Fixed);
        Assert.True(draft.ApplyPreset(preset).CanApply);
        Assert.Equal(preset.Id, draft.SourcePresetId); Assert.False(draft.HasPresetChanges);
        uint soldiers = draft.Soldiers;
        draft.SetSoldiers(soldiers); draft.Move(1, draft.Positions[1]);
        Assert.False(draft.HasPresetChanges);
        draft.SetSoldiers(soldiers - 1); Assert.True(draft.HasPresetChanges);
        draft.SetSoldiers(soldiers); Assert.False(draft.HasPresetChanges);
        draft.SetTactic(1, null); Assert.True(draft.HasPresetChanges);
        draft.SetTactic(1, 6000); Assert.False(draft.HasPresetChanges);
        var origin = draft.Positions[1]; var other = draft.Positions[2];
        draft.Move(1, other); Assert.True(draft.HasPresetChanges);
        draft.Move(1, origin); Assert.False(draft.HasPresetChanges);
        draft.SetFormation(100, false); Assert.True(draft.HasPresetChanges);
        Assert.True(draft.ApplyPreset(preset).CanApply); Assert.False(draft.HasPresetChanges);
        draft.SetMembers([1, 2]); Assert.True(draft.HasPresetChanges);
        Assert.False(draft.ApplyPreset(preset with { Id = Guid.NewGuid(), FormationId = int.MaxValue }).CanApply);
        Assert.Equal(preset.Id, draft.SourcePresetId); Assert.True(draft.HasPresetChanges);
    }

    [Theory]
    [InlineData(DeploymentSoldierMode.Fixed)]
    [InlineData(DeploymentSoldierMode.Maximum)]
    public void ExplicitUpdateReplacesOnlyTheSourceAndBecomesCleanAfterSuccessfulCommit(DeploymentSoldierMode mode)
    {
        var session = GameSession.Start(Fixture(), 190);
        var draft = new DeploymentDraft(session.View, 1);
        draft.SetMembers([1, 2]);
        var source = draft.CreatePreset(Guid.NewGuid(), "保持名称", mode);
        Assert.Equal(SessionDisposition.Accepted, session.Apply(session.Request(new SaveDeploymentPresetOperation(source))).Disposition);
        Assert.True(draft.ApplyPreset(source).CanApply);
        draft.SetMembers([1]);
        Assert.True(draft.HasPresetChanges);
        Assert.Equal(2, Assert.Single(session.DeploymentPresets).Members.Length);
        Assert.Throws<ArgumentException>(() => draft.CreatePresetUpdate(source with { Id = Guid.NewGuid() }));
        var update = draft.CreatePresetUpdate(source);
        Assert.Equal(source.Id, update.Id); Assert.Equal(source.Name, update.Name); Assert.Equal(mode, update.SoldierMode);
        Assert.Equal(mode == DeploymentSoldierMode.Maximum ? 0u : draft.Soldiers, update.Soldiers);
        Assert.Single(update.Members);
        var before = session.Capture();
        Assert.Equal(SessionDisposition.Rejected, session.Apply(session.Request(new SaveDeploymentPresetOperation(update), "ai:2")).Disposition);
        StateAssert.Equal(before, session.Capture()); Assert.True(draft.HasPresetChanges);
        Assert.Equal(SessionDisposition.Accepted, session.Apply(session.Request(new SaveDeploymentPresetOperation(update))).Disposition);
        draft.MarkPresetSaved(update);
        Assert.Single(session.DeploymentPresets); Assert.Single(session.DeploymentPresets[0].Members);
        Assert.False(draft.HasPresetChanges); Assert.Equal(source.Id, draft.SourcePresetId);
        StateAssert.Equal(before.World, session.Snapshot);
    }

    [Fact]
    public void ManagementSurvivesSaveAndExplicitScriptsWithSessionOwnershipAndDirtyTracking()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 190);
        var draft = new DeploymentDraft(session.View, 1); Assert.True(draft.SetMembers([1, 2]));
        var preset = draft.CreatePreset(Guid.NewGuid(), "常用队", DeploymentSoldierMode.Maximum);
        var before = session.Capture();
        session.MarkSaved(session.NextOperationSequence);
        var request = session.Request(new SaveDeploymentPresetOperation(preset));
        Assert.Equal(SessionDisposition.Accepted, session.Apply(request).Disposition);
        Assert.True(session.HasUnsavedChanges); Assert.Same(before.World, session.Snapshot);
        Assert.Empty(before.DeploymentPresets); Assert.Single(session.Capture().DeploymentPresets);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        StateAssert.Equal(session.Capture(), restored.Capture());
        var unavailable = restored.Capture() with { World = restored.Snapshot with
        {
            Officers = restored.Snapshot.Officers.Select(o => o.Id == 2 ? o with { Acted = true } : o).ToArray()
        } };
        var busySession = SaveService.Decode(SaveService.EncodeSnapshot(unavailable), definition);
        Assert.Single(new DeploymentDraft(busySession.View, 1).CheckPreset(busySession.DeploymentPresets[0]).MissingMembers);
        var renamed = preset with { Name = "改名队" };
        var renameRequest = restored.Request(new SaveDeploymentPresetOperation(renamed));
        Assert.Equal(SessionDisposition.Accepted, restored.Apply(renameRequest).Disposition);
        Assert.Equal("改名队", Assert.Single(restored.DeploymentPresets).Name);
        var invalid = renamed with { Members = renamed.Members.Add(renamed.Members[0]) };
        var unchanged = restored.Capture();
        Assert.Equal(SessionDisposition.Rejected, restored.Apply(restored.Request(new SaveDeploymentPresetOperation(invalid))).Disposition);
        StateAssert.Equal(unchanged, restored.Capture());
        var deleteRequest = restored.Request(new DeleteDeploymentPresetOperation(preset.Id));
        Assert.Equal(SessionDisposition.Accepted, restored.Apply(deleteRequest).Disposition);
        Assert.Empty(restored.DeploymentPresets);

        string path = Path.Combine(Path.GetTempPath(), $"san9-deployment-presets-{Guid.NewGuid():N}.jsonl");
        try
        {
            var scripted = GameSession.Start(definition, 190);
            CommandLogService.Write(path, CommandLogService.CreateHeader(scripted), [request, renameRequest, deleteRequest]);
            Assert.Null(CommandLogService.Replay(path, scripted).FirstFailure);
            StateAssert.Equal(restored.Capture(), scripted.Capture());
        }
        finally { File.Delete(path); }

    }

    [Fact]
    public void UnavailableFormationInStoredPreferenceDoesNotPreventLoadOrManagement()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 190);
        var draft = new DeploymentDraft(session.View, 1);
        Assert.True(draft.SetMembers([1, 2]));
        var preset = draft.CreatePreset(Guid.NewGuid(), "旧编队", DeploymentSoldierMode.Maximum);
        Assert.Equal(SessionDisposition.Accepted, session.Apply(session.Request(new SaveDeploymentPresetOperation(preset))).Disposition);
        // The current formation still exists for normal deployment, but its available positions shrank.
        var military = definition.Military!;
        var changed = definition with { Military = military with
        {
            Formations = military.Formations.Select(f => f with { OfficerCapacity = 1 }).ToImmutableArray()
        } };
        var restored = SaveService.Load(SaveService.Encode(session), changed);
        var saved = Assert.Single(restored.DeploymentPresets);
        Assert.Equal(preset.Id, saved.Id);
        Assert.Equal(DeploymentPresetFailure.InvalidPreset, new DeploymentDraft(restored.View, 1).CheckPreset(saved).Failure);
        Assert.Equal(SessionDisposition.Accepted, restored.Apply(restored.Request(new SaveDeploymentPresetOperation(saved with { Name = "旧方案" }))).Disposition);
        Assert.Equal(SessionDisposition.Accepted, restored.Apply(restored.Request(new DeleteDeploymentPresetOperation(saved.Id))).Disposition);
    }

    private static ScenarioInitialState Fixture()
    {
        var basis = G01ScenarioTests.Fixture();
        return basis with
        {
            Military = new(4, 4, Enumerable.Repeat(0, 16).ToImmutableArray(), [new(0, 1, null)],
                [new(900, 10, 10, 100000, 5, new(1, 1), TravelMode.Land)],
                basis.Settlements.Select(s => new BattlefieldPlace(s.Id, s.Position, new(1, 1))).ToImmutableArray()) { DefaultDeploymentFormationId = 900 },
            Information = basis.Information with { Formations = [new(900, "formation")] }
        };
    }
}
