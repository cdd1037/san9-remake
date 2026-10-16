using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Read-only mission facts shared by preparation and task creation.</summary>
internal sealed record MissionJourneyPlan(int Origin, int Destination, PersonnelRoute Route, GameClock Arrival)
{
    internal static MissionJourneyPlan Strategy(WorldMutation world, PersonnelRulebook rules, int origin,
        int destination, WorldPosition? position = null)
    {
        var route = PersonnelLocations.Route(world, rules, origin, destination, position);
        return Create(world.Clock, origin, destination, route, Math.Max(0, route.Days - 1));
    }

    internal static MissionJourneyPlan Diplomacy(WorldMutation world, SimulationRules rules, int officer, int target)
    {
        int origin = world.Officers[officer].SettlementId!.Value;
        GiftDiplomacy.Destination(world, target, rules, out int destination, out var position);
        var route = PersonnelLocations.Route(world, rules.Personnel, origin, destination, position);
        return Create(world.Clock, origin, destination, route, route.XunRoundedDays);
    }

    private static MissionJourneyPlan Create(GameClock clock, int origin, int destination, PersonnelRoute route, int days)
    {
        for (int day = 0; day < days; day++) clock = clock.NextDay();
        return new(origin, destination, route, clock);
    }
}

/// <summary>One eligibility and travel path for previews and committed mission starts.</summary>
internal static class MissionPreparation
{
    internal static CommandRejection Eligibility(WorldMutation world, GameCommand command, SimulationRules rules)
    {
        return command switch
        {
            AlienateCommand c => AlienateStrategies.Eligibility(world,c.IssuerId,c.OfficerId,c.TargetOfficerId,rules),
            BurnCommand c => BurnStrategies.Eligibility(world,c.IssuerId,c.OfficerId,c.TargetSettlementId,rules),
            SeizeCommand c => SeizeStrategies.Eligibility(world,c.IssuerId,c.OfficerId,c.TargetSettlementId,rules),
            GossipCommand c => GossipStrategies.Eligibility(world,c.IssuerId,c.OfficerId,c.FirstTargetForceId,c.SecondTargetForceId,rules),
            UnitStrategyCommand c => UnitStrategies.SubmissionEligibility(world,c.IssuerId,c.OfficerId,c.Strategy,c.TargetUnitId,rules),
            GiftCommand c => GiftDiplomacy.Eligibility(world,c.IssuerId,c.OfficerId,c.TargetForceId,c.Offer,rules),
            RequestAttackCommand c => AttackRequestDiplomacy.Eligibility(world,c.IssuerId,c.OfficerId,c.TargetForceId,c.TargetSettlementId,rules),
            ReturnPrisonerCommand c => PrisonerReturnDiplomacy.Eligibility(world,c.IssuerId,c.OfficerId,c.TargetForceId,c.PrisonerId,rules),
            DemandSurrenderCommand c => SurrenderDiplomacy.Eligibility(world,c.IssuerId,c.OfficerId,c.TargetForceId,rules),
            _ => CommandRejection.UnsupportedCommand
        };
    }

    internal static MissionJourneyPlan? Prepare(WorldMutation world, GameCommand command, SimulationRules rules,
        out CommandRejection rejection)
    {
        rejection = Eligibility(world, command, rules);
        if (rejection != CommandRejection.None) return null;
        return command switch
        {
            AlienateCommand c => ToOfficer(c.OfficerId, c.TargetOfficerId),
            BurnCommand c => Strategy(c.OfficerId, c.TargetSettlementId),
            SeizeCommand c => Strategy(c.OfficerId, c.TargetSettlementId),
            GossipCommand c => ToForce(c.OfficerId, c.FirstTargetForceId),
            UnitStrategyCommand c => ToUnit(c.OfficerId, c.TargetUnitId),
            GiftCommand c => MissionJourneyPlan.Diplomacy(world, rules, c.OfficerId, c.TargetForceId),
            RequestAttackCommand c => MissionJourneyPlan.Diplomacy(world, rules, c.OfficerId, c.TargetForceId),
            ReturnPrisonerCommand c => MissionJourneyPlan.Diplomacy(world, rules, c.OfficerId, c.TargetForceId),
            DemandSurrenderCommand c => MissionJourneyPlan.Diplomacy(world, rules, c.OfficerId, c.TargetForceId),
            _ => throw new InvalidOperationException("Unsupported mission passed eligibility.")
        };

        MissionJourneyPlan Strategy(int officer, int destination, WorldPosition? position = null) =>
            MissionJourneyPlan.Strategy(world, rules.Personnel, world.Officers[officer].SettlementId!.Value, destination, position);
        MissionJourneyPlan ToOfficer(int officer, int target)
        {
            AlienateStrategies.TryDestination(world, world.Officers[target], rules, out int destination, out var position);
            return Strategy(officer, destination, position);
        }
        MissionJourneyPlan ToForce(int officer, int target)
        {
            GossipStrategies.TryDestination(world, target, rules, out int destination, out var position);
            return Strategy(officer, destination, position);
        }
        MissionJourneyPlan ToUnit(int officer, int target)
        {
            var unit = world.Units[target];
            return Strategy(officer, rules.Military!.RegionAt(unit.Position)!.Value, unit.Position);
        }
    }
}

public sealed partial class SimulationEngine
{
    public ImmutableArray<int> GetGiftTreasures(int forceId)
    {
        var world = PreparationWorld();
        return world.Treasures.Keys.Where(id => GiftDiplomacy.CanOfferTreasure(world, forceId, id, _rules.Treasures))
            .Order().ToImmutableArray();
    }

    /// <summary>Checks a prospective delegation before a new corps has an identity.</summary>
    public bool CanSelectCorpsDelegation(CommandContext context, int owner, CorpsDelegation delegation)
    {
        var envelope = new CreateCorpsCommand(Guid.NewGuid(), owner, _world.Simulation.NextSequence, [], 0, 0, delegation);
        return ValidateEnvelope(context, envelope) == CommandRejection.None &&
            CorpsDelegations.Valid(PreparationWorld(), owner, delegation, _rules.Military);
    }

    /// <summary>Checks command candidates without computing advice, routes or temporary world changes.</summary>
    public CommandRejection GetPreparationEligibility(CommandContext context, GameCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var rejection = ValidateEnvelope(context, command);
        if (rejection != CommandRejection.None) return rejection;
        var world = PreparationWorld();
        return command switch
        {
            DevelopCommand develop => develop.Development is null || develop.Development.ForceId != command.IssuerId
                ? CommandRejection.NotActiveController
                : DomesticDevelopmentHandler.Prepare(world, develop.Development).Accepted
                    ? CommandRejection.None : CommandRejection.DevelopmentFailed,
            OrderUnitCommand order => MilitaryMovement.OrderEligibility(world, order, _rules.Military),
            SelectTacticCommand tactic => MilitaryTactics.Eligibility(world, tactic, _rules),
            SetUnitPoliciesCommand policies => MilitaryPolicies.Eligibility(world, policies),
            AppointAdviserCommand appoint => PersonnelAppointments.Eligibility(world, appoint),
            AssignOfficesCommand offices => PersonnelOffices.Eligibility(world, offices, _rules.Governance),
            RewardOfficerCommand reward => PersonnelRewards.Eligibility(world, reward),
            GrantTreasureCommand grant => PersonnelTreasures.GrantEligibility(world, grant, _rules.Treasures),
            ConfiscateTreasureCommand confiscate => PersonnelTreasures.ConfiscateEligibility(world, confiscate, _rules.Treasures),
            SetCorpsDelegationCommand delegation => CorpsDelegations.Eligibility(world, delegation, _rules.Military),
            RecruitOfficerCommand recruit => PersonnelMissions.ValidateRecruit(world, recruit, _rules, out _, out _),
            SearchCommand search => PersonnelMissions.ValidateSearch(world, search, _rules.Personnel),
            MoveOfficersCommand move => PersonnelMissions.ValidateMove(world, move, _rules.Personnel),
            TransportUnitCommand transport => MilitaryMovement.TransportEligibility(world, transport, _rules.Military, _rules.Treasures),
            ConstructFacilityCommand construct => FacilityConstruction.Eligibility(world, construct, _rules),
            DeployUnitCommand deploy => MilitaryMovement.PrepareDeployment(world, deploy, _rules.Military,
                _rules.Treasures, UnitPurpose.Combat, 0, 0, out _),
            _ => MissionPreparation.Eligibility(world, command, _rules)
        };
    }

    /// <summary>Reads mission eligibility and arrival without starting a task or evaluating a world change.</summary>
    public CommandPreparationPreview GetMissionPreparation(CommandContext context, GameCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var rejection = ValidateEnvelope(context, command);
        if (rejection != CommandRejection.None) return new(rejection, null, null);
        var plan = MissionPreparation.Prepare(PreparationWorld(), command, _rules, out rejection);
        return new(rejection, plan?.Arrival, null);
    }
}
