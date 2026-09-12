namespace San9.Core.Simulation;

/// <summary>Validates the complete candidate state at initialization and publication.</summary>
internal static class WorldInvariants
{
    internal static void Validate(WorldMutation state)
    {
        _ = new GameClock(state.Clock.Year, state.Clock.Month, state.Clock.Day);
        ValidateSimulation(state);
        foreach (var force in state.Forces.Values)
        {
            if (force.Id < 0 || force.Trust is < 0 or > 1000 || force.BaseTrust is < 0 or > 1000 || force.ProvidedTactics.IsDefault || force.ProvidedTactics.Any(t => t < 0) || force.ProvidedTactics.Distinct().Count() != force.ProvidedTactics.Length) throw new ArgumentException("Invalid force.");
        }
        foreach (var settlement in state.Settlements.Values)
        {
            if (settlement.Id < 0 || !Enum.IsDefined(settlement.Kind) || settlement.OwnerId is int owner && !state.Forces.ContainsKey(owner) ||
                settlement.SoldierCapacity is 0 or > 500000 || settlement.Soldiers > settlement.SoldierCapacity || settlement.Population > 1000000 || settlement.DraftPopulation > 500000 || settlement.MaxCommerce > 2500 || settlement.MaxCultivation > 4000 || settlement.MaxDurability <= 0 || settlement.Durability < 0 || settlement.Durability > settlement.MaxDurability ||
                settlement.Morale is < 0 or > 100 || settlement.Commerce < 0 || settlement.Commerce > settlement.MaxCommerce ||
                settlement.Cultivation < 0 || settlement.Cultivation > settlement.MaxCultivation || settlement.PublicOrder is < 0 or > 1000)
                throw new ArgumentException("Invalid settlement.");
        }
        foreach (var officer in state.Officers.Values)
        {
            if (!officer.IsValid() || officer.ForceLoyalties.Keys.Any(id => !state.Forces.ContainsKey(id)) ||
                officer.SettlementId is int location && !state.Settlements.ContainsKey(location) ||
                officer.OwnerId is int owner && !state.Forces.ContainsKey(owner) ||
                officer.IsServing && state.Settlements[officer.SettlementId!.Value].OwnerId != officer.OwnerId) throw new ArgumentException("Invalid officer.");
        }
        if (state.Forces.Values.Any(f => f.AdviserId is int adviser && (!state.Officers.TryGetValue(adviser, out var p) || p.OwnerId != f.Id || p.Status != San9.Core.Domain.OfficerStatus.Strategist)) ||
            state.Officers.Values.Any(p => p.Status == San9.Core.Domain.OfficerStatus.Strategist &&
                (p.OwnerId is not int owner || state.Forces[owner].AdviserId != p.Id)))
            throw new ArgumentException("Invalid force adviser relationship.");
        if (state.Forces.Values.Any(f => f.LeaderId is int leader &&
                (!state.Officers.TryGetValue(leader, out var officer) || officer.OwnerId != f.Id)) ||
            state.Settlements.Values.Any(s => s.CorpsId < 0 || s.CorpsId is int corpsId && (!state.Corps.TryGetValue(corpsId, out var corps) || corps.OwnerId != s.OwnerId)) ||
            state.Settlements.Values.Where(s => s.CorpsId is not null).GroupBy(s => s.CorpsId)
                .Any(g => g.Select(s => s.OwnerId).Distinct().Count() != 1))
            throw new ArgumentException("Invalid leader or corps relationship.");
        foreach (var unit in state.Units.Values)
        {
            if (!unit.IsValid || !state.Forces.ContainsKey(unit.OwnerId) || unit.CorpsId is int corpsId && (!state.Corps.TryGetValue(corpsId, out var corps) || corps.OwnerId != unit.OwnerId)) throw new ArgumentException("Invalid unit.");
        }
        if (state.Corps.Values.Any(c => c.Id < 0 || !state.Forces.ContainsKey(c.OwnerId) ||
                !state.Officers.TryGetValue(c.LeaderId, out var leader) || !leader.IsServing || leader.OwnerId != c.OwnerId) ||
            state.Treasures.Values.Any(t => !t.IsValid || t.HolderId is int holder && !state.Officers.ContainsKey(holder)) ||
            state.Relations.Values.Any(r => r.FirstId >= r.SecondId || r.Value is < 0 or > 100 || !state.Forces.ContainsKey(r.FirstId) || !state.Forces.ContainsKey(r.SecondId)))
            throw new ArgumentException("Invalid information state relationships.");
    }
    private static void ValidateSimulation(WorldMutation state)
    {
        var sim = state.Simulation;
        _ = new GameClock(sim.Started.Year, sim.Started.Month, sim.Started.Day);
        if (!sim.Started.IsXunBoundary || sim.Started.DayNumber > state.Clock.DayNumber ||
            !Enum.IsDefined(sim.Boundary) || sim.Controls.IsDefault || sim.Tasks.IsDefault ||
            sim.RandomStreams.IsDefault ||
            sim.ActiveControl < 0 || sim.ActiveControl > sim.Controls.Length || sim.NextSequence < 0 ||
            sim.Controls.Any(c => string.IsNullOrWhiteSpace(c.ControllerId) || !Enum.IsDefined(c.Kind) || !state.Forces.ContainsKey(c.ForceId)) ||
            sim.Controls.Select(c => c.ForceId).Distinct().Count() != sim.Controls.Length ||
            sim.Boundary != SimulationBoundary.Terminal && (!state.Clock.IsXunBoundary || sim.TerminalReason is not null) ||
            sim.Boundary == SimulationBoundary.Terminal && (string.IsNullOrWhiteSpace(sim.TerminalReason) || !sim.Tasks.IsEmpty) ||
            sim.RandomStreams.Any(s => string.IsNullOrWhiteSpace(s.Name)) ||
            !sim.RandomStreams.Select(s => s.Name).SequenceEqual(sim.RandomStreams.Select(s => s.Name).Distinct().Order(StringComparer.Ordinal)))
            throw new ArgumentException("Invalid simulation boundary, control, sequence or random state.");
        if (sim.Tasks.Select(t => t.Id).Distinct().Count() != sim.Tasks.Length ||
            sim.Tasks.Select(t => t.OfficerId).Distinct().Count() != sim.Tasks.Length)
            throw new ArgumentException("Duplicate task identity or officer occupation.");
        foreach (var task in sim.Tasks)
        {
            _ = new GameClock(task.Started.Year, task.Started.Month, task.Started.Day);
            _ = new GameClock(task.Due.Year, task.Due.Month, task.Due.Day);
            if (string.IsNullOrWhiteSpace(task.Kind) || task.Id == Guid.Empty ||
                task.Sequence < 0 || task.Sequence >= sim.NextSequence ||
                !sim.Controls.Any(c => c.ControllerId == task.ControllerId && c.ForceId == task.ForceId) ||
                !state.Forces.ContainsKey(task.ForceId) || !state.Officers.ContainsKey(task.OfficerId) || !state.Settlements.ContainsKey(task.OriginSettlementId) ||
                task.Started.DayNumber < sim.Started.DayNumber || task.Started.DayNumber > state.Clock.DayNumber ||
                task.Due.DayNumber < task.Started.DayNumber || task.Due.DayNumber < state.Clock.DayNumber || task.ProgressDays < 0 ||
                task.ProgressDays != state.Clock.DayNumber - task.Started.DayNumber)
                throw new ArgumentException("Invalid persistent task.");
            if (task.Journey is { } journey && (journey.DestinationPlaceId < 0 || journey.SuccessRoll is < 0 or > 99 ||
                journey.TargetOfficerId is int target && !state.Officers.ContainsKey(target)))
                throw new ArgumentException("Invalid personnel journey.");
        }
    }

}
