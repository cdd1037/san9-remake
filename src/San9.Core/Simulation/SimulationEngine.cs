using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed class SimulationEngine
{
    private readonly WorldState _world;
    private readonly SimulationRules _rules;
    private readonly ResolutionPipeline _pipeline;

    public SimulationEngine(WorldState world) : this(world, new SimulationRules()) { }
    public SimulationEngine(WorldState world, EconomyRulebook economy) : this(world, new SimulationRules { Economy = economy }) { }
    public SimulationEngine(WorldState world, EconomyRulebook economy, PersonnelRulebook personnel)
        : this(world, new SimulationRules { Economy = economy, Personnel = personnel }) { }

    public SimulationEngine(WorldState world, EconomyRulebook economy, PersonnelRulebook personnel, TreasureRulebook treasures,
        GovernanceRulebook? governance = null, MilitaryRulebook? military = null, TerminalDefinition? terminal = null, PoliticalRulebook? politics = null, QiaoMarriageDefinition? qiaoMarriage = null, SunMarriageDefinition? sunMarriage = null,
        HanzhongKingDefinition? hanzhongKing = null, FiveTigersDefinition? fiveTigers = null, ExpeditionMemorialDefinition? expeditionMemorial = null)
        : this(ValidateContent(world, treasures, governance), new SimulationRules
            { Economy = economy, Personnel = personnel, Treasures = treasures, Governance = governance ?? new([]), Military = military, Terminal = terminal, Politics = politics ?? PoliticalRulebook.Empty, QiaoMarriage = qiaoMarriage, SunMarriage = sunMarriage,
                HanzhongKing = hanzhongKing, FiveTigers = fiveTigers, ExpeditionMemorial = expeditionMemorial }) { }

    private static WorldState ValidateContent(WorldState world, TreasureRulebook treasures, GovernanceRulebook? governance)
    {
        var snapshot = world.CreateSnapshot();
        if (snapshot.Treasures.Any(t => t.Discovery != TreasureDiscovery.Disabled && !treasures.Items.ContainsKey(t.Id)) ||
            treasures.Items.Values.Any(t => t.FavoredOwnerIds.Any(id => !snapshot.Officers.Any(o => o.Id == id))) ||
            snapshot.Officers.Any(p => p.OfficeId is int id && (governance is null || !governance.Offices.ContainsKey(id))))
            throw new ArgumentException("Unknown ability source definition.");
        return world;
    }

    internal SimulationEngine(WorldState world, SimulationRules rules)
    {
        rules.Terminal?.Validate(world.CreateSnapshot().Settlements.Where(s => s.Kind == SettlementKind.City).Select(s => s.Id).ToHashSet());
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
        _rules = rules;
        MilitaryMovement.Validate(world.CreateSnapshot(), rules.Military);
        rules.Economy.Validate();
        rules.Economy.ImperialCourt.Validate(world.CreateSnapshot().Officers.Select(p => p.Id).ToHashSet());
        _pipeline = new(rules);
        if (rules.Personnel.TacticCourses.Any(c => !world.CreateSnapshot().Settlements.Any(s => s.Id == c.Key && s.Kind == SettlementKind.City) ||
            rules.Military is null || !rules.Military.Tactics.TryGetValue(c.Value, out var tactic) || tactic.Category is not (>= 0 and <= 8)))
            throw new ArgumentException("Invalid tactic study course.");
        var initial = world.BeginMutation(rules.Abilities);
        PoliticalEvents.ValidateRules(initial, rules);
        rules.QiaoMarriage?.Validate(world.CreateSnapshot().Officers.Select(p => p.Id).ToHashSet(), rules.Personnel.CityProvinces);
        rules.SunMarriage?.Validate(world.CreateSnapshot().Officers.Select(p => p.Id).ToHashSet(),
            rules.Military?.Tactics.Values.Where(t => t.Category is >= 0 and <= 8).Select(t => t.Id).ToHashSet() ?? []);
        var officerIds = initial.Officers.Keys.ToHashSet();
        var cityIds = initial.Settlements.Values.Where(s => s.Kind == SettlementKind.City).Select(s => s.Id).ToHashSet();
        var tacticIds = rules.Military?.Tactics.Values.Where(t => t.Category is >= 0 and <= 8).Select(t => t.Id).ToHashSet() ?? [];
        rules.HanzhongKing?.Validate(officerIds, cityIds, rules.Politics.Ranks.Keys.ToHashSet());
        rules.FiveTigers?.Validate(officerIds, tacticIds);
        rules.ExpeditionMemorial?.Validate(officerIds, rules.Personnel.CityProvinces, cityIds, rules.Politics.Titles.Keys.ToHashSet(), tacticIds);
        if (initial.Corps.Values.Any(c => !CorpsDelegations.Valid(initial, c, rules.Military)))
            throw new ArgumentException("Invalid corps delegation target.");
        foreach (var task in world.Simulation.Tasks)
            if (rules.FindTask(task.Kind) is not TaskRule rule || !rule.SupportsResolutionStep(task.ResolutionStep) ||
                !rule.HasValidPayload(task) || rule.DurationDays(task) != task.Due.DayNumber - task.Started.DayNumber)
                throw new ArgumentException($"Unsupported or invalid task '{task.Kind}'.");
        if (world.Simulation.PendingTacticStudy is { } study && TacticStudy.Candidates(initial, rules, study.CityId).IsEmpty)
            throw new ArgumentException("Invalid tactic study candidates.");
        if (world.Simulation.PendingDailyTask is { } pending)
        {
            var task = world.Simulation.Tasks.Single(t => t.Id == pending.RemainingTaskIds[0]);
            var mutation = world.BeginMutation(rules.Abilities);
            if (pending.RemainingTaskIds.Any(id => rules.FindTask(world.Simulation.Tasks.Single(t => t.Id == id).Kind)!.Phase != pending.Phase) ||
                rules.FindTask(task.Kind)!.DecisionRecipient(mutation, task) != pending.RespondingForceId)
                throw new ArgumentException("Invalid task continuation phase or respondent.");
        }
        if (world.Simulation.Boundary == SimulationBoundary.New)
        {
            _world.EnterExecution();
            try
            {
                var mutation = world.BeginMutation(rules.Abilities);
                List<DomainEvent> events = [];
                OfficerAppearance.Initialize(mutation, events);
                _pipeline.Prepare(mutation, events);
                AttackRequests.Cleanup(mutation, events);
                CorpsDelegations.Cleanup(mutation, _rules.Military, events);
                mutation.MaintainLeadership(events);
                world.Commit(mutation);
                InitialEvents = events.AsReadOnly();
            }
            finally { _world.ExitExecution(); }
        }
    }

    public IReadOnlyList<DomainEvent> InitialEvents { get; } = Array.Empty<DomainEvent>();
    public WorldSnapshot Snapshot => _world.CreateSnapshot();
    public TacticStudyPrompt? GetTacticStudyPrompt()
    {
        if (_world.IsExecuting) throw new InvalidOperationException("Simulation is executing.");
        return TacticStudy.Prompt(_world.BeginMutation(_rules.Abilities), _rules);
    }

    public DuelSuccessionPrompt? GetDuelSuccessionPrompt()
    {
        if (_world.IsExecuting) throw new InvalidOperationException("Simulation is executing.");
        return _world.Simulation.PendingDuel is { } pending
            ? new(pending.Aftermath.PendingRulerId!.Value, pending.RespondingForceId,
                DuelAftermath.Successors(_world.BeginMutation(_rules.Abilities), pending.Aftermath.PendingRulerId.Value)) : null;
    }

    public ExecutionSuccessionPrompt? GetExecutionSuccessionPrompt()
    {
        if (_world.IsExecuting) throw new InvalidOperationException("Simulation is executing.");
        return _world.Simulation.PendingExecutionSuccession is { } pending
            ? new(pending.ExecutingForceId, pending.RulerId, pending.RespondingForceId,
                PrisonerExecution.Candidates(_world.BeginMutation(_rules.Abilities), pending.RespondingForceId)) : null;
    }
    public CommandAdvice GetSurrenderAdvice(int forceId, int officerId, int targetForceId)
    {
        if (_world.IsExecuting) throw new InvalidOperationException("Simulation is executing.");
        return SurrenderDiplomacy.Advice(_world.BeginMutation(_rules.Abilities), forceId, officerId, targetForceId, _rules);
    }
    public AttackRequestPrompt? GetAttackRequestPrompt()
    {
        if (_world.IsExecuting) throw new InvalidOperationException("Simulation is executing.");
        if (_world.Simulation.PendingDailyTask is not { } waiting) return null;
        var task = _world.Simulation.Tasks.Single(t => t.Id == waiting.RemainingTaskIds[0]);
        return task.AttackRequest is { } mission ? new(task.Id, task.ResolutionStep, waiting.RespondingForceId,
            task.ForceId, mission.TargetForceId, mission.TargetSettlementId, mission.Offer) : null;
    }
    public CommandAdvice GetAttackRequestAdvice(int forceId, int officerId, int targetForceId, int targetSettlementId)
    {
        if (_world.IsExecuting) throw new InvalidOperationException("Simulation is executing.");
        return AttackRequestDiplomacy.Advice(_world.BeginMutation(_rules.Abilities), forceId, officerId, targetForceId, targetSettlementId, _rules);
    }
    public PrisonerReturnPrompt? GetPrisonerReturnPrompt()
    {
        if (_world.IsExecuting) throw new InvalidOperationException("Simulation is executing.");
        if (_world.Simulation.PendingDailyTask is not { } waiting) return null;
        var task = _world.Simulation.Tasks.Single(t => t.Id == waiting.RemainingTaskIds[0]);
        return task.PrisonerReturn is { } mission ? new(task.Id, task.ResolutionStep, waiting.RespondingForceId,
            task.ForceId, mission.TargetForceId, mission.PrisonerId, mission.Offer) : null;
    }
    public CommandAdvice GetPrisonerReturnAdvice(int forceId, int officerId, int targetForceId, int prisonerId)
    {
        if (_world.IsExecuting) throw new InvalidOperationException("Simulation is executing.");
        return PrisonerReturnDiplomacy.Advice(_world.BeginMutation(_rules.Abilities), forceId, officerId, targetForceId, prisonerId, _rules);
    }
    public CommandAdvice GetGiftAdvice(int forceId, int officerId, int targetForceId, GiftOffer offer)
    {
        if (_world.IsExecuting) throw new InvalidOperationException("Simulation is executing.");
        return GiftDiplomacy.Advice(_world.BeginMutation(_rules.Abilities), forceId, officerId, targetForceId, offer, _rules);
    }
    public WorldSnapshot CaptureStableState()
    {
        if (_world.IsExecuting || !_world.Simulation.CanPersist)
            throw new InvalidOperationException("Simulation is not at a save boundary.");
        return Snapshot;
    }

    public CommandSubmissionResult Validate(CommandContext context, GameCommand command) =>
        Evaluate(context, command, commit: false).Submission;

    public AllianceFormationCandidates GetAllianceCandidates()
    {
        if (_world.IsExecuting) throw new InvalidOperationException("Simulation is executing.");
        return AllianceEligibility.Find(_world.BeginMutation(_rules.Abilities), _rules);
    }

    public CountryTitlePrompt? GetCountryTitlePrompt()
    {
        if (_world.IsExecuting) throw new InvalidOperationException("Simulation is executing.");
        return _world.Simulation.PendingEvent is CountryTitleEventSession pending ? new(pending.ForceId, pending.ChangeKind, pending.CandidateIds) : null;
    }

    public OfficeRequestPrompt? GetOfficeRequestPrompt()
    {
        if (_world.IsExecuting) throw new InvalidOperationException("Simulation is executing.");
        return _world.Simulation.PendingEvent is OfficeRequestEventSession session
            ? new(session.OfficerId, session.ForceId, session.SettlementId) : null;
    }

    public SearchRecruitmentPrompt? GetSearchRecruitmentPrompt()
    {
        if (_world.IsExecuting) throw new InvalidOperationException("Simulation is executing.");
        if (_world.Simulation.PendingDailyTask is not { } pending) return null;
        var task = _world.Simulation.Tasks.Single(t => t.Id == pending.RemainingTaskIds[0]);
        return task.Kind == PersonnelMissions.SearchKind && task.SearchEncounter is PersonSearchEncounter encounter
            ? new(task.Id, task.ResolutionStep, task.ForceId, task.OfficerId, encounter.OfficerId, task.Journey!.DestinationPlaceId) : null;
    }

    public BeastSearchPrompt? GetBeastSearchPrompt()
    {
        if (_world.IsExecuting) throw new InvalidOperationException("Simulation is executing.");
        if (_world.Simulation.PendingDailyTask is not { } pending) return null;
        var task = _world.Simulation.Tasks.Single(t => t.Id == pending.RemainingTaskIds[0]);
        return task.SearchEncounter is BeastSearchEncounter encounter &&
            _rules.Personnel.SearchEvents.GetValueOrDefault(encounter.EventId) is BeastSearchEventDefinition definition
            ? new(task.Id, task.ResolutionStep, encounter.EventId, task.ForceId, task.OfficerId, task.Journey!.DestinationPlaceId,
                SearchEvents.BattleChance(_world.BeginMutation(_rules.Abilities), task.OfficerId, definition)) : null;
    }

    public AllianceParticipationPrompt? GetAllianceParticipationPrompt()
    {
        if (_world.IsExecuting) throw new InvalidOperationException("Simulation is executing.");
        return AllianceFormation.Prompt(_world.BeginMutation(_rules.Abilities), _rules);
    }

    public CommandAdvice GetBurnAdvice(int forceId, int officerId, int targetSettlementId)
    {
        if (_world.IsExecuting) throw new InvalidOperationException("Simulation is executing.");
        return BurnStrategies.Advice(_world.BeginMutation(_rules.Abilities), forceId, officerId, targetSettlementId, _rules);
    }

    public CommandAdvice GetAlienateAdvice(int forceId, int officerId, int targetOfficerId)
    {
        if (_world.IsExecuting) throw new InvalidOperationException("Simulation is executing.");
        return AlienateStrategies.Advice(_world.BeginMutation(_rules.Abilities), forceId, officerId,
            targetOfficerId, _rules);
    }

    public CommandAdvice GetSeizeAdvice(int forceId, int officerId, int targetSettlementId)
    {
        if (_world.IsExecuting) throw new InvalidOperationException("Simulation is executing.");
        return SeizeStrategies.Advice(_world.BeginMutation(_rules.Abilities), forceId, officerId,
            targetSettlementId, _rules);
    }

    public CommandAdvice GetGossipAdvice(int forceId, int officerId, int firstTargetForceId,
        int secondTargetForceId)
    {
        if (_world.IsExecuting) throw new InvalidOperationException("Simulation is executing.");
        return GossipStrategies.Advice(_world.BeginMutation(_rules.Abilities), forceId, officerId,
            firstTargetForceId, secondTargetForceId, _rules);
    }

    public CommandAdvice GetUnitStrategyAdvice(int forceId, int officerId, UnitStrategyKind strategy, int targetUnitId)
    {
        if (_world.IsExecuting) throw new InvalidOperationException("Simulation is executing.");
        return UnitStrategies.Advice(_world.BeginMutation(_rules.Abilities), forceId, officerId, strategy, targetUnitId, _rules);
    }

    public AttackWarning GetAttackWarning(int forceId, MilitaryOrder order)
    {
        if (_world.IsExecuting) throw new InvalidOperationException("Simulation is executing.");
        return MilitaryDiplomacy.Warning(_world.BeginMutation(_rules.Abilities), forceId, order, _rules.Military);
    }

    public CommandExecutionResult Execute(CommandContext context, GameCommand command) =>
        Evaluate(context, command, commit: true);

    private CommandExecutionResult Evaluate(CommandContext context, GameCommand command, bool commit)
    {
        ArgumentNullException.ThrowIfNull(command);
        var rejection = ValidateEnvelope(context, command);
        if (rejection != CommandRejection.None) return Reject(rejection);
        _world.EnterExecution();
        try
        {
            var mutation = _world.BeginMutation(_rules.Abilities);
            List<DomainEvent> events = [];
            DevelopmentOutcome? development = null;
            switch (command)
            {
                case ChooseTacticStudentCommand student:
                    var studyRejection = TacticStudy.Respond(mutation, student, _rules, events);
                    if (studyRejection != CommandRejection.None) return Reject(studyRejection);
                    if (mutation.Simulation.PendingTacticStudy is null)
                        _pipeline.Prepare(mutation, events, ResolutionStage.QuarterStudyOffers);
                    break;
                case ExecutePrisonerCommand execution:
                    var executionRejection = PrisonerExecution.Start(mutation, execution, _rules, events);
                    if (executionRejection != CommandRejection.None) return Reject(executionRejection);
                    break;
                case ChooseDuelSuccessorCommand duelSuccessor:
                    var duel = mutation.Simulation.PendingDuel!;
                    if (duel.Aftermath.PendingRulerId != duelSuccessor.RulerId ||
                        duelSuccessor.SuccessorId is int selected && !DuelAftermath.Successors(mutation, duelSuccessor.RulerId).Contains(selected))
                        return Reject(CommandRejection.InvalidDecision);
                    int duelHeir = duelSuccessor.SuccessorId ?? RulerSuccession.Automatic(mutation,
                        duel.RespondingForceId, mutation.Officers[duelSuccessor.RulerId])!.Id;
                    _pipeline.ResumeDuel(mutation, events, duelHeir);
                    ContinueXun(mutation, duel.XunStarted, events);
                    break;
                case ChooseExecutionSuccessorCommand successor:
                    var successionRejection = PrisonerExecution.Respond(mutation, successor, _rules, events);
                    if (successionRejection != CommandRejection.None) return Reject(successionRejection);
                    break;
                case DemandSurrenderCommand surrender:
                    var surrenderRejection = SurrenderDiplomacy.Start(mutation, context, surrender, _rules, events);
                    if (surrenderRejection != CommandRejection.None) return Reject(surrenderRejection);
                    break;
                case RequestAttackCommand attackRequest:
                    var requestRejection = AttackRequestDiplomacy.Start(mutation, context, attackRequest, _rules, events);
                    if (requestRejection != CommandRejection.None) return Reject(requestRejection);
                    break;
                case ReturnPrisonerCommand prisonerReturn:
                    var prisonerRejection = PrisonerReturnDiplomacy.Start(mutation, context, prisonerReturn, _rules, events);
                    if (prisonerRejection != CommandRejection.None) return Reject(prisonerRejection);
                    break;
                case GiftCommand gift:
                    var giftRejection = GiftDiplomacy.Start(mutation, context, gift, _rules, events);
                    if (giftRejection != CommandRejection.None) return Reject(giftRejection);
                    break;
                case RespondDailyTaskCommand response:
                    var waiting = mutation.Simulation.PendingDailyTask!;
                    if (waiting.RemainingTaskIds[0] != response.TaskId || response.Step !=
                        mutation.Simulation.Tasks.Single(t => t.Id == response.TaskId).ResolutionStep)
                        return Reject(CommandRejection.InvalidDecision);
                    var respondingTask = mutation.Simulation.Tasks.Single(t => t.Id == response.TaskId);
                    if (!_rules.FindTask(respondingTask.Kind)!.AcceptsResponse(mutation, respondingTask, response))
                        return Reject(CommandRejection.InvalidDecision);
                    _pipeline.Respond(mutation, response, events);
                    ContinueXun(mutation, waiting.XunStarted, events);
                    break;
                case ChooseCountryTitleCommand countryTitle:
                    var titleRejection = PoliticalEvents.Respond(mutation, _rules, countryTitle, events);
                    if (titleRejection != CommandRejection.None) return Reject(titleRejection);
                    _pipeline.Prepare(mutation, events, ResolutionStage.QuarterPolitics);
                    break;
                case RespondOfficeRequestCommand officeRequest:
                    var officeRejection = OfficeRequests.Respond(mutation, _rules, officeRequest, events);
                    if (officeRejection != CommandRejection.None) return Reject(officeRejection);
                    if (mutation.Simulation.PendingEvent is null)
                        _pipeline.Prepare(mutation, events, ResolutionStage.XunUpdate);
                    break;
                case AllianceParticipationCommand participation:
                    var participationRejection = AllianceFormation.Respond(mutation, participation, _rules, events);
                    if (participationRejection != CommandRejection.None) return Reject(participationRejection);
                    if (mutation.Simulation.PendingEvent is null)
                        _pipeline.Prepare(mutation, events, ResolutionStage.QuarterEvents);
                    break;
                case WaitCommand:
                    break;
                case BurnCommand burn:
                    var burnRejection = BurnStrategies.Start(mutation, context, burn, _rules, events);
                    if (burnRejection != CommandRejection.None) return Reject(burnRejection);
                    break;
                case AlienateCommand alienate:
                    var alienateRejection = AlienateStrategies.Start(mutation, context, alienate, _rules, events);
                    if (alienateRejection != CommandRejection.None) return Reject(alienateRejection);
                    break;
                case SeizeCommand seize:
                    var seizeRejection = SeizeStrategies.Start(mutation, context, seize, _rules, events);
                    if (seizeRejection != CommandRejection.None) return Reject(seizeRejection);
                    break;
                case GossipCommand gossip:
                    var gossipRejection = GossipStrategies.Start(mutation, context, gossip, _rules, events);
                    if (gossipRejection != CommandRejection.None) return Reject(gossipRejection);
                    break;
                case UnitStrategyCommand strategy:
                    var strategyRejection = UnitStrategies.Start(mutation, context, strategy, _rules, events);
                    if (strategyRejection != CommandRejection.None) return Reject(strategyRejection);
                    break;
                case SelectTacticCommand tactic:
                    var tacticRejection = MilitaryTactics.Select(mutation, tactic, _rules, events);
                    if (tacticRejection != CommandRejection.None) return Reject(tacticRejection);
                    break;
                case DeployUnitCommand deploy:
                    var deployRejection = MilitaryMovement.Deploy(mutation, deploy, _rules.Military, _rules.Governance, _rules.Treasures, events);
                    if (deployRejection != CommandRejection.None) return Reject(deployRejection);
                    break;
                case SetUnitPoliciesCommand policies:
                    var policyRejection = MilitaryPolicies.Set(mutation, policies, events);
                    if (policyRejection != CommandRejection.None) return Reject(policyRejection);
                    break;
                case DemolishFacilityCommand demolish:
                    var demolitionRejection = FacilityDemolition.Apply(mutation, demolish, _rules, events);
                    if (demolitionRejection != CommandRejection.None) return Reject(demolitionRejection);
                    break;
                case ConstructFacilityCommand construct:
                    var constructionRejection = FacilityConstruction.Start(mutation, construct, _rules, events);
                    if (constructionRejection != CommandRejection.None) return Reject(constructionRejection);
                    break;
                case OrderUnitCommand order:
                    var orderRejection = MilitaryMovement.Order(mutation, order, _rules.Military, events);
                    if (orderRejection != CommandRejection.None) return Reject(orderRejection);
                    break;
                case ReformUnitCommand reform:
                    var reformRejection = UnitReformation.Apply(mutation, reform, _rules.Military, _rules.Treasures, _rules.Governance, events);
                    if (reformRejection != CommandRejection.None) return Reject(reformRejection);
                    break;
                case SetCorpsControlCommand control:
                    var controlRejection = CorpsControl.Set(mutation, control, events);
                    if (controlRejection != CommandRejection.None) return Reject(controlRejection);
                    break;
                case SetCorpsDelegationCommand delegation:
                    var delegationRejection = CorpsDelegations.Set(mutation, delegation, _rules.Military, events);
                    if (delegationRejection != CommandRejection.None) return Reject(delegationRejection);
                    break;
                case DissolveCorpsCommand dissolve:
                    var dissolveRejection = CorpsGovernance.Dissolve(mutation, dissolve, _rules.Governance, events);
                    if (dissolveRejection != CommandRejection.None) return Reject(dissolveRejection);
                    break;
                case CreateCorpsCommand create:
                    var createRejection = CorpsGovernance.Create(mutation, create, _rules.Personnel, _rules.Governance, events);
                    if (createRejection != CommandRejection.None) return Reject(createRejection);
                    break;
                case ChangeCorpsCommand change:
                    var changeRejection = CorpsGovernance.Change(mutation, change, _rules.Personnel, _rules.Governance, events);
                    if (changeRejection != CommandRejection.None) return Reject(changeRejection);
                    break;
                case AppointAdviserCommand appoint:
                    var appointRejection = PersonnelAppointments.Apply(mutation, appoint, events);
                    if (appointRejection != CommandRejection.None) return Reject(appointRejection);
                    break;
                case AssignOfficesCommand offices:
                    var officesRejection = PersonnelOffices.Apply(mutation, offices, _rules.Governance, events);
                    if (officesRejection != CommandRejection.None) return Reject(officesRejection);
                    break;
                case RewardOfficerCommand reward:
                    var rewardRejection = PersonnelRewards.Apply(mutation, reward, events);
                    if (rewardRejection != CommandRejection.None) return Reject(rewardRejection);
                    break;
                case GrantTreasureCommand grant:
                    var grantRejection = PersonnelTreasures.Grant(mutation, grant, _rules.Treasures, _rules.Economy, events);
                    if (grantRejection != CommandRejection.None) return Reject(grantRejection);
                    break;
                case ConfiscateTreasureCommand confiscate:
                    var confiscateRejection = PersonnelTreasures.Confiscate(mutation, confiscate, _rules.Treasures, _rules.Economy, events);
                    if (confiscateRejection != CommandRejection.None) return Reject(confiscateRejection);
                    break;
                case ExileOfficerCommand exile:
                    var exileRejection = PersonnelExile.Apply(mutation, exile, _rules.Personnel, _rules.Economy, _rules.Governance, events);
                    if (exileRejection != CommandRejection.None) return Reject(exileRejection);
                    break;
                case ReleasePrisonerCommand release:
                    var releaseRejection = PrisonerRelease.Apply(mutation, release, release.OfficerId, _rules.Personnel, _rules.Economy, events);
                    if (releaseRejection != CommandRejection.None) return Reject(releaseRejection);
                    break;
                case SearchCommand search:
                    var searchRejection = PersonnelMissions.StartSearch(mutation, context, search, _rules.Personnel, events);
                    if (searchRejection != CommandRejection.None) return Reject(searchRejection);
                    break;
                case RecruitOfficerCommand recruit:
                    var recruitRejection = PersonnelMissions.StartRecruit(mutation, context, recruit, _rules.Personnel, events);
                    if (recruitRejection != CommandRejection.None) return Reject(recruitRejection);
                    break;
                case MoveOfficersCommand move:
                    var moveRejection = PersonnelMissions.StartMove(mutation, context, move, _rules.Personnel, events);
                    if (moveRejection != CommandRejection.None) return Reject(moveRejection);
                    break;
                case DevelopCommand develop:
                    if (develop.Development is null || develop.Development.ForceId != command.IssuerId)
                        return Reject(CommandRejection.NotActiveController);
                    development = DomesticDevelopmentHandler.Apply(mutation, develop.Development, _rules.Economy, events);
                    if (!development.Value.Accepted) return Reject(CommandRejection.DevelopmentFailed, development);
                    events.Add(new DevelopmentCompletedEvent(mutation.Clock, develop.Development,
                        development.Value.Increment, development.Value.Cost));
                    break;
                case StartTaskCommand start:
                    var rule = _rules.FindTask(start.Kind);
                    if (rule is null) return Reject(CommandRejection.UnsupportedCommand);
                    var task = new PersistentTask(start.CommandId, start.Kind, context.ControllerId, start.IssuerId,
                        start.OfficerId, start.TargetId, mutation.Clock, mutation.Clock, 0, mutation.Simulation.NextSequence);
                    int duration = rule.DurationDays(task);
                    if (duration <= 0) return Reject(CommandRejection.InvalidTask);
                    GameClock due = mutation.Clock;
                    for (int day = 0; day < duration; day++) due = due.NextDay();
                    task = task with { Due = due };
                    if (!ResolutionPipeline.TaskReferencesValid(mutation, task) ||
                        mutation.Officers[task.OfficerId].Acted || mutation.Simulation.Tasks.Any(t => t.OfficerId == task.OfficerId) ||
                        !rule.Validate(mutation, task)) return Reject(CommandRejection.InvalidTask);
                    mutation.Replace(mutation.Officers[task.OfficerId] with { Acted = true });
                    mutation.SetSimulation(mutation.Simulation with { Tasks = mutation.Simulation.Tasks.Add(task) });
                    events.Add(new TaskStartedEvent(task));
                    break;
                default:
                    return Reject(CommandRejection.UnsupportedCommand);
            }
            mutation.SetSimulation(mutation.Simulation with
            {
                NextSequence = checked(mutation.Simulation.NextSequence + 1),
            });
            AttackRequests.Cleanup(mutation, events);
            CorpsDelegations.Cleanup(mutation, _rules.Military, events);
            mutation.MaintainLeadership(events);
            WorldInvariants.Validate(mutation);
            if (!commit) return new(CommandSubmissionResult.Success, [], Snapshot, development);
            _world.Commit(mutation);
            events.Add(new CommandResolvedEvent(command.CommandId, command.IssuerId, command.GetType().Name)
                { Sequence = command.Sequence });
            return new(CommandSubmissionResult.Success, events.AsReadOnly(), Snapshot, development);
        }
        finally { _world.ExitExecution(); }
    }

    public System.Collections.Immutable.ImmutableDictionary<int, OfficerAbilities> GetAbilities(WorldSnapshot snapshot) =>
        _rules.Abilities.EvaluateMany(snapshot.Officers, snapshot.Treasures);

    private CommandRejection ValidateEnvelope(CommandContext context, GameCommand command)
    {
        if (command.IssuerId < 0) return CommandRejection.NegativeIssuerId;
        if (command.Sequence < 0) return CommandRejection.NegativeSequence;
        if (command.CommandId == Guid.Empty) return CommandRejection.InvalidCommandId;
        if (_world.IsExecuting || _world.Simulation.Boundary is not (SimulationBoundary.Stable or SimulationBoundary.AwaitingDecision))
            return CommandRejection.NotStable;
        if (_world.Simulation.Tasks.Any(t => t.Id == command.CommandId)) return CommandRejection.DuplicateCommandId;
        if (command.Sequence != _world.Simulation.NextSequence) return CommandRejection.WrongSequence;
        var sim = _world.Simulation;
        if (sim.PendingDuel is { } duel)
        {
            if (command is not ChooseDuelSuccessorCommand) return CommandRejection.DecisionPending;
            return duel.RespondingForceId == command.IssuerId && sim.Controls.Any(c => c.ForceId == command.IssuerId &&
                c.Kind == ControllerKind.Player && c.ControllerId == context.ControllerId)
                ? CommandRejection.None : CommandRejection.NotActiveController;
        }
        if (sim.PendingTacticStudy is { } study)
        {
            if (command is not ChooseTacticStudentCommand) return CommandRejection.DecisionPending;
            return study.RespondingForceId == command.IssuerId && sim.Controls.Any(c => c.ForceId == command.IssuerId &&
                c.Kind == ControllerKind.Player && c.ControllerId == context.ControllerId)
                ? CommandRejection.None : CommandRejection.NotActiveController;
        }
        if (sim.PendingExecutionSuccession is { } succession)
        {
            if (command is not ChooseExecutionSuccessorCommand) return CommandRejection.DecisionPending;
            return succession.RespondingForceId == command.IssuerId && sim.Controls.Any(c => c.ForceId == command.IssuerId &&
                c.Kind == ControllerKind.Player && c.ControllerId == context.ControllerId)
                ? CommandRejection.None : CommandRejection.NotActiveController;
        }
        if (sim.PendingDailyTask is { } daily)
        {
            if (command is not RespondDailyTaskCommand) return CommandRejection.DecisionPending;
            return daily.RespondingForceId == command.IssuerId && sim.Controls.Any(c => c.ForceId == daily.RespondingForceId &&
                c.Kind == ControllerKind.Player && c.ControllerId == context.ControllerId)
                ? CommandRejection.None : CommandRejection.NotActiveController;
        }
        if (sim.PendingEvent is CountryTitleEventSession title)
        {
            if (command is not ChooseCountryTitleCommand) return CommandRejection.DecisionPending;
            return title.ForceId == command.IssuerId && sim.Controls.Any(c => c.ForceId == command.IssuerId &&
                c.Kind == ControllerKind.Player && c.ControllerId == context.ControllerId)
                ? CommandRejection.None : CommandRejection.NotActiveController;
        }
        if (sim.PendingEvent is OfficeRequestEventSession officeRequest)
        {
            if (command is not RespondOfficeRequestCommand) return CommandRejection.DecisionPending;
            return officeRequest.RespondingForceId == command.IssuerId && sim.Controls.Any(c => c.ForceId == command.IssuerId &&
                c.Kind == ControllerKind.Player && c.ControllerId == context.ControllerId)
                ? CommandRejection.None : CommandRejection.NotActiveController;
        }
        if (sim.PendingEvent is AllianceEventSession { Proposal: var proposal })
        {
            if (command is not AllianceParticipationCommand) return CommandRejection.DecisionPending;
            int respondent = proposal.PlayerChoices.First(p => p.Join is null).ForceId;
            return respondent == command.IssuerId && sim.Controls.Any(c => c.ForceId == respondent &&
                c.Kind == ControllerKind.Player && c.ControllerId == context.ControllerId)
                ? CommandRejection.None : CommandRejection.NotActiveController;
        }
        if (sim.Boundary != SimulationBoundary.Stable) return CommandRejection.NotStable;
        if (command is ChooseCountryTitleCommand or RespondOfficeRequestCommand or ChooseDuelSuccessorCommand or AllianceParticipationCommand or RespondDailyTaskCommand or ChooseExecutionSuccessorCommand or ChooseTacticStudentCommand) return CommandRejection.InvalidDecision;
        if (sim.ActiveControl >= sim.Controls.Length) return CommandRejection.NotActiveController;
        var seat = sim.Controls[sim.ActiveControl];
        return CorpsControl.Authorize(Snapshot, seat, context, command);
    }

    private CommandExecutionResult Reject(CommandRejection rejection, DevelopmentOutcome? development = null) =>
        new(CommandSubmissionResult.Reject(rejection), [], Snapshot, development);

    public AdvanceXunResult EndControl(CommandContext context)
    {
        var seat = _world.EndControl(context);
        return new(Array.AsReadOnly<DomainEvent>([new ControlEndedEvent(seat.ControllerId, seat.ForceId)]), Snapshot);
    }

    public AdvanceXunResult AdvanceXun()
    {
        if (_world.IsExecuting) throw new InvalidOperationException("Simulation is executing.");
        if (_world.Simulation.Boundary == SimulationBoundary.Terminal) return new([], Snapshot);
        if (_world.Simulation.Boundary != SimulationBoundary.Stable ||
            _world.Simulation.ActiveControl != _world.Simulation.Controls.Length)
            throw new InvalidOperationException("All controllers must finish before advancing.");
        _world.EnterExecution();
        try
        {
            GameClock previous = _world.Clock;
            var mutation = _world.BeginMutation(_rules.Abilities);
            List<DomainEvent> events = [];
            MilitaryDiplomacy.BeginAdvance(mutation, _rules, events);
            ContinueXun(mutation, previous, events);
            AttackRequests.Cleanup(mutation, events);
            CorpsDelegations.Cleanup(mutation, _rules.Military, events);
            mutation.MaintainLeadership(events);
            _world.Commit(mutation);
            return new(events.AsReadOnly(), Snapshot);
        }
        finally { _world.ExitExecution(); }
    }

    private void ContinueXun(WorldMutation mutation, GameClock previous, List<DomainEvent> events)
    {
        GameClock next = previous.Advance();
        while (mutation.Clock != next && mutation.Simulation.Boundary != SimulationBoundary.Terminal &&
            mutation.Simulation.PendingDailyTask is null && mutation.Simulation.PendingDuel is null)
            _pipeline.AdvanceDay(mutation, events, previous);
        if (mutation.Simulation.Boundary != SimulationBoundary.Terminal && mutation.Simulation.PendingDailyTask is null && mutation.Simulation.PendingDuel is null)
            _pipeline.Prepare(mutation, events);
        if (mutation.Clock == next && mutation.Simulation.PendingDailyTask is null && mutation.Simulation.PendingDuel is null)
            events.Add(new XunAdvancedEvent(previous, mutation.Clock));
    }
}

public sealed record AdvanceXunResult(IReadOnlyList<DomainEvent> Events, WorldSnapshot Snapshot);
