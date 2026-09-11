using System.Collections.Immutable;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application;

internal sealed class ScenarioSessionDefinition
{
    internal ScenarioSessionDefinition(ScenarioInitialState definition)
    {
        definition.Validate();
        if (definition.Day is not (1 or 11 or 21)) throw new ArgumentException("Expected a stable xun boundary.");
        Content = definition with
        {
            Information = definition.Information.Copy(), Forces = definition.Forces.ToArray(),
            Officers = definition.Officers.ToArray(), Units = definition.Units.ToArray(),
            Settlements = definition.Settlements.Select(s => s with { Icons = s.Icons.ToArray() }).ToArray(),
            RegionNameKeys = new(definition.RegionNameKeys), RegionCityIds = new(definition.RegionCityIds)
        };
        Fingerprint = CanonicalJson.Hash(new
        {
            definition.SchemaVersion, definition.ScenarioId, definition.Year, definition.Month, definition.Day, definition.PlayerForceId, definition.DomesticPairs, definition.ImperialSealId, definition.EmperorId,
            Forces = Forces().OrderBy(f => f.Id), Settlements = Settlements().OrderBy(s => s.Id),
            Officers = definition.Officers.Select(o => o.State with { LearnedTactics = o.State.LearnedTactics.Order().ToImmutableArray() }).OrderBy(o => o.Id),
            Units = definition.Units.OrderBy(u => u.Id), Corps = definition.Information.Corps.OrderBy(c => c.Id),
            Treasures = definition.Information.TreasureStates.OrderBy(t => t.Id),
            Relations = definition.Information.Relations.OrderBy(r => r.FirstId).ThenBy(r => r.SecondId),
            Coordinates = definition.Settlements.OrderBy(s => s.Id).Select(s => new { s.Id, s.X, s.Y }),
            Regions = definition.RegionNameKeys.Keys.Order(), definition.RegionCityIds,
            Ranks = definition.Information.Ranks.OrderBy(r => r.Id).Select(r => new { r.Id, r.CommandCapacity, r.RequiredTrust, r.LoyaltyBonus }),
            Offices = definition.Information.Offices.OrderBy(o => o.Id).Select(o => new { o.Id, o.CommandCapacity, o.Salary }),
            TreasureDefinitions = definition.Information.Treasures.OrderBy(t => t.Id).Select(t => new { t.Id, t.Category, t.AbilityBonus, t.LoyaltyBonus, t.TacticId })
        });
    }

    internal ScenarioInitialState Content { get; }
    internal string Fingerprint { get; }
    internal WorldState CreateWorld(ulong seed) => WorldState.Create(new(Content.Year, Content.Month, Content.Day), seed,
        Forces(), Settlements(), Content.Officers.Select(o => o.State), Content.Units, Content.Information.Corps,
        Content.Information.TreasureStates, Content.Information.Relations,
        Content.Forces.OrderBy(f => f.Id == Content.PlayerForceId ? 0 : 1).ThenBy(f => f.Id)
            .Select(f => new ControlSeat(f.Id == Content.PlayerForceId ? "player" : $"ai:{f.Id}", f.Id,
                f.Id == Content.PlayerForceId ? ControllerKind.Player : ControllerKind.Automatic)));

    private IEnumerable<ForceState> Forces() => Content.Forces.Select(f => new ForceState(f.Id, f.Money, f.Food, f.Trust)
    {
        ProvidedTactics = f.ProvidedTactics.Order().ToImmutableArray(), IsBarbarian = f.IsBarbarian,
        LeaderId = f.LeaderId, RankId = f.RankId, BaseTrust = f.BaseTrust
    });

    private IEnumerable<SettlementState> Settlements() => Content.Settlements.Select(s => new SettlementState(s.Id,
        s.OwnerId, s.Soldiers, s.Durability, s.MaxDurability, s.Morale, s.PublicOrder, s.Commerce, s.MaxCommerce,
        s.Cultivation, s.MaxCultivation)
    {
        Kind = s.Kind, CorpsId = s.CorpsId, SoldierCapacity = s.SoldierCapacity, ConsumesFood = s.ConsumesFood, Population = s.Population, DraftPopulation = s.DraftPopulation
    });
}
