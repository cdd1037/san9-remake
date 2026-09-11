namespace San9.Core.Simulation;

/// <summary>Validates the complete candidate state at initialization and publication.</summary>
internal static class WorldInvariants
{
    internal static void Validate(WorldMutation state)
    {
        _ = new GameClock(state.Clock.Year, state.Clock.Month, state.Clock.Xun);
        foreach (var force in state.Forces.Values)
        {
            if (force.Id < 0 || force.Trust < 0 || force.ProvidedTactics.IsDefault || force.ProvidedTactics.Any(t => t < 0) || force.ProvidedTactics.Distinct().Count() != force.ProvidedTactics.Length) throw new ArgumentException("Invalid force.");
        }
        foreach (var settlement in state.Settlements.Values)
        {
            if (settlement.Id < 0 || !Enum.IsDefined(settlement.Kind) || settlement.OwnerId is int owner && !state.Forces.ContainsKey(owner) ||
                settlement.MaxDurability <= 0 || settlement.Durability < 0 || settlement.Durability > settlement.MaxDurability ||
                settlement.Morale is < 0 or > 100 || settlement.Commerce < 0 || settlement.Commerce > settlement.MaxCommerce ||
                settlement.Cultivation < 0 || settlement.Cultivation > settlement.MaxCultivation || settlement.PublicOrder is < 0 or > 1000)
                throw new ArgumentException("Invalid settlement.");
        }
        foreach (var officer in state.Officers.Values)
        {
            if (!officer.IsValid() || officer.SettlementId is int location && !state.Settlements.ContainsKey(location) ||
                officer.OwnerId is int owner && !state.Forces.ContainsKey(owner) ||
                officer.IsServing && state.Settlements[officer.SettlementId!.Value].OwnerId != officer.OwnerId) throw new ArgumentException("Invalid officer.");
        }
        if (state.Forces.Values.Any(f => f.LeaderId is int leader &&
                (!state.Officers.TryGetValue(leader, out var officer) || officer.OwnerId != f.Id)) ||
            state.Settlements.Values.Any(s => s.CorpsId < 0 || s.CorpsId is not null && s.OwnerId is null) ||
            state.Settlements.Values.Where(s => s.CorpsId is not null).GroupBy(s => s.CorpsId)
                .Any(g => g.Select(s => s.OwnerId).Distinct().Count() != 1))
            throw new ArgumentException("Invalid leader or corps relationship.");
        foreach (var unit in state.Units.Values)
        {
            if (!unit.IsValid || !state.Forces.ContainsKey(unit.OwnerId)) throw new ArgumentException("Invalid unit.");
        }
        if (state.Corps.Values.Any(c => c.Id < 0 || !state.Forces.ContainsKey(c.OwnerId) ||
                !state.Officers.TryGetValue(c.LeaderId, out var leader) || !leader.IsServing || leader.OwnerId != c.OwnerId) ||
            state.Treasures.Values.Any(t => !t.IsValid || t.HolderId is int holder && !state.Officers.ContainsKey(holder)) ||
            state.Relations.Values.Any(r => r.FirstId >= r.SecondId || r.Value is < 0 or > 100 || !state.Forces.ContainsKey(r.FirstId) || !state.Forces.ContainsKey(r.SecondId)))
            throw new ArgumentException("Invalid information state relationships.");
    }
}
