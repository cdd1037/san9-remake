using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace San9.Core.Domain;

public enum WorldEditorKind { Officers, Corps, Cities, Facilities, Obstacles }

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(OfficerEdit), "officer")]
[JsonDerivedType(typeof(CorpsEdit), "corps")]
[JsonDerivedType(typeof(SettlementEdit), "settlement")]
public abstract record WorldEdit(int Id);

public sealed record OfficerEdit(int Id, OfficerAbilities Abilities, int Personality, int Righteousness,
    int Ambition, int Affinity, int Injury, int? Loyalty, ImmutableArray<int> Proficiencies,
    ImmutableArray<int> MasteredCategories, ImmutableArray<int> LearnedTactics) : WorldEdit(Id)
{
    public OfficerState ApplyTo(OfficerState person)
    {
        var next = person with { BaseAbilities = Abilities, Personality = Personality, Righteousness = Righteousness,
            Ambition = Ambition, Affinity = Affinity, Injury = Injury, TacticProficiencies = Proficiencies,
            MasteredCategories = MasteredCategories.Order().ToImmutableArray(), LearnedTactics = LearnedTactics.Order().ToImmutableArray(),
            EquippedTacticId = person.EquippedTacticId is int equipped && !LearnedTactics.Contains(equipped) ? null : person.EquippedTacticId };
        return person.OwnerId is int owner && Loyalty is int loyalty ? next.WithLoyalty(owner, loyalty) : next;
    }
}

public sealed record CorpsEdit(int Id, uint Money, uint Food) : WorldEdit(Id)
{
    public CorpsState ApplyTo(CorpsState corps) => corps with { Treasury = new(Money, Food) };
}

public sealed record SettlementEdit(int Id, int Durability, uint Soldiers, int Morale,
    int PublicOrder, int Commerce, int MaxCommerce, int Cultivation, int MaxCultivation,
    int MaxDurability, uint Population, uint DraftPopulation,
    ImmutableArray<int> EnabledTacticIds) : WorldEdit(Id)
{
    public SettlementState ApplyTo(SettlementState place) => place with
    {
        Durability = Durability, Soldiers = Soldiers, Morale = Morale, PublicOrder = PublicOrder,
        Commerce = Commerce, MaxCommerce = MaxCommerce, Cultivation = Cultivation, MaxCultivation = MaxCultivation,
        MaxDurability = MaxDurability, Population = Population, DraftPopulation = DraftPopulation,
        EnabledTacticIds = EnabledTacticIds.Order().ToImmutableArray()
    };
}
