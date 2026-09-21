using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace San9.Core.Domain;

public sealed record FacilityAttackModifier(bool UsesRockfallPower, int RangeBonus);
public sealed record FacilityAttackProfile(OrdinaryAttack? Attack, bool UsesRockfallPower);

public enum TacticEffect
{
    Martial, FormationTechnology, Construction, StoneFormation, CounterConfusion, CounterTrap,
    Confusion, TrapSelection, Trap, Defection, Illusion, Taunt, Inspire, Heal, Sorcery, FacilityAttack
}

public sealed record TacticRule(int Id, int? Category, TacticEffect Effect, int Power, int MinimumInterval,
    int TrainingRequirement, bool Selectable, bool AffectsFacilities, ImmutableArray<int> CompatibleFormations)
{
    [JsonRequired] public int EffectRadius { get; init; }
    [JsonRequired] public bool CanRescuePrisoners { get; init; }
    [JsonRequired] public int? ContactRange { get; init; }
    [JsonRequired] public FacilityAttackModifier? FacilityAttack { get; init; }
    internal void Validate(IReadOnlyDictionary<int, FormationRule> formations)
    {
        if (Id < 0 || EffectRadius < 0 || CanRescuePrisoners && Effect != TacticEffect.Martial || ContactRange is <= 0 || ContactRange is not null && Effect != TacticEffect.Martial || Category is < 0 or > 8 || !Enum.IsDefined(Effect) || Power < 0 || MinimumInterval < 0 ||
            TrainingRequirement < 0 || CompatibleFormations.IsDefault ||
            CompatibleFormations.Distinct().Count() != CompatibleFormations.Length ||
            CompatibleFormations.Any(id => !formations.ContainsKey(id)) ||
            (Effect == TacticEffect.FacilityAttack) != (Category is null) ||
            (Effect == TacticEffect.FacilityAttack) != (FacilityAttack is not null) || FacilityAttack is { RangeBonus: < 0 } ||
            Selectable && Effect is not (TacticEffect.Martial or TacticEffect.Confusion or TacticEffect.TrapSelection or
                TacticEffect.Defection or TacticEffect.Illusion or TacticEffect.Taunt or TacticEffect.Inspire or TacticEffect.Heal or TacticEffect.Sorcery or
                TacticEffect.CounterConfusion or TacticEffect.CounterTrap))
            throw new ArgumentException("Invalid tactic definition.");
        int? denominator = Effect switch
        {
            TacticEffect.Taunt => 10, TacticEffect.Defection => 30,
            TacticEffect.Illusion or TacticEffect.Sorcery => 33,
            TacticEffect.Trap or TacticEffect.Heal => 20, TacticEffect.Inspire => 15, _ => null
        };
        if (denominator is int maximum && Power >= maximum)
            throw new ArgumentException("Tactic power produces an undefined effect divisor.");
    }
}
