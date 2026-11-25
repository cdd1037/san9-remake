using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application;

public enum DeploymentSoldierMode { Fixed, Maximum }

public sealed record DeploymentPresetMember(
    [property: JsonRequired] int OfficerId,
    [property: JsonRequired] FormationSlot Position,
    [property: JsonRequired] int? TacticId);

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
    public static bool IsValidName(string? name) => !string.IsNullOrWhiteSpace(name) &&
        name == name.Trim() && name.Length <= MaximumNameLength && !name.Any(char.IsControl);

    // A saved preference owns identities and layout data, not permission to deploy today.
    internal void ValidateStored(WorldSnapshot world)
    {
        if (Id == Guid.Empty || !IsValidName(Name) || !Enum.IsDefined(SoldierMode) ||
            (SoldierMode == DeploymentSoldierMode.Fixed ? Soldiers == 0 : Soldiers != 0) ||
            !world.Forces.Any(f => f.Id == ForceId) || Members.IsDefaultOrEmpty ||
            Members.Any(m => m is null) || Members.Select(m => m.OfficerId).Distinct().Count() != Members.Length ||
            Members.Any(m => !world.Officers.Any(o => o.Id == m.OfficerId) || m.Position.Column < 0 ||
                m.Position.Row is < 0 or > 2 || m.TacticId < 0) || FormationId < 0 || NavalFormationId < 0)
            throw new ArgumentException("Invalid stored deployment preset.");
    }

    internal void Validate(StrategySnapshot view)
    {
        ValidateStored(view.World);
        var rules = view.MilitaryRules;
        if (rules is null || !rules.Formations.TryGetValue(FormationId, out var land) || land.TravelMode != TravelMode.Land ||
            Members.Length > land.OfficerCapacity || !rules.ValidFormationPair(FormationId, NavalFormationId) ||
            NavalFormationId is int water && (!rules.Formations.TryGetValue(water, out var ship) ||
                ship.TravelMode != TravelMode.Water || Members.Length > ship.OfficerCapacity))
            throw new ArgumentException("Invalid deployment preset.");
        var slots = DeploymentDraft.FormationSlots(view, FormationId);
        if (Members.Any(m => !slots.Contains(m.Position) ||
            m.TacticId is int tactic && !rules.Tactics.ContainsKey(tactic)))
            throw new ArgumentException("Invalid deployment preset member configuration.");
    }
}
