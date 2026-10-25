using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record BattleDamageEvent(GameClock Clock, int AttackerId, int? TargetUnitId, int? SettlementId,
    uint SoldierLoss, uint AddedWounded, uint Dead, int DurabilityLoss, int MoraleLoss, bool AttackerIsSettlement = false) : DomainEvent
{
    public int? TacticId { get; init; }
    public bool IsTacticExchange { get; init; }
    public bool ContinuesSiege { get; init; }
    public uint? CalculatedSoldierLoss { get; init; }
    public BattleObjectContext? Source { get; init; }
    public BattleObjectContext? Target { get; init; }
}

internal static class MilitaryCombat
{
    // Copy the remaining day only when a decision actually suspends this exchange.
    private sealed record Exchange(GameClock XunStarted, BattleSource Source, BattleSource Target,
        ImmutableArray<int> AffectedForces, Func<CombatDayContinuation> CaptureDay, int? PreviousSettlementOwnerId = null)
    {
        internal CombatExchangeContinuation Capture() => new(XunStarted, CaptureDay(), Source, Target,
            AffectedForces, PreviousSettlementOwnerId);
    }

    internal static bool CanInitiate(UnitState unit) => !unit.IsEmbedded && !unit.IsPersonnel &&
        unit.FormationId is not null && unit.Soldiers > 0 && unit.Morale > 0 && unit.DepartureDaysRemaining == 0 &&
        unit.Condition is not (UnitCondition.Confused or UnitCondition.Routed);

    internal static uint NormalDamage(WorldMutation world, BattleSource source, BattleSource target, MilitaryRulebook military)
    {
        CombatStrength StrengthOf(BattleSource actor, BattleSource opponent) =>
            (actor.IsSettlement ? GarrisonStrength(world, world.Settlements[actor.Id]) : Strength(world, world.Units[actor.Id]))
            with
            { Intensity = MilitaryEngagements.Intensity(world, actor, opponent, military) };
        CombatProfile Profile(BattleSource actor) => actor.IsSettlement ? military.Place(world.Settlements[actor.Id]).Combat! :
            military.CombatFormation(world.Units[actor.Id])!;
        return CombatMath.TroopDamage(StrengthOf(source, target), StrengthOf(target, source), Profile(source), Profile(target),
            target.IsSettlement, target.IsSettlement ? world.Settlements[target.Id].Durability : 0,
            source.IsSettlement && military.FacilityAttack(world.Settlements[source.Id]).UsesRockfallPower);
    }

    internal static bool ValidTarget(IWorldQuery world, int owner, MilitaryOrder order, FormationRule formation, MilitaryRulebook rules)
    {
        if (formation.Combat is null || formation.Attack is null) return false;
        if (order.Kind == MilitaryOrderKind.AttackUnit)
            return order.UnitId is int id && world.Units.TryGetValue(id, out var unit) && MilitaryDiplomacy.CanFight(world, owner, unit.OwnerId) &&
                unit.FormationId is not null && rules.Formation(unit).Combat is not null && rules.CanAttackTarget(formation.Attack, Area(unit, rules), false);
        return order.Kind == MilitaryOrderKind.AttackSettlement && order.SettlementId is int place &&
            world.Settlements.TryGetValue(place, out var settlement) && !settlement.IsDestroyed && settlement.Facility?.BuilderUnitId is null &&
            rules.TryPlace(settlement, out var definition) && definition.Combat is not null &&
            (MilitaryDiplomacy.CanFight(world, owner, settlement.OwnerId) || settlement.OwnerId == owner &&
                definition.FacilityTypeId is int type && rules.Facilities[type].IsWork) &&
            rules.CanAttackTarget(formation.Attack, rules.BuildingArea(definition), true);
    }

    internal static int UnitRange(IWorldQuery world, UnitState source, MilitaryOrder target, MilitaryRulebook rules, WorldPosition? position = null)
        => UnitRange(world, source, TargetArea(world, target, rules), target.SettlementId, rules, position);

    private static int UnitRange(IWorldQuery world, UnitState source, MapOccupancy target, int? settlementId,
        MilitaryRulebook rules, WorldPosition? position = null)
    {
        var actualPosition = position ?? source.Position;
        var attack = rules.Formation(source, actualPosition).Attack;
        return rules.AttackRange(attack, Area(source, rules, actualPosition), target, settlementId is not null,
            attack?.RequiresWaterContact == true ? point => world.BlocksWaterContact(rules, point, settlementId) : null);
    }

    internal static int FacilityRange(IWorldQuery world, SettlementState source, UnitState target, MilitaryRulebook rules) =>
        rules.FacilityAttackRange(source, target.Position, world.Settlements.Values);

    internal static MapOccupancy TargetArea(IWorldQuery world, MilitaryOrder order, MilitaryRulebook rules) =>
        order.UnitId is int id ? Area(world.Units[id], rules) :
        rules.SettlementArea(world.Settlements[order.SettlementId!.Value]);

    internal static MapOccupancy Area(UnitState unit, MilitaryRulebook rules, WorldPosition? position = null) =>
        new(position ?? unit.Position, rules.UnitFootprint(unit, position));

    internal static int WallDamage(WorldMutation world, UnitState unit, SettlementState target, MilitaryRulebook rules)
    {
        var attack = rules.Formation(unit).Attack;
        if (attack?.DamagesWalls != true || unit.Condition is not (UnitCondition.Normal or UnitCondition.HighSpirits)) return 0;
        bool close = Area(unit, rules).IntersectsExpanded(new BattleSource(target.Id, true).Area(world, rules), MapCoordinates.UnitsPerCell);
        if (!close && !attack.RangedSiege) return 0;
        int damage = CombatMath.DurabilityDamage(unit.Soldiers, Strength(world, unit).Leadership,
            rules.CombatFormation(unit)!.AgainstWalls, target.Durability);
        return close ? damage : Math.Min(damage, Math.Max(0, target.Durability - 1));
    }

    private static bool RequiresCloseSiege(SettlementState target) => target.Soldiers < 1000 || target.Durability < 20;

    internal static int ApproachRange(IWorldQuery world, UnitState source, MilitaryOrder target, MilitaryRulebook rules, WorldPosition position)
    {
        var formation = rules.Formation(source, position);
        if (formation.Attack?.RequiresWaterContact == true) return UnitRange(world, source, target, rules, position);
        if (formation.Attack?.RangedSiege != true || rules.Map.IsWater(position)) return 1;
        int reach = UnitRange(world, source, target, rules, position);
        var area = Area(source, rules, position);
        bool targetCanReply;
        if (target.SettlementId is int id)
        {
            var place = world.Settlements[id];
            if (RequiresCloseSiege(place)) return 1;
            targetCanReply = new BattleSource(id, true).Area(world, rules).IntersectsExpanded(area,
                FacilityRange(world, place, source with { Position = position }, rules));
        }
        else
        {
            var unit = world.Units[target.UnitId!.Value];
            targetCanReply = Area(unit, rules).IntersectsExpanded(area, UnitRange(world, unit, area, null, rules));
        }
        return targetCanReply ? 1 : reach;
    }

    internal static void AdvanceDay(WorldMutation world, SimulationRules rules, List<DomainEvent> events,
        GameClock? xunStarted = null, CombatDayContinuation? continuation = null)
    {
        if (rules.Military is not { } military) return;
        // These sets track initiated attacks only; replying never consumes the day's own attack.
        var used = (continuation?.UsedUnits ?? []).ToHashSet();
        var buildingsUsed = (continuation?.UsedBuildings ?? []).ToHashSet();
        var units = continuation?.RemainingUnits ?? world.Units.Keys.Order().ToImmutableArray();
        for (int index = 0; index < units.Length; index++)
        {
            int id = units[index];
            world.MaintainLeadership(events);
            if (world.Simulation.TerminalReason is not null) break;
            if (!world.Units.TryGetValue(id, out var attacker) || !CanInitiate(attacker) || attacker.EffectiveOrder is not { IsAttack: true } order) continue;
            var formation = military.Formation(attacker);
            if (!ValidTarget(world, attacker.OwnerId, order, formation, military))
            {
                if (attacker.ContactTarget is not null)
                {
                    world.Replace(attacker with { ContactTarget = null });
                    continue;
                }
                MilitaryOrders.ResolveOrder(world, attacker, military, events);
                continue;
            }
            var targetArea = TargetArea(world, order, military);
            if (used.Contains(id) || !Area(attacker, military).IntersectsExpanded(targetArea, UnitRange(world, attacker, order, military))) continue;
            used.Add(id);
            MilitaryEngagements.Open(world, new(id, false), new(order.UnitId ?? order.SettlementId!.Value, order.UnitId is null), military);
            attacker = world.Units[id];
            if (order.SettlementId is int requestTarget) AttackRequests.Fulfill(world, attacker.OwnerId, requestTarget, events);
            MilitaryDiplomacy.CombatOpened(world, attacker.OwnerId, MilitaryDiplomacy.TargetOwner(world, order),
                order.SettlementId is int attackedPlace && MilitaryDiplomacy.Work(world, attackedPlace, military), events);
            var source = new BattleSource(id, false);
            var target = new BattleSource(order.UnitId ?? order.SettlementId!.Value, order.UnitId is null);
            CombatDayContinuation CaptureDay() => new(units.Skip(index + 1).ToImmutableArray(),
                used.Order().ToImmutableArray(), buildingsUsed.Order().ToImmutableArray());
            if (!BeginExchange(world, source, target, rules, events, xunStarted ?? world.Clock, CaptureDay)) return;
        }
        FacilityCombat.AdvanceDay(world, rules, used, buildingsUsed, events, xunStarted, continuation?.RemainingBuildings);
    }

    internal static bool BeginExchange(WorldMutation world, BattleSource source, BattleSource target,
        SimulationRules rules, List<DomainEvent> events, GameClock xunStarted, Func<CombatDayContinuation> captureDay)
    {
        var military = rules.Military!;
        int? previousOwner = target.IsSettlement ? world.Settlements[target.Id].OwnerId : null;
        var affected = new[] { source.IsSettlement ? world.Settlements[source.Id].OwnerId : source.Owner(world),
            target.IsSettlement ? world.Settlements[target.Id].OwnerId : target.Owner(world) }.OfType<int>().Distinct().Order().ToImmutableArray();
        var exchange = new Exchange(xunStarted, source, target, affected, captureDay, previousOwner);
        bool counter = CanReply(world, target, source, military);
        // The pair is captured before any casualty, condition change, duel or succession.
        var damage = new CombatExchangeDamage(NormalDamage(world, source, target, military),
            counter ? NormalDamage(world, target, source, military) : 0);
        var siegeUnit = source.IsSettlement ? target : source;
        var siegeTarget = source.IsSettlement ? source : target;
        if (siegeTarget.IsSettlement && (!source.IsSettlement || counter))
        {
            var unit = world.Units[siegeUnit.Id];
            var settlement = world.Settlements[siegeTarget.Id];
            int walls = WallDamage(world, unit, settlement, military);
            if (walls > 0)
            {
                DamageSettlement(world, settlement, 0, military.Place(settlement).Combat!, unit.Id, events, walls);
                damage = source.IsSettlement ? damage with { IncomingContinuesSiege = true } :
                    damage with { OutgoingContinuesSiege = true };
                if (world.Settlements[settlement.Id].Durability > 0) BattleLearning.Siege(world, unit.Id, events);
                if (!source.IsSettlement && !IsDefeated(world, siegeTarget) &&
                    military.Place(settlement).FacilityTypeId is int type && military.Facilities[type].Reprisal is { } reprisal)
                    FacilityCombat.ApplyReprisal(world, world.Settlements[settlement.Id], world.Units[unit.Id],
                        reprisal, military.CombatFormation(unit)!, events);
            }
        }
        if (!IsDefeated(world, source) && !IsDefeated(world, target))
        {
            if (!FinishOpportunities(world, rules, events, exchange, damage, counter)) return false;
        }
        else
        {
            if (IsDefeated(world, siegeUnit) && !IsDefeated(world, siegeTarget))
                CommitOrdinary(world, source, target, damage, military, events, isTacticExchange: false);
            CompleteLosses(world, source, target, rules, events);
        }
        CompleteExchange(world, exchange, rules, events);
        return true;
    }

    private static bool CanReply(WorldMutation world, BattleSource source, BattleSource target, MilitaryRulebook military)
    {
        if (source.IsSettlement)
        {
            var place = world.Settlements[source.Id];
            return place.OwnerId is not null && place.Morale > 0 && place.ConfusionDaysRemaining == 0 &&
                military.FacilityAttack(place).Attack is not null && source.Area(world, military).IntersectsExpanded(
                    target.Area(world, military), FacilityRange(world, place, world.Units[target.Id], military));
        }
        var unit = world.Units[source.Id];
        return unit.Condition is not (UnitCondition.Confused or UnitCondition.Routed) && military.Formation(unit).Attack is not null &&
            source.Area(world, military).IntersectsExpanded(target.Area(world, military), UnitRange(world, unit,
                TargetOrder(target), military));
    }

    internal static MilitaryOrder TargetOrder(BattleSource target) => target.IsSettlement
        ? new(MilitaryOrderKind.AttackSettlement, null, target.Id)
        : new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = target.Id };

    private static bool FinishOpportunities(WorldMutation world, SimulationRules rules, List<DomainEvent> events,
        Exchange exchange, CombatExchangeDamage damage, bool counterAllowed, bool counterOnly = false)
    {
        for (int opportunity = counterOnly ? 1 : 0; opportunity < (counterAllowed ? 2 : 1) && world.Simulation.TerminalReason is null; opportunity++)
        {
            bool counter = opportunity == 1;
            var source = counter ? exchange.Target : exchange.Source;
            var target = counter ? exchange.Source : exchange.Target;
            // Neutral places and unstaffed works remain targets even though they cannot act as battle sources.
            if (!source.IsPresent(world) || !target.IsPresent(world) || IsDefeated(world, source) || IsDefeated(world, target)) break;
            var oriented = counter ? damage.Reverse() : damage;
            bool resolved;
            if (!source.IsSettlement && !target.IsSettlement)
            {
                var result = DuelCombat.TryOpportunity(world, source.Id, target.Id, rules, events, counter, oriented);
                resolved = result.Resolved;
                if (result.Progress is { } progress)
                {
                    int owner = world.Officers[progress.PendingRulerId!.Value].OwnerId!.Value;
                    world.SetSimulation(world.Simulation with
                    {
                        Boundary = SimulationBoundary.AwaitingDecision,
                        PendingDuel = new(exchange.XunStarted, exchange.CaptureDay(), exchange.Source.Id, exchange.Target.Id,
                            exchange.AffectedForces, counter, result.TacticReady, progress, owner, damage)
                    });
                    return false;
                }
            }
            else resolved = source.IsSettlement
                ? FacilityCombat.TryTactic(world, source.Id, TargetOrder(target), rules, events, counter, oriented)
                : TacticCombat.TryUnit(world, source.Id, TargetOrder(target), rules, events, counter, oriented);
            // Sniping is an aftermath of an already committed martial exchange.
            if (world.Simulation.PendingSniping is not null && SnipingSuccession.CaptureExchange(world, exchange.Capture())) return false;
            if (resolved) return true;
        }
        CommitOrdinary(world, exchange.Source, exchange.Target, damage, rules.Military!, events, isTacticExchange: false);
        CompleteLosses(world, exchange.Source, exchange.Target, rules, events);
        return true;
    }

    private static void CompleteExchange(WorldMutation world, Exchange exchange,
        SimulationRules rules, List<DomainEvent> events)
    {
        BattleRouting.CompleteExchange(world, exchange.Source, exchange.Target, rules, events);
        if (exchange.Target.IsSettlement)
            CityBattleEffects.CompleteAttack(world, exchange.Source.Id, exchange.Target.Id, exchange.PreviousSettlementOwnerId, events);
        GovernanceLifecycle.Reconcile(world, exchange.AffectedForces.ToHashSet(), rules, events, world.CurrentDateCommandsConsumed);
        ResolutionPipeline.CheckConquest(world, events);
    }

    internal static void ResumeDuel(WorldMutation world, SimulationRules rules, List<DomainEvent> events, int successorId)
    {
        var pending = world.Simulation.PendingDuel!;
        var progress = DuelAftermath.Advance(world, pending.Aftermath, rules, events, successorId);
        if (progress is not null)
        {
            world.SetSimulation(world.Simulation with
            {
                PendingDuel = pending with
                {
                    Aftermath = progress,
                    RespondingForceId = world.Officers[progress.PendingRulerId!.Value].OwnerId!.Value
                }
            });
            return;
        }
        world.SetSimulation(world.Simulation with { PendingDuel = null, Boundary = SimulationBoundary.Stable });
        int source = pending.Counter ? pending.DefendingUnitId : pending.AttackingUnitId;
        int target = pending.Counter ? pending.AttackingUnitId : pending.DefendingUnitId;
        var exchange = new Exchange(pending.XunStarted, new(pending.AttackingUnitId, false),
            new(pending.DefendingUnitId, false), pending.AffectedForces, () => pending.Day);
        bool resolved = pending.TacticReady && TacticCombat.ResolveOpportunity(world, source,
            new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = target }, rules, events,
            pending.Counter ? pending.Damage.Reverse() : pending.Damage);
        if (world.Simulation.PendingSniping is not null && SnipingSuccession.CaptureExchange(world, exchange.Capture())) return;
        if (!resolved && !FinishOpportunities(world, rules, events, exchange, pending.Damage,
            counterAllowed: !pending.Counter, counterOnly: true)) return;
        CompleteExchange(world, exchange, rules, events);
        AdvanceDay(world, rules, events, pending.XunStarted, pending.Day);
    }

    internal static void ResumeSniping(WorldMutation world, SimulationRules rules, List<DomainEvent> events, int successorId)
    {
        var pending = world.Simulation.PendingSniping!;
        var continuation = pending.Exchange!;
        var exchange = new Exchange(continuation.XunStarted, continuation.Source, continuation.Target,
            continuation.AffectedForces, () => continuation.Day, continuation.PreviousSettlementOwnerId);
        world.SetSimulation(world.Simulation with { PendingSniping = null, Boundary = SimulationBoundary.Stable });
        OfficerDeath.Resolve(world, pending.RulerId, OfficerDeathCause.Sniping, rules, events,
            afterDailyCommands: world.CurrentDateCommandsConsumed, successorId: successorId);
        if (!ResolutionPipeline.CheckTerminal(world, events)) return;
        TacticCombat.ResumeAfterSniping(world, pending.Tactic!, rules, events);
        CompleteExchange(world, exchange, rules, events);
        if (!ResolutionPipeline.CheckTerminal(world, events)) return;
        AdvanceDay(world, rules, events, exchange.XunStarted, continuation.Day);
    }

    internal static bool IsDefeated(WorldMutation world, BattleSource source) => source.IsSettlement
        ? Defeated(world.Settlements[source.Id]) : !world.Units.TryGetValue(source.Id, out var unit) || Defeated(unit);

    internal static int? CompleteLosses(WorldMutation world, BattleSource source, BattleSource target,
        SimulationRules rules, List<DomainEvent> events, UnitDefeatCause cause = UnitDefeatCause.Other, bool deferTargetCapture = false)
    {
        bool sourceLost = IsDefeated(world, source), targetLost = IsDefeated(world, target);
        if (targetLost && !target.IsSettlement && world.Units.ContainsKey(target.Id))
            BattleAftermath.TerminateUnit(world, target.Id, sourceLost ? null : source, rules, events, cause);
        if (sourceLost && !source.IsSettlement && world.Units.ContainsKey(source.Id))
            BattleAftermath.TerminateUnit(world, source.Id, targetLost || !target.IsCombatant(world) ? null : target, rules, events);
        if (sourceLost && source.IsSettlement && !targetLost && target.IsCombatant(world))
            BattleAftermath.ResolveSettlementDefeat(world, source.Id, target, rules, events);
        if (targetLost && target.IsSettlement && !sourceLost && source.IsCombatant(world))
        {
            if (deferTargetCapture) return target.Id;
            BattleAftermath.ResolveSettlementDefeat(world, target.Id, source, rules, events);
        }
        return null;
    }

    internal static void CommitOrdinary(WorldMutation world, BattleSource source, BattleSource target,
        CombatExchangeDamage? damage, MilitaryRulebook military, List<DomainEvent> events, bool isTacticExchange = true)
    {
        if (damage is not { } pair) return;
        ApplyDamage(world, source, target, pair.Outgoing, military, events, isTacticExchange: isTacticExchange, continuesSiege: pair.OutgoingContinuesSiege);
        ApplyDamage(world, target, source, pair.Incoming, military, events, isTacticExchange: isTacticExchange, continuesSiege: pair.IncomingContinuesSiege);
    }

    internal static void ApplyDamage(WorldMutation world, BattleSource source, BattleSource target, uint damage,
        MilitaryRulebook military, List<DomainEvent> events, int? tacticId = null, uint? calculatedLoss = null,
        bool isTacticExchange = false, bool continuesSiege = false)
    {
        if (!source.IsPresent(world) || !target.IsPresent(world)) return;
        if (target.IsSettlement)
            DamageSettlement(world, world.Settlements[target.Id], damage, military.Place(world.Settlements[target.Id]).Combat!, source.Id,
                events, attackerIsSettlement: source.IsSettlement, tacticId: tacticId,
                calculatedSoldierLoss: calculatedLoss, isTacticExchange: isTacticExchange, continuesSiege: continuesSiege);
        else DamageUnit(world, world.Units[target.Id], damage, military.CombatFormation(world.Units[target.Id])!, source.Id, events,
            source.IsSettlement, tacticId, calculatedLoss, isTacticExchange, continuesSiege);
    }

    private static bool Defeated(UnitState unit) => unit.Soldiers == 0 || unit.Morale == 0;
    internal static bool Defeated(SettlementState settlement) => !settlement.IsDestroyed && settlement.Facility?.BuilderUnitId is null &&
        (settlement.Durability == 0 || settlement.CanHostPersons && (settlement.Soldiers == 0 || settlement.Morale == 0));
    internal static void DamageSettlement(WorldMutation world, SettlementState settlement, uint damage, CombatProfile profile,
        int attacker, List<DomainEvent> events, int walls = 0, bool attackerIsSettlement = false, int? tacticId = null, uint? calculatedSoldierLoss = null, bool isTacticExchange = false, bool continuesSiege = false)
    {
        walls = Math.Clamp(walls, 0, settlement.Durability);
        damage = FinalDamage(world, new(attacker, attackerIsSettlement), settlement.OwnerId, damage, settlement.Soldiers);
        var loss = CombatMath.Loss(settlement.Soldiers, damage, profile.WoundedPercent, settlement.Wounded, MilitaryRulebook.WoundedCapacity);
        int morale = 0;
        var damaged = settlement with
        {
            Soldiers = loss.RemainingSoldiers,
            Wounded = settlement.Wounded + loss.AddedWounded,
            Morale = settlement.Morale - morale,
            Durability = settlement.Durability - walls
        };
        world.Replace(SiegeContributions.Credit(world, damaged, new(attacker, attackerIsSettlement),
            settlement.Soldiers - loss.RemainingSoldiers, walls));
        events.Add(new BattleDamageEvent(world.Clock, attacker, null, settlement.Id, settlement.Soldiers - loss.RemainingSoldiers,
            loss.AddedWounded, loss.Dead, walls, morale, attackerIsSettlement)
        {
            TacticId = tacticId,
            IsTacticExchange = isTacticExchange,
            ContinuesSiege = continuesSiege,
            CalculatedSoldierLoss = calculatedSoldierLoss,
            Source = BattleObjectContext.Capture(world, new(attacker, attackerIsSettlement)),
            Target = BattleObjectContext.Capture(world, new(settlement.Id, true))
        });
    }
    internal static void DamageUnit(WorldMutation world, UnitState unit, uint damage, CombatProfile profile, int attacker, List<DomainEvent> events,
        bool attackerIsSettlement = false, int? tacticId = null, uint? calculatedSoldierLoss = null, bool isTacticExchange = false, bool continuesSiege = false)
    {
        damage = FinalDamage(world, new(attacker, attackerIsSettlement), unit.OwnerId, damage, unit.Soldiers);
        int morale = 0;
        ApplyUnitLoss(world, unit, damage, morale, profile, new(attacker, attackerIsSettlement), events, tacticId, calculatedSoldierLoss, isTacticExchange, continuesSiege);
    }
    internal static void ApplyUnitLoss(WorldMutation world, UnitState unit, uint damage, int morale, CombatProfile profile,
        BattleSource source, List<DomainEvent> events, int? tacticId = null, uint? calculatedSoldierLoss = null, bool isTacticExchange = false, bool continuesSiege = false)
    {
        damage = Math.Min(damage, unit.Soldiers);
        var loss = CombatMath.Loss(unit.Soldiers, damage, profile.WoundedPercent, unit.Wounded, MilitaryRulebook.WoundedCapacity);
        world.Replace(unit with { Soldiers = loss.RemainingSoldiers, Wounded = unit.Wounded + loss.AddedWounded, Morale = unit.Morale - morale });
        events.Add(new BattleDamageEvent(world.Clock, source.Id, unit.Id, null, damage, loss.AddedWounded, loss.Dead, 0, morale, source.IsSettlement)
        { TacticId = tacticId, IsTacticExchange = isTacticExchange, ContinuesSiege = continuesSiege, CalculatedSoldierLoss = calculatedSoldierLoss, Source = BattleObjectContext.Capture(world, source), Target = BattleObjectContext.Capture(unit) });
    }
    internal static uint FinalDamage(WorldMutation world, BattleSource source, int? targetOwner, uint damage, uint soldiers)
    {
        int? sourceOwner = source.IsSettlement ? world.Settlements[source.Id].OwnerId : world.Units[source.Id].OwnerId;
        bool Player(int? owner) => owner is int id && world.Simulation.Controls.Any(c => c.ForceId == id && c.Kind == ControllerKind.Player);
        int bonus = !Player(sourceOwner) && Player(targetOwner) ? world.Simulation.ComputerDamageBonusPercent : 0;
        return CombatMath.AdjustComputerDamage(damage, soldiers, bonus);
    }
    internal static CombatStrength Strength(WorldMutation world, UnitState unit) => new(unit.Soldiers, unit.Morale,
        Leadership(world, world.ServingUnitMembers(unit.Id)), 1, unit.Condition switch { UnitCondition.Confused => 8, UnitCondition.Routed => 6, _ => 10 });
    internal static CombatStrength GarrisonStrength(WorldMutation world, SettlementState settlement) => new(settlement.Soldiers,
        settlement.Morale, Leadership(world, world.ServingSettlementMembers(settlement.Id)), 1, settlement.ConfusionDaysRemaining > 0 ? 8 : 10);
    private static int Leadership(WorldMutation world, IEnumerable<OfficerState> members) => members
        .Where(p => !world.IsTraveling(p.Id))
        .Select(p => world.Abilities(p).Leadership).DefaultIfEmpty(0).Max();
}
