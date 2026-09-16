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
            if (force.IsEliminated && (force.LeaderId is not null || force.AdviserId is not null || force.Money != 0 || force.Food != 0 ||
                state.Officers.Values.Any(p => p.OwnerId == force.Id) || state.Settlements.Values.Any(s => s.OwnerId == force.Id) ||
                state.Units.Values.Any(u => u.OwnerId == force.Id) || state.Corps.Values.Any(c => c.OwnerId == force.Id) ||
                state.Simulation.Controls.Any(c => c.ForceId == force.Id))) throw new ArgumentException("Eliminated force retains active references.");
            if (force.Id < 0 || force.Trust is < 0 or > 1000 || force.BaseTrust is < 0 or > 1000 || force.ProvidedTactics.IsDefault || force.ProvidedTactics.Any(t => t < 0) || force.ProvidedTactics.Distinct().Count() != force.ProvidedTactics.Length) throw new ArgumentException("Invalid force.");
        }
        foreach (var settlement in state.Settlements.Values)
        {
            if (settlement.Facility is { } facility && (settlement.Kind != San9.Core.Domain.SettlementKind.Facility ||
                facility.TypeId < 0 || facility.Position.X < 0 || facility.Position.Y < 0 || facility.BuilderUnitId < 0 ||
                facility.BuilderUnitId is int builder && (settlement.IsDestroyed || settlement.CanHostPersons ||
                    !state.Units.TryGetValue(builder, out var unit) || unit.OwnerId != settlement.OwnerId ||
                    unit.Order?.Construction is not { } plan || plan.RegionId != settlement.Id || plan.FacilityTypeId != facility.TypeId || plan.Position != facility.Position)))
                throw new ArgumentException("Invalid facility instance or construction owner.");
            if (settlement.IsDestroyed && (settlement.OwnerId is not null || settlement.CorpsId is not null || settlement.Durability != 0 ||
                settlement.Soldiers != 0 || settlement.Wounded != 0 || settlement.ConfusionDaysRemaining != 0)) throw new ArgumentException("Destroyed settlement retains active state.");
            if (settlement.ConfusionDaysRemaining < 0) throw new ArgumentException("Invalid settlement confusion duration.");
            if (settlement.Wounded > San9.Core.Domain.MilitaryRulebook.WoundedCapacity) throw new ArgumentException("Invalid settlement wounded.");
            if (settlement.Id < 0 || !Enum.IsDefined(settlement.Kind) || settlement.OwnerId is int owner && !state.Forces.ContainsKey(owner) ||
                settlement.SoldierCapacity is 0 or > 500000 || settlement.Soldiers > settlement.SoldierCapacity || settlement.Population > 1000000 || settlement.MaxCommerce > 2500 || settlement.MaxCultivation > 4000 || settlement.MaxDurability <= 0 || settlement.Durability < 0 || settlement.Durability > settlement.MaxDurability ||
                settlement.Morale is < 0 or > 100 || settlement.Commerce < 0 || settlement.Commerce > settlement.MaxCommerce ||
                settlement.Cultivation < 0 || settlement.Cultivation > settlement.MaxCultivation || settlement.PublicOrder is < 0 or > 1000)
                throw new ArgumentException("Invalid settlement.");
        }
        foreach (var officer in state.Officers.Values)
        {
            if (!officer.IsValid() || officer.ForceLoyalties.Keys.Any(id => !state.Forces.ContainsKey(id)) ||
                officer.SettlementId is int location && !state.Settlements.ContainsKey(location) ||
                officer.OwnerId is int owner && !state.Forces.ContainsKey(owner) ||
                officer.UnitId is int unitId && (!state.Units.TryGetValue(unitId, out var unit) ||
                    (officer.Status == San9.Core.Domain.OfficerStatus.Captive ? unit.OwnerId == officer.OwnerId : unit.OwnerId != officer.OwnerId)) ||
                officer.IsServing && officer.SettlementId is int home && state.Settlements[home].OwnerId != officer.OwnerId) throw new ArgumentException("Invalid officer.");
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
            if (!unit.IsValid || !state.Forces.ContainsKey(unit.OwnerId) ||
                unit.MemberRanks.Keys.Any(id => !state.Officers.TryGetValue(id, out var person) || person.UnitId != unit.Id || !person.IsServing) ||
                !unit.MemberOrder.IsEmpty && !unit.MemberOrder.Order().SequenceEqual(state.Officers.Values
                    .Where(p => p.UnitId == unit.Id && p.IsServing).Select(p => p.Id).Order()) ||
                unit.Order?.SettlementId is int target && !state.Settlements.ContainsKey(target) ||
                unit.Order?.UnitId is int targetUnit && !state.Units.ContainsKey(targetUnit) ||
                unit.EngagementTargetId is int engagement && !state.Units.ContainsKey(engagement) ||
                unit.CommanderId is int commander && !state.Officers.ContainsKey(commander) ||
                unit.CorpsId is int corpsId && (!state.Corps.TryGetValue(corpsId, out var corps) || corps.OwnerId != unit.OwnerId)) throw new ArgumentException("Invalid unit.");
        }
        if (state.Corps.Values.Any(c => c.Id < 0 || !Enum.IsDefined(c.ControlMode) || !state.Forces.ContainsKey(c.OwnerId) ||
                !state.Officers.TryGetValue(c.LeaderId, out var leader) || !leader.IsServing || leader.OwnerId != c.OwnerId) ||
            state.Treasures.Values.Any(t => !t.IsValid || t.HolderId is int holder && !state.Officers.ContainsKey(holder)) ||
            state.Relations.Values.Any(r => r.FirstId >= r.SecondId || r.Value is < 0 or > 100 || !state.Forces.ContainsKey(r.FirstId) || !state.Forces.ContainsKey(r.SecondId)))
            throw new ArgumentException("Invalid information state relationships.");
    }
    private static void ValidateSimulation(WorldMutation state)
    {
        var sim = state.Simulation;
        sim.Alliance?.Validate(state.Forces.Values.Where(f => !f.IsEliminated && (!f.IsBarbarian ||
            sim.Controls.Any(c => c.ForceId == f.Id && c.Kind == ControllerKind.Player))).Select(f => f.Id).ToHashSet());
        _ = new GameClock(sim.Started.Year, sim.Started.Month, sim.Started.Day);
        if (!sim.Started.IsXunBoundary || sim.Started.DayNumber > state.Clock.DayNumber ||
            sim.ComputerDamageBonusPercent < 0 || !Enum.IsDefined(sim.Boundary) || sim.Controls.IsDefault || sim.Tasks.IsDefault ||
            sim.RandomStreams.IsDefault ||
            sim.ActiveControl < 0 || sim.ActiveControl > sim.Controls.Length || sim.NextSequence < 0 ||
            sim.NextUnitId < 0 || sim.NextUnitId > (long)int.MaxValue + 1 || state.Units.Keys.Any(id => id >= sim.NextUnitId) ||
            sim.Controls.Any(c => string.IsNullOrWhiteSpace(c.ControllerId) || !Enum.IsDefined(c.Kind) || !state.Forces.ContainsKey(c.ForceId)) ||
            sim.Controls.Select(c => c.ForceId).Distinct().Count() != sim.Controls.Length ||
            sim.Boundary != SimulationBoundary.Terminal && (!state.Clock.IsXunBoundary && sim.PendingDailyTask is null || sim.TerminalReason is not null) ||
            sim.Boundary == SimulationBoundary.Terminal && (string.IsNullOrWhiteSpace(sim.TerminalReason) || !sim.Tasks.IsEmpty) ||
            sim.RandomStreams.Any(s => string.IsNullOrWhiteSpace(s.Name)) ||
            !sim.RandomStreams.Select(s => s.Name).SequenceEqual(sim.RandomStreams.Select(s => s.Name).Distinct().Order(StringComparer.Ordinal)))
            throw new ArgumentException("Invalid simulation boundary, control, sequence or random state.");
        AllianceFormation.ValidatePending(state);
        if (sim.PendingDailyTask is { } pending)
        {
            _ = new GameClock(pending.XunStarted.Year, pending.XunStarted.Month, pending.XunStarted.Day);
            if (!pending.XunStarted.IsXunBoundary || pending.XunStarted.DayNumber < sim.Started.DayNumber ||
                state.Clock.DayNumber < pending.XunStarted.DayNumber || state.Clock.DayNumber >= pending.XunStarted.Advance().DayNumber ||
                sim.ActiveControl != sim.Controls.Length || pending.RemainingTaskIds.IsDefaultOrEmpty ||
                pending.RemainingTaskIds.Distinct().Count() != pending.RemainingTaskIds.Length ||
                !sim.Tasks.Any(t => t.Id == pending.RemainingTaskIds[0] && t.Due.DayNumber <= state.Clock.DayNumber) ||
                !sim.Controls.Any(c => c.ForceId == pending.RespondingForceId && c.Kind == ControllerKind.Player))
                throw new ArgumentException("Invalid daily task decision.");
            var ordered = sim.Tasks.Where(t => pending.RemainingTaskIds.Contains(t.Id))
                .OrderBy(t => t.Sequence).ThenBy(t => t.OriginSettlementId).ThenBy(t => t.OfficerId).ThenBy(t => t.Id)
                .Select(t => t.Id);
            if (!ordered.SequenceEqual(pending.RemainingTaskIds))
                throw new ArgumentException("Invalid remaining daily task order.");
        }
        AttackRequests.Validate(state);
        if (sim.Tasks.Select(t => t.Id).Distinct().Count() != sim.Tasks.Length ||
            sim.Tasks.Select(t => t.OfficerId).Distinct().Count() != sim.Tasks.Length)
            throw new ArgumentException("Duplicate task identity or officer occupation.");
        foreach (var task in sim.Tasks)
        {
            if (task.Gift is { } gift && (task.Kind != GiftDiplomacy.Kind || gift.Offer?.IsValid != true ||
                gift.TargetForceId == task.ForceId || !state.Forces.ContainsKey(gift.TargetForceId)))
                throw new ArgumentException("Invalid gift mission.");
            _ = new GameClock(task.Started.Year, task.Started.Month, task.Started.Day);
            _ = new GameClock(task.Due.Year, task.Due.Month, task.Due.Day);
            if (string.IsNullOrWhiteSpace(task.Kind) || task.Id == Guid.Empty ||
                task.Sequence < 0 || task.Sequence >= sim.NextSequence ||
                string.IsNullOrWhiteSpace(task.ControllerId) ||
                !state.Forces.ContainsKey(task.ForceId) || !state.Officers.ContainsKey(task.OfficerId) || !state.Settlements.ContainsKey(task.OriginSettlementId) ||
                task.Started.DayNumber < sim.Started.DayNumber || task.Started.DayNumber > state.Clock.DayNumber ||
                task.Due.DayNumber < task.Started.DayNumber || task.Due.DayNumber < state.Clock.DayNumber || task.ProgressDays < 0 ||
                task.ProgressDays != state.Clock.DayNumber - task.Started.DayNumber)
                throw new ArgumentException("Invalid persistent task.");
            if (task.Reinforcements is { } troops &&
                (troops.Soldiers is 0 or > 3000 || troops.Morale is < 0 or > 100 ||
                 !state.Settlements.TryGetValue(troops.DemobilizationCityId, out var muster) || muster.Kind != San9.Core.Domain.SettlementKind.City ||
                 !(task.Kind == UnitStrategies.Kind && task.Journey?.UnitStrategy?.Strategy == UnitStrategyKind.Reinforce ||
                   task.Kind == PersonnelReturns.Kind && task.Journey?.ReturnLeg is not null)))
                throw new ArgumentException("Invalid reinforcement transport.");
            if (task.Journey is { } journey && (journey.DestinationPlaceId < 0 || journey.SuccessRoll is < 0 or > 99 ||
                journey.DestinationPosition is { X: < 0 } or { Y: < 0 } ||
                journey.UnitStrategy is { } strategy && (!Enum.IsDefined(strategy.Strategy) || strategy.TargetUnitId < 0 || journey.DestinationPosition is null ||
                    (strategy.Strategy == UnitStrategyKind.Reinforce
                        ? task.Reinforcements is not { Soldiers: 3000 } : task.Reinforcements is not null)) ||
                journey.Burn is { } burn && (burn.TargetSettlementId < 0 || journey.UnitStrategy is not null || journey.Seize is not null || journey.Gossip is not null || journey.DestinationPosition is null) ||
                journey.Seize is { } seize && (seize.TargetSettlementId < 0 || journey.UnitStrategy is not null || journey.Burn is not null || journey.Gossip is not null || journey.DestinationPosition is null) ||
                journey.Gossip is { } gossip && (gossip.FirstTargetForceId < 0 || gossip.SecondTargetForceId < 0 ||
                    gossip.FirstTargetForceId == gossip.SecondTargetForceId || journey.UnitStrategy is not null ||
                    journey.Burn is not null || journey.Seize is not null || journey.TargetOfficerId is not null) ||
                journey.TargetOfficerId is int target && !state.Officers.ContainsKey(target)))
                throw new ArgumentException("Invalid personnel journey.");
        }
    }

}
