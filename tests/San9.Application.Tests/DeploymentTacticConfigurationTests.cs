using System.Collections.Immutable;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed class DeploymentTacticConfigurationTests
{
    private static StrategySnapshot View(ImmutableArray<int>? favored = null, bool configureOnSelection = false, bool mixed = false) => DeploymentDraftTests.View(world => world with
    {
        Simulation = world.Simulation with { NewGame = world.Simulation.NewGame with { MixedTroopsEnabled = mixed } },
        Forces = world.Forces.Select(f => f.Id == 1 ? f with { ProvidedTactics = [6201] } : f).ToArray(),
        Officers = world.Officers.Select(o => o.Id switch
        {
            1 => o with { BaseAbilities = new(10, 100, 50, 80),
                LearnedTactics = [6000, 6101, 6102, 6103, 6200, 6300, 6400, 6500, 6501, 6502] },
            2 => o with { LearnedTactics = [6000], EquippedTacticId = 6000 },
            _ => o with { LearnedTactics = [], EquippedTacticId = null }
        }).ToArray()
    }, configure: definition =>
    {
        TacticRule Martial(int id, int category, int power, params int[] formations) =>
            new(id, category, TacticEffect.Martial, power, 1, 0, true, false, formations.ToImmutableArray());
        TacticRule[] additions = [Martial(6101, 0, 20, 100, 900), Martial(6102, 0, 20, 100, 900),
            Martial(6103, 0, 80, 100), Martial(6200, 1, 30, 100, 900), Martial(6201, 1, 40, 100, 900),
            Martial(6300, 2, 30, 100, 900), Martial(6400, 3, 30, 100, 900),
            new(6500, 7, TacticEffect.Illusion, 2, 1, 0, true, false, [100, 900]),
            new(6501, 8, TacticEffect.Heal, 7, 1, 0, true, false, [100, 900]),
            new(6502, 8, TacticEffect.Sorcery, 1, 1, 0, true, false, [100, 900])];
        return definition with
        {
            Military = definition.Military! with
            {
                TroopTypes = mixed ? [new(1, "弓兵", 2,
                    new Dictionary<int, int> { [6101] = 1, [6103] = 2 }.ToImmutableDictionary())] : [],
                Formations = definition.Military.Formations.Select(f => f with
                { Tactics = f.Id == 200 ? null : new(15, 2, favored ?? [1], []) }).ToImmutableArray(),
                Tactics = definition.Military.Tactics.AddRange(additions)
            },
            Information = definition.Information with
            { Tactics = [.. definition.Information.Tactics, .. additions.Select(t => new TacticCatalogDefinition(t.Id, "tactic-" + t.Id))] }
        };
    }, companion: CompanionSettings.Default with { ConfigureTacticsOnFormationSelection = configureOnSelection });

    [Fact]
    public void MixedTacticPreviewAndRecommendationsUseTheSelectedDetachmentLevel()
    {
        var draft = new DeploymentDraft(View(mixed: true), 1);
        Assert.True(draft.SetMembers([1]));
        Assert.True(draft.SetFormation(100, false));
        Assert.True(draft.SetDetachments(ImmutableDictionary<int, DetachmentState>.Empty.Add(1, new(1, 1, draft.Soldiers))));
        Assert.False(draft.SetTactic(1, 6103));
        Assert.DoesNotContain(draft.SelectableTactics(1), t => t.Id == 6103);
        Assert.Contains(draft.SelectableTactics(1), t => t.Id == 6500);
        draft.ConfigureTactics(DeploymentTacticMode.Infantry);
        Assert.Equal(6101, draft.Tactics[1]);
        Assert.True(draft.IsTacticCompatible(1));
        Assert.True(draft.SetDetachments(ImmutableDictionary<int, DetachmentState>.Empty.Add(1, new(1, 2, draft.Soldiers))));
        draft.ConfigureTactics(DeploymentTacticMode.Infantry);
        Assert.Equal(6103, draft.Tactics[1]);
        Assert.True(draft.IsTacticCompatible(1));
        Assert.True(draft.SetDetachments(ImmutableDictionary<int, DetachmentState>.Empty.Add(1, new(1, 1, draft.Soldiers))));
        Assert.Null(draft.Tactics[1]);
    }

    [Fact]
    public void ChangingMembersPreservesRemainingDetachmentEquipment()
    {
        var draft = new DeploymentDraft(View(mixed: true), 1);
        Assert.True(draft.SetMembers([1]));
        Assert.True(draft.SetDetachments(ImmutableDictionary<int, DetachmentState>.Empty.Add(1, new(1, 2, draft.Soldiers))));
        Assert.True(draft.SetMembers([1, 2]));
        Assert.Equal(2, draft.Detachments[1].Level);
        Assert.Equal(1, draft.Detachments[2].Level);
        Assert.Equal((long)draft.Soldiers, draft.Detachments.Values.Sum(detachment => (long)detachment.Soldiers));
        Assert.True(draft.SetMembers([1]));
        Assert.Equal(2, draft.Detachments[1].Level);
        Assert.Equal(draft.Soldiers, draft.Detachments[1].Soldiers);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LandFormationSelectionUsesTheOptionalExistingConfigurationRule(bool enabled)
    {
        var view = View(configureOnSelection: enabled);
        var draft = new DeploymentDraft(view, 1);
        Assert.True(draft.SetMembers([1, 2]));
        Assert.Equal(6000, draft.Tactics[1]);
        Assert.True(draft.SetFormation(100, false));
        if (enabled)
        {
            var explicitConfiguration = new DeploymentDraft(View(), 1);
            explicitConfiguration.SetMembers([1, 2]);
            explicitConfiguration.SetFormation(100, false);
            explicitConfiguration.ConfigureTactics();
            Assert.Equal(explicitConfiguration.Tactics.OrderBy(p => p.Key), draft.Tactics.OrderBy(p => p.Key));
        }
        else Assert.Equal(6000, draft.Tactics[1]);
        // Confirming the current land formation is still an explicit selection.
        Assert.True(draft.SetTactic(1, 6501));
        Assert.True(draft.SetFormation(100, false));
        Assert.Equal(enabled ? 6201 : 6501, draft.Tactics[1]);
        Assert.Equal(6000, view.World.Officers.Single(o => o.Id == 1).EquippedTacticId);
    }

    [Fact]
    public void ShipsCrewAndPresetsPreserveManualTacticsAndFailedSelectionIsAtomic()
    {
        var draft = new DeploymentDraft(View(configureOnSelection: true), 1);
        draft.SetMembers([1]); draft.SetTactic(1, 6501);
        Assert.True(draft.SetFormation(700, true)); Assert.Equal(6501, draft.Tactics[1]);
        Assert.True(draft.SetMembers([1, 2])); Assert.Equal(6501, draft.Tactics[1]);
        var before = draft.Tactics;
        Assert.False(draft.SetFormation(99999, false));
        Assert.Equal(900, draft.FormationId); Assert.Same(before, draft.Tactics);
        var preset = draft.CreatePreset(Guid.NewGuid(), "保留治疗", DeploymentSoldierMode.Fixed);
        draft.MarkPresetSaved(preset);
        draft.ConfigureTactics();
        Assert.Equal(6201, draft.Tactics[1]); Assert.True(draft.HasPresetChanges);
        var configured = draft.CreatePreset(Guid.NewGuid(), "骑兵队", DeploymentSoldierMode.Fixed);
        Assert.True(draft.ApplyPreset(preset).CanApply);
        Assert.Equal(6501, draft.Tactics[1]); Assert.False(draft.HasPresetChanges);
        Assert.True(draft.ApplyPreset(configured).CanApply);
        Assert.Equal(6201, draft.Tactics[1]); Assert.False(draft.HasPresetChanges);
    }

    [Theory]
    [InlineData(DeploymentTacticMode.Formation, 6201)]
    [InlineData(DeploymentTacticMode.Infantry, 6101)]
    [InlineData(DeploymentTacticMode.Cavalry, 6201)]
    [InlineData(DeploymentTacticMode.MountedArchery, 6300)]
    [InlineData(DeploymentTacticMode.Crossbow, 6400)]
    [InlineData(DeploymentTacticMode.Intelligence, 6500)]
    public void ExplicitModesRespectCategoryFormationAndPersonalSources(DeploymentTacticMode mode, int expected)
    {
        var draft = new DeploymentDraft(View(), 1);
        Assert.True(draft.SetMembers([1, 2, 3]));
        draft.ConfigureTactics(mode);
        Assert.Equal(expected, draft.Tactics[1]);
        bool cavalryAvailable = mode is DeploymentTacticMode.Formation or DeploymentTacticMode.Cavalry;
        Assert.Equal(cavalryAvailable ? 6201 : 6000, draft.Tactics[2]);
        Assert.Equal(cavalryAvailable ? 6201 : (int?)null, draft.Tactics[3]);
    }

    [Fact]
    public void TiesKeepEquipmentThenUseStableIdentityAndCurrentMartialCategory()
    {
        var draft = new DeploymentDraft(View(), 1); draft.SetMembers([1]);
        draft.SetTactic(1, 6102);
        draft.ConfigureTactics(DeploymentTacticMode.Infantry);
        Assert.Equal(6102, draft.Tactics[1]);
        draft.SetTactic(1, 6000);
        draft.ConfigureTactics(DeploymentTacticMode.Infantry);
        Assert.Equal(6101, draft.Tactics[1]);
        draft.ConfigureTactics(DeploymentTacticMode.Infantry);
        Assert.Equal(6101, draft.Tactics[1]);
        var noAdvantages = new DeploymentDraft(View([]), 1); noAdvantages.SetMembers([1]);
        noAdvantages.SetFormation(100, false);
        noAdvantages.SetTactic(1, 6200);
        noAdvantages.ConfigureTactics();
        // Retain the cavalry category before comparing power, then choose its stronger tactic.
        Assert.Equal(6201, noAdvantages.Tactics[1]);
    }

    [Fact]
    public void FormationAdvantagesComeFromContentAndFallbackUsesOnlyCompatibleChoices()
    {
        var intelligence = new DeploymentDraft(View([7]), 1); intelligence.SetMembers([1]);
        intelligence.ConfigureTactics(); Assert.Equal(6500, intelligence.Tactics[1]);
        var noAdvantages = new DeploymentDraft(View([]), 1); noAdvantages.SetMembers([1]);
        noAdvantages.ConfigureTactics(); Assert.Equal(6101, noAdvantages.Tactics[1]);
        Assert.True(noAdvantages.SetFormation(100, false));
        Assert.Equal(6101, noAdvantages.Tactics[1]);
        noAdvantages.ConfigureTactics(); Assert.Equal(6103, noAdvantages.Tactics[1]);
        // A formation without tactic opportunities must not manufacture an equipment recommendation.
        var siege = new DeploymentDraft(DeploymentDraftTests.View(), 1); siege.SetMembers([1, 2]);
        siege.SetFormation(200, false);
        var before = siege.Tactics;
        siege.ConfigureTactics();
        Assert.Equal(before.OrderBy(p => p.Key), siege.Tactics.OrderBy(p => p.Key));
        Assert.Equal(6000, siege.Tactics[1]);
    }

    [Fact]
    public void ConfigurationOnlyChangesTacticsAndLaterManualEditsRemainExplicit()
    {
        var view = View();
        var draft = new DeploymentDraft(view, 1); draft.SetMembers([1, 2]);
        draft.SetSoldiers(123); draft.SetFormation(100, false);
        var position = draft.Positions[2]; draft.Move(1, position);
        var before = draft.CreatePreset(Guid.NewGuid(), "出征", DeploymentSoldierMode.Fixed);
        draft.ConfigureTactics();
        Assert.Equal(6201, draft.Tactics[1]);
        var after = draft.CreatePreset(before.Id, before.Name, before.SoldierMode);
        Assert.Equal(before with { Members = after.Members }, after);
        Assert.Equal(before.Members.Select(m => (m.OfficerId, m.Position)), after.Members.Select(m => (m.OfficerId, m.Position)));
        Assert.Null(draft.SourcePresetId);
        Assert.Equal(6000, view.World.Officers.Single(o => o.Id == 1).EquippedTacticId);
        draft.SetTactic(1, 6103);
        draft.SetFormation(900, false); Assert.False(draft.IsTacticCompatible(1));
        draft.SetFormation(100, false); Assert.True(draft.IsTacticCompatible(1));
        draft.SetTactic(1, null);
        draft.SetFormation(900, false); Assert.Null(draft.Tactics[1]);
        draft.ConfigureTactics();
        draft.SetMembers([1]);
        Assert.Equal(6201, draft.Tactics[1]);
        Assert.Single(draft.Tactics);
    }
}
