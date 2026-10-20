namespace San9.Core.Simulation;

/// <summary>Identity and authority of the current decision, independent of the active turn.</summary>
public sealed record ResponseRequest(string Kind, ControlSeat Owner)
{
    public ResponsePrompt Prompt { get; internal init; } = null!;
    public DailyDecisionPrompt? Daily { get; internal init; }
    internal ResponseIdentity Decision { get; init; } = null!;
    public bool HasSameDecision(ResponseRequest? other) => other is not null &&
        Owner == other.Owner && Decision == other.Decision && Step == other.Step;
    internal CommandRejection Authorize(CommandContext context, GameCommand command) =>
        !Decision.Accepts(command) ? CommandRejection.DecisionPending :
        Owner.ForceId == command.IssuerId && Owner.ControllerId == context.ControllerId
            ? CommandRejection.None : CommandRejection.NotActiveController;
    internal ResponseStep? Step { get; init; }
}

public abstract record ResponsePrompt;
public sealed record ResponsePrompt<T>(T Value) : ResponsePrompt;

internal abstract record ResponseIdentity
{
    internal abstract bool Accepts(GameCommand command);
}
internal sealed record ResponseIdentity<TState, TCommand>(TState State) : ResponseIdentity where TCommand : GameCommand
{
    internal override bool Accepts(GameCommand command) => command is TCommand;
}

internal sealed record ResponseStep(Guid TaskId, int ResolutionStep, SearchEncounter? SearchEncounter,
    CompensationOffer? AttackOffer, CompensationOffer? PrisonerOffer);

public sealed partial class SimulationEngine
{
    public ResponseRequest? GetPendingResponse()
    {
        if (_world.IsExecuting) throw new InvalidOperationException("Simulation is executing.");
        var sim = Snapshot.Simulation;
        if (sim.Boundary != SimulationBoundary.AwaitingDecision) return null;
        (string kind, int force, ResponseIdentity pending) = sim switch
        {
            { PendingCapturedRuler: { } p } => ("captured-ruler", p.CaptorId, (ResponseIdentity)new ResponseIdentity<CapturedRulerDecision, ChooseCapturedRulerDispositionCommand>(p)),
            { PendingExecutionSuccession: { } p } => ("execution-succession", p.RespondingForceId, (ResponseIdentity)new ResponseIdentity<ExecutionSuccessionDecision, ChooseExecutionSuccessorCommand>(p)),
            { PendingSniping: { } p } => ("sniping-succession", p.RespondingForceId, (ResponseIdentity)new ResponseIdentity<SnipingCombatDecision, ChooseSnipingSuccessorCommand>(p)),
            { PendingDuel: { } p } => ("duel-succession", p.RespondingForceId, (ResponseIdentity)new ResponseIdentity<DuelCombatDecision, ChooseDuelSuccessorCommand>(p)),
            { PendingTacticStudy: { } p } => ("tactic-study", p.RespondingForceId, (ResponseIdentity)new ResponseIdentity<TacticStudyDecision, ChooseTacticStudentCommand>(p)),
            { PendingDailyTask: { } p } => ("daily-task", p.RespondingForceId, (ResponseIdentity)new ResponseIdentity<DailyTaskDecision, RespondDailyTaskCommand>(p)),
            { PendingEvent: EmperorTransferEventSession p } => (nameof(EmperorTransferEventSession), p.RespondingForceId, (ResponseIdentity)new ResponseIdentity<EmperorTransferEventSession, RespondEmperorTransferCommand>(p)),
            { PendingEvent: CountryTitleEventSession p } => (nameof(CountryTitleEventSession), p.RespondingForceId, (ResponseIdentity)new ResponseIdentity<CountryTitleEventSession, ChooseCountryTitleCommand>(p)),
            { PendingEvent: OfficeRequestEventSession p } => (nameof(OfficeRequestEventSession), p.RespondingForceId, (ResponseIdentity)new ResponseIdentity<OfficeRequestEventSession, RespondOfficeRequestCommand>(p)),
            { PendingEvent: AllianceEventSession p } => (nameof(AllianceEventSession), p.RespondingForceId, (ResponseIdentity)new ResponseIdentity<AllianceEventSession, AllianceParticipationCommand>(p)),
            _ => throw new InvalidOperationException("Awaiting decision has no response identity.")
        };
        var owner = sim.Controls.Single(c => c.ForceId == force);
        ResponseStep? step = null;
        if (sim.PendingDailyTask is { } daily)
        {
            var task = sim.Tasks.Single(t => t.Id == daily.RemainingTaskIds[0]);
            step = new(task.Id, task.ResolutionStep, task.SearchEncounter, task.AttackRequest?.Offer, task.PrisonerReturn?.Offer);
            if (task.ForceId == daily.RespondingForceId)
            {
                var world = _world.BeginMutation(_rules.Abilities, _rules.Economy);
                int? corpsId = PersonnelLocations.Corps(world, world.Officers[task.OfficerId]) ??
                    world.Corps.Values.SingleOrDefault(c => c.OwnerId == force && c.IsRoot)?.Id;
                if (corpsId is int id && world.Corps[id].ControlMode == San9.Core.Domain.CorpsControlMode.Automatic)
                    owner = owner with { ControllerId = CorpsControl.ControllerId(id), Kind = ControllerKind.Automatic };
            }
        }
        return new(kind, owner) { Decision = pending, Step = step, Prompt = DescribeResponse(), Daily = GetDailyDecisionPrompt() };
    }

    private ResponsePrompt DescribeResponse()
    {
        if (GetCountryTitlePrompt() is { } p0) return new ResponsePrompt<CountryTitlePrompt>(p0);
        if (GetEmperorTransferPrompt() is { } p1) return new ResponsePrompt<EmperorTransferPrompt>(p1);
        if (GetOfficeRequestPrompt() is { } p2) return new ResponsePrompt<OfficeRequestPrompt>(p2);
        if (GetSearchRecruitmentPrompt() is { } p3) return new ResponsePrompt<SearchRecruitmentPrompt>(p3);
        if (GetShanyangGongPrompt() is { } p4) return new ResponsePrompt<ShanyangGongPrompt>(p4);
        if (GetMysticBookOfferPrompt() is { } p5) return new ResponsePrompt<MysticBookOfferPrompt>(p5);
        if (GetMysticBookReturnPrompt() is { } p6) return new ResponsePrompt<MysticBookReturnPrompt>(p6);
        if (GetScholarInsultPrompt() is { } p7) return new ResponsePrompt<ScholarInsultPrompt>(p7);
        if (GetLongevityPrompt() is { } p8) return new ResponsePrompt<LongevityPrompt>(p8);
        if (GetTreasureMonumentPrompt() is { } p9) return new ResponsePrompt<TreasureMonumentPrompt>(p9);
        if (GetScholarReturnPrompt() is { } p10) return new ResponsePrompt<ScholarReturnPrompt>(p10);
        if (GetMerchantInvestmentPrompt() is { } p11) return new ResponsePrompt<MerchantInvestmentPrompt>(p11);
        if (GetCommunitySearchPrompt() is { } p12) return new ResponsePrompt<CommunitySearchPrompt>(p12);
        if (GetSearchConflictPrompt() is { } p13) return new ResponsePrompt<SearchConflictPrompt>(p13);
        if (GetTigerSearchPrompt() is { } p14) return new ResponsePrompt<TigerSearchPrompt>(p14);
        if (GetBeastSearchPrompt() is { } p15) return new ResponsePrompt<BeastSearchPrompt>(p15);
        if (GetTacticStudyPrompt() is { } p16) return new ResponsePrompt<TacticStudyPrompt>(p16);
        if (GetSnipingSuccessionPrompt() is { } p17) return new ResponsePrompt<SnipingSuccessionPrompt>(p17);
        if (GetDuelSuccessionPrompt() is { } p18) return new ResponsePrompt<DuelSuccessionPrompt>(p18);
        if (GetCapturedRulerPrompt() is { } p19) return new ResponsePrompt<CapturedRulerPrompt>(p19);
        if (GetExecutionSuccessionPrompt() is { } p20) return new ResponsePrompt<ExecutionSuccessionPrompt>(p20);
        if (GetAllianceParticipationPrompt() is { } p21) return new ResponsePrompt<AllianceParticipationPrompt>(p21);
        if (GetAttackRequestPrompt() is { } p22) return new ResponsePrompt<AttackRequestPrompt>(p22);
        if (GetPrisonerReturnPrompt() is { } p23) return new ResponsePrompt<PrisonerReturnPrompt>(p23);
        if (GetDailyDecisionPrompt() is { } daily) return new ResponsePrompt<DailyDecisionPrompt>(daily);
        throw new InvalidOperationException("Pending decision has no prompt.");
    }
}
