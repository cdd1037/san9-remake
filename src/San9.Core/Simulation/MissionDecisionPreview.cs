namespace San9.Core.Simulation;

/// <summary>Public, read-only decision inputs from the same formulas used at resolution.</summary>
public sealed record MissionDecisionPreview(CommandAdvice Advice, int? Threshold);

public sealed partial class SimulationEngine
{
    public MissionDecisionPreview GetMissionDecision(CommandContext context, GameCommand command)
    {
        var rejection = GetPreparationEligibility(context, command);
        if (rejection != CommandRejection.None)
            return new(new(rejection, null, AdviserOpinion.Unavailable), null);
        var world = PreparationWorld();
        return command switch
        {
            UnitStrategyCommand c => new(GetUnitStrategyAdvice(c.IssuerId, c.OfficerId, c.Strategy, c.TargetUnitId),
                UnitStrategies.Threshold(world, c.OfficerId, new(c.Strategy, c.TargetUnitId), _rules)),
            AlienateCommand c => new(GetAlienateAdvice(c.IssuerId, c.OfficerId, c.TargetOfficerId),
                AlienateStrategies.Threshold(world, c.OfficerId, c.TargetOfficerId)),
            BurnCommand c => new(GetBurnAdvice(c.IssuerId, c.OfficerId, c.TargetSettlementId),
                BurnStrategies.Threshold(world, c.OfficerId, c.TargetSettlementId)),
            SeizeCommand c => new(GetSeizeAdvice(c.IssuerId, c.OfficerId, c.TargetSettlementId),
                SeizeStrategies.DecisionThreshold(world, c.OfficerId, c.TargetSettlementId)),
            GossipCommand c => new(GetGossipAdvice(c.IssuerId, c.OfficerId, c.FirstTargetForceId, c.SecondTargetForceId),
                GossipStrategies.Threshold(world, c.OfficerId, c.FirstTargetForceId, c.SecondTargetForceId)),
            GiftCommand c => new(GetGiftAdvice(c.IssuerId, c.OfficerId, c.TargetForceId, c.Offer),
                GiftDiplomacy.Threshold(world, c.IssuerId, c.OfficerId, c.TargetForceId, c.Offer, _rules)),
            RequestAttackCommand c => new(GetAttackRequestAdvice(c.IssuerId, c.OfficerId, c.TargetForceId, c.TargetSettlementId),
                AttackRequestDiplomacy.FreeThreshold(world, c.IssuerId, c.OfficerId, c.TargetForceId, c.TargetSettlementId, _rules)),
            ReturnPrisonerCommand c => new(GetPrisonerReturnAdvice(c.IssuerId, c.OfficerId, c.TargetForceId, c.PrisonerId),
                PrisonerCompensation.Quote(world, c.IssuerId, c.OfficerId, c.TargetForceId, c.PrisonerId, _rules.Treasures).Kind == CompensationKind.Refuse ? 0 : 100),
            DemandSurrenderCommand c => new(GetSurrenderAdvice(c.IssuerId, c.OfficerId, c.TargetForceId),
                SurrenderDiplomacy.Threshold(world, c.IssuerId, c.OfficerId, c.TargetForceId, _rules)),
            _ => throw new ArgumentException("Command has no mission decision.", nameof(command))
        };
    }

    public int? GetAutomaticCountryTitle(int force, Func<ulong> random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var world = PreparationWorld();
        return PoliticalEvents.AutomaticTitle(world, _rules, world.Forces[force], random);
    }

    /// <summary>Uses public negotiation rules for both automatic responders and preparation tools.</summary>
    public RespondDailyTaskCommand GetAutomaticDiplomacyResponse(Guid id, int force, long sequence)
    {
        if (GetPendingResponse()?.Owner is not { Kind: ControllerKind.Automatic, ForceId: var owner } || owner != force)
            throw new InvalidOperationException("Diplomacy choice belongs to the player.");
        var world = PreparationWorld();
        if (world.Simulation.PendingDailyTask is not { } pending || pending.RespondingForceId != force)
            throw new InvalidOperationException("No diplomacy response for this force.");
        var task = world.Simulation.Tasks.Single(t => t.Id == pending.RemainingTaskIds[0]);
        if (task.AttackRequest is null && task.PrisonerReturn is null)
            throw new InvalidOperationException("Not a diplomacy task.");
        var response = new RespondDailyTaskCommand(id, force, sequence, task.Id, task.ResolutionStep, false);
        if (task.ResolutionStep == 0)
        {
            var offer = task.AttackRequest is not null ? AttackRequestDiplomacy.Quote(world, task, _rules) :
                PrisonerCompensation.Quote(world, task.ForceId, task.OfficerId, task.PrisonerReturn!.TargetForceId,
                    task.PrisonerReturn.PrisonerId, _rules.Treasures);
            return response with { Accept = offer.Kind != CompensationKind.Refuse,
                Offer = offer.Kind != CompensationKind.Refuse ? offer : null };
        }
        return response with { Accept = task.AttackRequest is not null
            ? AttackRequestDiplomacy.Accept(world, task, _rules) : PrisonerReturnDiplomacy.Accept(world, task, _rules) };
    }
}
