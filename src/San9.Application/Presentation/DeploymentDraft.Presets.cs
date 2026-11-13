using System.Collections.Immutable;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Presentation;

public enum DeploymentPresetFailure
{
    None, InvalidPreset, WrongForce, OfficerDead, OfficerCaptured, OfficerTransferred,
    OfficerElsewhere, OfficerBusy, FormationUnavailable, ShipUnavailable, TacticUnavailable,
    InsufficientSoldiers, InsufficientMoney, NoAvailableMembers, MissingDetachments
}

public sealed record DeploymentPresetMissingMember(int OfficerId, DeploymentPresetFailure Reason);

public sealed record DeploymentPresetCheck(DeploymentPresetFailure Failure, int? OfficerId = null, uint MaximumSoldiers = 0)
{
    public ImmutableArray<DeploymentPresetMissingMember> MissingMembers { get; init; } = [];
    public bool HasMissingMembers => !MissingMembers.IsDefaultOrEmpty;
    public bool CanApply => Failure == DeploymentPresetFailure.None;
    public string DescribeAvailability(DeploymentPreset preset, StrategySnapshot view, TextCatalog texts)
    {
        if (!HasMissingMembers) return Describe(view, texts);
        string missing = $"{preset.Members.Length - MissingMembers.Length}/{preset.Members.Length}名武将可用\n" +
            string.Join("\n", MissingMembers.Select(m => new DeploymentPresetCheck(m.Reason, m.OfficerId).Describe(view, texts)));
        return CanApply ? missing : missing + "\n" + Describe(view, texts);
    }
    public string Describe(StrategySnapshot view, TextCatalog texts)
    {
        string person = OfficerId is int id && view.Officers.FirstOrDefault(o => o.Id == id) is { } officer
            ? texts.Resolve(officer.NameKey) : "武将";
        return Failure switch
        {
            DeploymentPresetFailure.None => "可以套用",
            DeploymentPresetFailure.WrongForce => "不属于当前势力",
            DeploymentPresetFailure.OfficerDead => $"{person}已死亡",
            DeploymentPresetFailure.OfficerCaptured => $"{person}被俘",
            DeploymentPresetFailure.OfficerTransferred => $"{person}已不属于本势力",
            DeploymentPresetFailure.OfficerElsewhere => $"{person}不在当前据点",
            DeploymentPresetFailure.OfficerBusy => $"{person}当前无法出征",
            DeploymentPresetFailure.FormationUnavailable => "当前成员无法使用保存的阵形",
            DeploymentPresetFailure.ShipUnavailable => "当前成员无法使用保存的船",
            DeploymentPresetFailure.TacticUnavailable => $"{person}无法使用保存的兵法",
            DeploymentPresetFailure.InsufficientSoldiers => $"兵力超过当前上限（{MaximumSoldiers}人）",
            DeploymentPresetFailure.InsufficientMoney => "当前军团出征资金不足",
            DeploymentPresetFailure.NoAvailableMembers => "编队当前没有可出征的成员",
            DeploymentPresetFailure.MissingDetachments => "旧编队缺少兵种和分兵配置，请重新设置后保存",
            _ => "编队配置无效"
        };
    }
}

public sealed partial class DeploymentDraft
{
    private DeploymentPreset? _presetBaseline;
    public Guid? SourcePresetId => _presetBaseline?.Id;
    public bool HasPresetChanges => _presetBaseline is { } baseline &&
        (FormationId != baseline.FormationId || NavalFormationId != baseline.NavalFormationId || NavalTrainingId != baseline.NavalTrainingId ||
         SiegeEquipmentId != baseline.SiegeEquipmentId || Soldiers != baseline.Soldiers ||
         Members.Length != baseline.Members.Length || baseline.Members.Any(m => !Members.Contains(m.OfficerId) ||
             !Positions.TryGetValue(m.OfficerId, out var position) || position != m.Position ||
             !Tactics.TryGetValue(m.OfficerId, out var tactic) || tactic != m.TacticId ||
             Detachments.GetValueOrDefault(m.OfficerId) != m.Detachment));

    public DeploymentPreset CreatePreset(Guid id, string name, DeploymentSoldierMode soldierMode)
    {
        if (!CanProceed) throw new InvalidOperationException("A complete deployment preparation is required.");
        var preset = new DeploymentPreset(id, Force.Id, name.Trim(),
            Members.Select(person => new DeploymentPresetMember(person, Positions[person], Tactics[person])
                { Detachment = Detachments.GetValueOrDefault(person) }).ToImmutableArray(),
            FormationId!.Value, NavalFormationId, soldierMode, soldierMode == DeploymentSoldierMode.Maximum ? 0 : Soldiers)
            { NavalTrainingId = NavalTrainingId, SiegeEquipmentId = SiegeEquipmentId };
        preset.Validate(View);
        return preset;
    }

    public DeploymentPreset CreatePresetUpdate(DeploymentPreset source)
    {
        if (source.Id != SourcePresetId || source.ForceId != Force.Id)
            throw new ArgumentException("The preset is not this preparation's source.", nameof(source));
        return CreatePreset(source.Id, source.Name, source.SoldierMode);
    }

    /// <summary>Tracks the effective draft after application or a successful save, including any omitted crew or capped troops.</summary>
    public void MarkPresetSaved(DeploymentPreset preset)
    {
        if (preset.ForceId != Force.Id) throw new ArgumentException("The preset belongs to another force.", nameof(preset));
        _presetBaseline = CreatePreset(preset.Id, preset.Name, DeploymentSoldierMode.Fixed);
    }

    public DeploymentPresetCheck CheckPreset(DeploymentPreset preset) => PreparePreset(preset, out _);

    public int? PresetCommanderId(DeploymentPreset preset) => Candidates
        .Where(id => preset.Members.Any(m => m.OfficerId == id)).Select(id => (int?)id).FirstOrDefault();

    /// <summary>Omit unavailable crew, cap troops, and validate the complete preparation before publishing it.</summary>
    public DeploymentPresetCheck ApplyPreset(DeploymentPreset preset)
    {
        var check = PreparePreset(preset, out var candidate);
        if (!check.CanApply) return check;
        Members = candidate!.Members; Positions = candidate.Positions; Tactics = candidate.Tactics;
        FormationId = candidate.FormationId; NavalFormationId = candidate.NavalFormationId; Soldiers = candidate.Soldiers;
        NavalTrainingId = candidate.NavalTrainingId;
        SiegeEquipmentId = candidate.SiegeEquipmentId;
        _detachments = candidate.Detachments;
        MarkPresetSaved(preset);
        return check;
    }

    private DeploymentPresetCheck PreparePreset(DeploymentPreset preset, out DeploymentDraft? candidate)
    {
        candidate = null;
        if (preset is null) return new(DeploymentPresetFailure.InvalidPreset);
        try { preset.Validate(View); }
        catch (ArgumentException) { return new(DeploymentPresetFailure.InvalidPreset); }
        if (preset.ForceId != Force.Id) return new(DeploymentPresetFailure.WrongForce);
        if (View.World.Simulation.NewGame.MixedTroopsEnabled && preset.Members.Any(member => member.Detachment is null))
            return new(DeploymentPresetFailure.MissingDetachments);
        if (View.World.Simulation.NewGame.MixedTroopsEnabled && preset.NavalFormationId is not null && preset.NavalTrainingId is null)
            return new(DeploymentPresetFailure.MissingDetachments);
        var missing = preset.Members.Select(m => new DeploymentPresetMissingMember(m.OfficerId, MemberFailure(m.OfficerId)))
            .Where(m => m.Reason != DeploymentPresetFailure.None).ToImmutableArray();
        DeploymentPresetCheck Result(DeploymentPresetFailure reason, int? officerId = null, uint maximumSoldiers = 0) =>
            new(reason, officerId, maximumSoldiers) { MissingMembers = missing };
        var members = preset.Members.Where(m => !missing.Any(unavailable => unavailable.OfficerId == m.OfficerId)).ToArray();
        if (members.Length == 0) return Result(DeploymentPresetFailure.NoAvailableMembers);
        var prepared = new DeploymentDraft(View, Home.Id);
        if (!prepared.SetMembers(members.Select(m => m.OfficerId)) ||
            !prepared.Formations(false).Any(f => f.Id == preset.FormationId))
            return Result(DeploymentPresetFailure.FormationUnavailable);
        if (preset.NavalFormationId is int ship && !prepared.Formations(true).Any(f => f.Id == ship))
            return Result(DeploymentPresetFailure.ShipUnavailable);
        // Validate the saved land/water pair together; an intermediate default pair is irrelevant.
        prepared.FormationId = preset.FormationId;
        prepared.NavalFormationId = preset.NavalFormationId;
        if (preset.NavalTrainingId is int training && !prepared.SetNavalTraining(training))
            return Result(DeploymentPresetFailure.ShipUnavailable);
        if (View.World.Simulation.NewGame.MixedTroopsEnabled && !prepared.SetSiegeEquipment(preset.SiegeEquipmentId))
            return Result(DeploymentPresetFailure.FormationUnavailable);
        prepared.Positions = members.ToImmutableDictionary(m => m.OfficerId, m => m.Position);
        uint maximum = prepared.MaximumSoldiers;
        uint soldiers = preset.SoldierMode == DeploymentSoldierMode.Maximum ? maximum : Math.Min(preset.Soldiers, maximum);
        if (soldiers == 0)
            return Result(DeploymentPresetFailure.InsufficientSoldiers, maximumSoldiers: maximum);
        prepared.Soldiers = soldiers;
        if (View.World.Simulation.NewGame.MixedTroopsEnabled && members.All(member => member.Detachment is not null))
        {
            var saved = members.ToImmutableDictionary(member => member.OfficerId, member => member.Detachment!);
            if (!prepared.SetDetachments(DetachmentAllocation.Resize(saved, soldiers)))
                return Result(DeploymentPresetFailure.InvalidPreset);
        }
        foreach (var member in members)
            if (!prepared.SetTactic(member.OfficerId, member.TacticId))
                return Result(DeploymentPresetFailure.TacticUnavailable, member.OfficerId);
        if (prepared.Cost > Money) return Result(DeploymentPresetFailure.InsufficientMoney);
        candidate = prepared;
        return Result(DeploymentPresetFailure.None, maximumSoldiers: maximum);
    }

    private DeploymentPresetFailure MemberFailure(int officerId)
    {
        var person = View.World.Officers.Single(o => o.Id == officerId);
        if (person.Status == OfficerStatus.Dead) return DeploymentPresetFailure.OfficerDead;
        if (person.Status == OfficerStatus.Captive) return DeploymentPresetFailure.OfficerCaptured;
        if (person.OwnerId != Force.Id) return DeploymentPresetFailure.OfficerTransferred;
        if (person.SettlementId != Home.Id || person.UnitId is not null)
            return DeploymentPresetFailure.OfficerElsewhere;
        return Candidates.Contains(person.Id) ? DeploymentPresetFailure.None : DeploymentPresetFailure.OfficerBusy;
    }
}
