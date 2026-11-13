using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Application;

public enum DeploymentSoldierMode { Fixed, Maximum }

public sealed record DeploymentPresetMember(
    [property: JsonRequired] int OfficerId,
    [property: JsonRequired] FormationSlot Position,
    [property: JsonRequired] int? TacticId)
{
    public DetachmentState? Detachment { get; init; }
}

/// <summary>Authored preparation preferences within one campaign; never reserves world resources.</summary>
public sealed record DeploymentPreset(
    [property: JsonRequired] Guid Id,
    [property: JsonRequired] int ForceId,
    [property: JsonRequired] string Name,
    [property: JsonRequired] ImmutableArray<DeploymentPresetMember> Members,
    [property: JsonRequired] int FormationId,
    [property: JsonRequired] int? NavalFormationId,
    [property: JsonRequired] DeploymentSoldierMode SoldierMode,
    [property: JsonRequired] uint Soldiers)
{
    public const int MaximumNameLength = 32;
    public int? NavalTrainingId { get; init; }
    public int? SiegeEquipmentId { get; init; }
    public static bool IsValidName(string? name) => !string.IsNullOrWhiteSpace(name) &&
        name == name.Trim() && name.Length <= MaximumNameLength && !name.Any(char.IsControl);

    // Availability is deliberately checked only on application: casualties, transfers and lost
    // technology must not make a campaign save containing an old preference unloadable.
    internal void Validate(StrategySnapshot view)
    {
        var rules = view.MilitaryRules;
        if (Id == Guid.Empty || !IsValidName(Name) || !Enum.IsDefined(SoldierMode) ||
            (SoldierMode == DeploymentSoldierMode.Fixed ? Soldiers == 0 : Soldiers != 0) ||
            !view.World.Forces.Any(f => f.Id == ForceId) || Members.IsDefaultOrEmpty ||
            Members.Any(m => m is null) || Members.Select(m => m.OfficerId).Distinct().Count() != Members.Length ||
            Members.Any(m => !view.World.Officers.Any(o => o.Id == m.OfficerId)) ||
            rules is null || NavalTrainingId is int training && !rules.NavalTraining.ContainsKey(training) ||
            SiegeEquipmentId is int siege && !rules.SiegeEquipment.ContainsKey(siege) ||
            !rules.Formations.TryGetValue(FormationId, out var land) || land.TravelMode != TravelMode.Land ||
            Members.Length > land.OfficerCapacity || !rules.ValidFormationPair(FormationId, NavalFormationId) ||
            NavalFormationId is int water && (!rules.Formations.TryGetValue(water, out var ship) ||
                ship.TravelMode != TravelMode.Water || Members.Length > ship.OfficerCapacity))
            throw new ArgumentException("Invalid deployment preset.");
        var slots = DeploymentDraft.FormationSlots(view, FormationId);
        if (Members.Any(m => !slots.Contains(m.Position) ||
            m.TacticId is int tactic && !rules.Tactics.ContainsKey(tactic) ||
            m.Detachment is { } detachment && (!detachment.IsValid || detachment.Soldiers == 0 ||
                !rules.TroopTypes.TryGetValue(detachment.TroopTypeId, out var type) || detachment.Level > type.MaximumLevel)))
            throw new ArgumentException("Invalid deployment preset member configuration.");
    }
}
