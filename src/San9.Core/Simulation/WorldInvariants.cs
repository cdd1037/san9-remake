namespace San9.Core.Simulation;

/// <summary>Validates the complete candidate state at initialization and publication.</summary>
internal static class WorldInvariants
{
    internal static void Validate(WorldMutation state)
    {
        _ = new GameClock(state.Clock.Year, state.Clock.Month, state.Clock.Day);
        ValidateParticipation(state);
        ValidateSimulation(state);
        var titles = state.AllForces.Values.Where(f => f.CountryTitleId is not null).Select(f => f.CountryTitleId!.Value).ToArray();
        if (titles.Any(id => id < 0) || titles.Distinct().Count() != titles.Length) throw new ArgumentException("Invalid country title ownership.");
        foreach (var force in state.AllForces.Values)
        {
            if (force.Origin is { } origin)
            {
                _ = new GameClock(origin.Clock.Year, origin.Clock.Month, origin.Clock.Day);
                if (!Enum.IsDefined(origin.Kind) || !origin.Clock.IsXunBoundary ||
                    (origin.Kind == San9.Core.Domain.ForceCreationKind.EmptyCityUprising) != (origin.PredecessorId is null) ||
                    origin.PredecessorId is int predecessor && (!state.AllForces.ContainsKey(predecessor) || predecessor >= force.Id) ||
                    !state.AllOfficers.ContainsKey(origin.FounderId) ||
                    !state.AllSettlements.TryGetValue(origin.SettlementId, out var foundingCity) ||
                    foundingCity.Kind != San9.Core.Domain.SettlementKind.City)
                    throw new ArgumentException("Invalid force origin.");
            }
            if (force.IsEliminated && (force.CountryTitleId is not null || force.LeaderId is not null || force.AdviserId is not null || force.Money != 0 || force.Food != 0 ||
                state.AllOfficers.Values.Any(p => p.OwnerId == force.Id) || state.AllSettlements.Values.Any(s => s.OwnerId == force.Id) ||
                state.Units.Values.Any(u => u.OwnerId == force.Id) || state.AllCorps.Values.Any(c => c.OwnerId == force.Id) ||
                state.Simulation.Controls.Any(c => c.ForceId == force.Id))) throw new ArgumentException("Eliminated force retains active references.");
            if (force.EventHistory.IsDefault || force.EventHistory.Any(k => string.IsNullOrWhiteSpace(k.Value)) ||
                !force.EventHistory.Select(k => k.Value).SequenceEqual(force.EventHistory.Select(k => k.Value).Distinct().Order(StringComparer.Ordinal)))
                throw new ArgumentException("Invalid force event history.");
            if (force.Id < 0 || force.Trust is < 0 or > 1000 || force.BaseTrust is < 0 or > 1000 || force.ProvidedTactics.IsDefault || force.ProvidedTactics.Any(t => t < 0) || force.ProvidedTactics.Distinct().Count() != force.ProvidedTactics.Length) throw new ArgumentException("Invalid force.");
        }
        foreach (var settlement in state.AllSettlements.Values)
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
            if (settlement.Id < 0 || !Enum.IsDefined(settlement.Kind) || settlement.OwnerId is int owner && !state.AllForces.ContainsKey(owner) ||
                settlement.SoldierCapacity is 0 or > 500000 || settlement.Soldiers > settlement.SoldierCapacity || settlement.Population > 1000000 || settlement.MaxCommerce > 2500 || settlement.MaxCultivation > 4000 || settlement.MaxDurability <= 0 || settlement.Durability < 0 || settlement.Durability > settlement.MaxDurability ||
                settlement.Morale is < 0 or > 100 || settlement.Commerce < 0 || settlement.Commerce > settlement.MaxCommerce ||
                settlement.Cultivation < 0 || settlement.Cultivation > settlement.MaxCultivation || settlement.PublicOrder is < 0 or > 1000)
                throw new ArgumentException("Invalid settlement.");
        }
        foreach (var officer in state.AllOfficers.Values)
        {
            if (!officer.IsValid() || officer.ForceLoyalties.Keys.Any(id => !state.AllForces.ContainsKey(id)) ||
                officer.SettlementId is int location && !state.AllSettlements.ContainsKey(location) ||
                officer.OwnerId is int owner && !state.AllForces.ContainsKey(owner) ||
                officer.UnitId is int unitId && (!state.Units.TryGetValue(unitId, out var unit) ||
                    (officer.Status == San9.Core.Domain.OfficerStatus.Captive ? unit.OwnerId == officer.OwnerId : unit.OwnerId != officer.OwnerId)) ||
                officer.IsServing && officer.SettlementId is int home && state.AllSettlements[home].OwnerId != officer.OwnerId) throw new ArgumentException("Invalid officer.");
        }
        if (state.AllForces.Values.Any(f => f.AdviserId is int adviser && (!state.AllOfficers.TryGetValue(adviser, out var p) || p.OwnerId != f.Id || p.Status != San9.Core.Domain.OfficerStatus.Strategist)) ||
            state.AllOfficers.Values.Any(p => p.Status == San9.Core.Domain.OfficerStatus.Strategist &&
                (p.OwnerId is not int owner || state.AllForces[owner].AdviserId != p.Id)))
            throw new ArgumentException("Invalid force adviser relationship.");
        if (state.AllForces.Values.Any(f => f.LeaderId is int leader &&
                (!state.AllOfficers.TryGetValue(leader, out var officer) || officer.OwnerId != f.Id)) ||
            state.AllSettlements.Values.Any(s => s.CorpsId < 0 || s.CorpsId is int corpsId && (!state.AllCorps.TryGetValue(corpsId, out var corps) || corps.OwnerId != s.OwnerId)) ||
            state.AllSettlements.Values.Where(s => s.CorpsId is not null).GroupBy(s => s.CorpsId)
                .Any(g => g.Select(s => s.OwnerId).Distinct().Count() != 1))
            throw new ArgumentException("Invalid leader or corps relationship.");
        foreach (var unit in state.Units.Values)
        {
            if (!unit.IsValid || !state.AllForces.ContainsKey(unit.OwnerId) ||
                unit.MemberRanks.Keys.Any(id => !state.AllOfficers.TryGetValue(id, out var person) || person.UnitId != unit.Id || !person.IsServing) ||
                unit.Order?.SettlementId is int target && !state.AllSettlements.ContainsKey(target) ||
                unit.Order?.UnitId is int targetUnit && !state.Units.ContainsKey(targetUnit) ||
                unit.EngagementTargetId is int engagement && !state.Units.ContainsKey(engagement) ||
                unit.CommanderId is int commander && (!state.AllOfficers.TryGetValue(commander, out var leader) ||
                    !leader.IsServing || leader.UnitId != unit.Id || leader.OwnerId != unit.OwnerId) ||
                unit.CorpsId is int corpsId && (!state.AllCorps.TryGetValue(corpsId, out var corps) || corps.OwnerId != unit.OwnerId)) throw new ArgumentException("Invalid unit.");
        }
        if (state.AllCorps.Values.Any(c => c.Id < 0 || !Enum.IsDefined(c.ControlMode) || !ValidDelegation(state, c) || !state.AllForces.ContainsKey(c.OwnerId) ||
                !state.AllOfficers.TryGetValue(c.LeaderId, out var leader) || leader.OwnerId != c.OwnerId ||
                !leader.IsServing && !(c.Treasury is null && leader.Status == San9.Core.Domain.OfficerStatus.Captive &&
                    state.AllForces[c.OwnerId].LeaderId == leader.Id)) ||
            state.AllTreasures.Values.Any(t => !t.IsValid || t.HolderId is int holder && !state.AllOfficers.ContainsKey(holder)) ||
            state.AllRelations.Values.Any(r => r.FirstId >= r.SecondId || r.Value is < 0 or > 100 || !state.AllForces.ContainsKey(r.FirstId) || !state.AllForces.ContainsKey(r.SecondId)))
            throw new ArgumentException("Invalid information state relationships.");
    }
    private static void ValidateParticipation(WorldMutation state)
    {
        if (state.AllForces.Values.Any(f => f.IsDormant && f.IsEliminated) ||
            state.AllSettlements.Values.Any(s => s.IsDormant && s.IsDestroyed ||
                s.OwnerId is int owner && state.AllForces.TryGetValue(owner, out var force) && force.IsDormant && !s.IsDormant) ||
            state.AllOfficers.Values.Any(p => p.IsDormant && p.UnitId is not null ||
                !p.IsDormant && (p.OwnerId is int owner && state.AllForces.TryGetValue(owner, out var force) && force.IsDormant ||
                    p.SettlementId is int place && state.AllSettlements.TryGetValue(place, out var settlement) && settlement.IsDormant)) ||
            state.AllForces.Values.Any(f => !f.IsDormant &&
                (f.LeaderId is int leader && state.AllOfficers.TryGetValue(leader, out var p) && p.IsDormant ||
                 f.AdviserId is int adviser && state.AllOfficers.TryGetValue(adviser, out var a) && a.IsDormant)) ||
            state.Units.Values.Any(u => !state.Forces.ContainsKey(u.OwnerId) ||
                u.MemberRanks.Keys.Any(id => !state.Officers.ContainsKey(id)) ||
                u.CommanderId is int commander && !state.Officers.ContainsKey(commander) ||
                u.Order?.SettlementId is int target && !state.Settlements.ContainsKey(target) ||
                u.ServicePlaceId is int home && state.AllSettlements.TryGetValue(home, out var place) && place.IsDormant) ||
            state.Corps.Values.Any(c => !state.Officers.ContainsKey(c.LeaderId)) ||
            state.Simulation.Controls.Any(c => !state.Forces.ContainsKey(c.ForceId)) ||
            state.Simulation.Tasks.Any(t => !state.Forces.ContainsKey(t.ForceId) ||
                !state.Officers.ContainsKey(t.OfficerId) || !state.Settlements.ContainsKey(t.OriginSettlementId)))
            throw new ArgumentException("Dormant objects cannot participate in the active world.");
    }

    private static bool ValidDelegation(WorldMutation state, San9.Core.Domain.CorpsState corps) =>
        corps.Treasury is null ? corps.Delegation is San9.Core.Domain.GeneralDelegation : corps.Delegation switch
        {
            San9.Core.Domain.GeneralDelegation or San9.Core.Domain.DomesticDelegation => true,
            San9.Core.Domain.DefeatForceDelegation target => target.ForceId != corps.OwnerId &&
                state.AllForces.TryGetValue(target.ForceId, out var force) && !force.IsEliminated,
            San9.Core.Domain.CaptureSettlementDelegation target => state.AllSettlements.TryGetValue(target.SettlementId, out var place) && !place.IsDestroyed,
            _ => false
        };
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
            sim.Boundary != SimulationBoundary.Terminal && (!state.Clock.IsXunBoundary && sim.PendingDailyTask is null && sim.PendingDuel is null || sim.TerminalReason is not null) ||
            sim.Boundary == SimulationBoundary.Terminal && (string.IsNullOrWhiteSpace(sim.TerminalReason) || !sim.Tasks.IsEmpty) ||
            sim.RandomStreams.Any(s => string.IsNullOrWhiteSpace(s.Name)) ||
            !sim.RandomStreams.Select(s => s.Name).SequenceEqual(sim.RandomStreams.Select(s => s.Name).Distinct().Order(StringComparer.Ordinal)))
            throw new ArgumentException("Invalid simulation boundary, control, sequence or random state.");
        if (sim.ThreeVisitsStarted is { } started)
        {
            _ = new GameClock(started.Year, started.Month, started.Day);
            if (!started.IsXunBoundary || started.DayNumber > state.Clock.DayNumber)
                throw new ArgumentException("Invalid Three Visits starting date.");
        }
        int pendingDecisions = (sim.PendingEvent is null ? 0 : 1) + (sim.PendingDailyTask is null ? 0 : 1) +
            (sim.PendingDuel is null ? 0 : 1) + (sim.PendingExecutionSuccession is null ? 0 : 1) + (sim.PendingTacticStudy is null ? 0 : 1);
        if (pendingDecisions > 1 || (sim.Boundary == SimulationBoundary.AwaitingDecision) != (pendingDecisions == 1))
            throw new ArgumentException("Decision boundary requires exactly one pending decision.");
        AllianceFormation.ValidatePending(state);
        OfficeRequests.ValidatePending(state);
        EmperorTransfer.ValidateState(state);
        PoliticalEvents.ValidatePending(state);
        if (state.Simulation.ConsumedEventKeys.IsDefault ||
            state.Simulation.ConsumedEventKeys.Any(k => string.IsNullOrWhiteSpace(k.Value)) ||
            !state.Simulation.ConsumedEventKeys.Select(k => k.Value).SequenceEqual(
                state.Simulation.ConsumedEventKeys.Select(k => k.Value).Distinct().Order(StringComparer.Ordinal)))
            throw new ArgumentException("Invalid consumed event keys.");
        if (sim.PendingEvent is not (null or AllianceEventSession or OfficeRequestEventSession or CountryTitleEventSession or EmperorTransferEventSession))
            throw new ArgumentException("Unknown event session.");
        PrisonerExecution.ValidatePending(state);
        DuelCombat.ValidatePending(state);
        TacticStudy.ValidatePending(state);
        if (sim.PendingDailyTask is { } pending)
        {
            _ = new GameClock(pending.XunStarted.Year, pending.XunStarted.Month, pending.XunStarted.Day);
            if (!Enum.IsDefined(pending.Phase) || !pending.XunStarted.IsXunBoundary || pending.XunStarted.DayNumber < sim.Started.DayNumber ||
                (pending.Phase == TaskResolutionPhase.XunBoundary ? state.Clock != pending.XunStarted.Advance() :
                    state.Clock.DayNumber < pending.XunStarted.DayNumber || state.Clock.DayNumber >= pending.XunStarted.Advance().DayNumber) ||
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
        if (sim.Tasks.Where(t => t.Kind == TacticStudy.Kind).Select(t => t.OriginSettlementId).Distinct().Count() !=
            sim.Tasks.Count(t => t.Kind == TacticStudy.Kind)) throw new ArgumentException("Duplicate city study.");
        foreach (var task in sim.Tasks)
        {
            if ((task.Kind == PersonnelMissions.SearchKind && task.ResolutionStep > 0) != (task.SearchEncounter is not null))
                throw new ArgumentException("Search resolution step requires its selected encounter.");
            if (task.SearchEncounter is { } encounter &&
                (task.Kind != PersonnelMissions.SearchKind || task.ResolutionStep != (encounter is ShanyangGongEncounter { ChoosingCountryTitle: true } ? 2 : 1) ||
                    (encounter switch
                    {
                        PersonSearchEncounter person => person.OfficerId < 0 || !state.Officers.ContainsKey(person.OfficerId),
                        ShanyangGongEncounter => false,
                        BeastSearchEncounter beast => string.IsNullOrWhiteSpace(beast.EventId),
                        ScholarReturnEncounter scholar => string.IsNullOrWhiteSpace(scholar.EventId),
                        ScholarInsultEncounter insult => string.IsNullOrWhiteSpace(insult.EventId),
                        LongevityEncounter longevity => string.IsNullOrWhiteSpace(longevity.EventId),
                        TreasureMonumentEncounter monument => string.IsNullOrWhiteSpace(monument.EventId),
                        _ => true
                    }) ||
                    sim.PendingDailyTask is not { } pendingSearch || pendingSearch.RemainingTaskIds[0] != task.Id))
                throw new ArgumentException("Invalid search encounter context.");
            if ((task.Kind == TacticStudy.Kind) != (task.StudyTacticId is not null) || task.StudyTacticId < 0 || task.Kind == TacticStudy.Kind && task.Started.DayNumber - sim.Started.DayNumber < 30)
                throw new ArgumentException("Invalid study task payload.");
            if (task.Surrender is { } surrender && (task.Kind != SurrenderDiplomacy.Kind || task.Gift is not null ||
                task.PrisonerReturn is not null || task.AttackRequest is not null || !state.Forces.ContainsKey(surrender.TargetForceId) ||
                surrender.TargetForceId == task.ForceId)) throw new ArgumentException("Invalid surrender mission.");
            if (task.AttackRequest is { } request && (task.Kind != AttackRequestDiplomacy.Kind || task.Gift is not null ||
                task.PrisonerReturn is not null || !state.Forces.ContainsKey(request.TargetForceId) || request.TargetForceId == task.ForceId ||
                !state.Settlements.ContainsKey(request.TargetSettlementId) || request.Offer is { IsValid: false } ||
                request.Offer?.PrisonerId is int other && !state.Officers.ContainsKey(other) ||
                request.Offer?.TreasureId is int treasure && !state.Treasures.ContainsKey(treasure)))
                throw new ArgumentException("Invalid attack request mission.");
            if (task.PrisonerReturn is { } mission && (task.Kind != PrisonerReturnDiplomacy.Kind || task.Gift is not null ||
                !state.Forces.ContainsKey(mission.TargetForceId) || mission.TargetForceId == task.ForceId ||
                !state.Officers.ContainsKey(mission.PrisonerId) || mission.Offer is { IsValid: false } ||
                mission.Offer?.PrisonerId is int exchange && !state.Officers.ContainsKey(exchange) ||
                mission.Offer?.TreasureId is int item && !state.Treasures.ContainsKey(item)))
                throw new ArgumentException("Invalid prisoner return mission.");
            if (task.ResolutionStep < 0 || task.ResolutionStep > 0 &&
                (sim.PendingDailyTask is not { } active || active.RemainingTaskIds[0] != task.Id || task.Due != state.Clock))
                throw new ArgumentException("Invalid task resolution step.");
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
                journey.Route is null || journey.Route.Origin is { X: < 0 } or { Y: < 0 } ||
                journey.Route.Destination is { X: < 0 } or { Y: < 0 } ||
                journey.UnitStrategy is { } strategy && (!Enum.IsDefined(strategy.Strategy) || strategy.TargetUnitId < 0 ||
                    (strategy.Strategy == UnitStrategyKind.Reinforce
                        ? task.Reinforcements is not { Soldiers: 3000 } : task.Reinforcements is not null)) ||
                journey.Burn is { } burn && (burn.TargetSettlementId < 0 || journey.UnitStrategy is not null || journey.Seize is not null || journey.Gossip is not null) ||
                journey.Seize is { } seize && (seize.TargetSettlementId < 0 || journey.UnitStrategy is not null || journey.Burn is not null || journey.Gossip is not null) ||
                journey.Gossip is { } gossip && (gossip.FirstTargetForceId < 0 || gossip.SecondTargetForceId < 0 ||
                    gossip.FirstTargetForceId == gossip.SecondTargetForceId || journey.UnitStrategy is not null ||
                    journey.Burn is not null || journey.Seize is not null || journey.TargetOfficerId is not null) ||
                journey.TargetOfficerId is int target && !state.Officers.ContainsKey(target)))
                throw new ArgumentException("Invalid personnel journey.");
        }
    }

}
