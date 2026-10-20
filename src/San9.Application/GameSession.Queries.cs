using System.Collections.Immutable;
using San9.Application.AI;
using San9.Application.Presentation;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application;

public sealed partial class GameSession
{
    public WorldMessageContext Messages(San9.Content.Text.TextCatalog texts) => new(_definition.Content, _snapshotSource, Snapshot, texts);
    public WorldMessageContext Messages(San9.Content.Text.TextCatalog texts, WorldSnapshot snapshot) => new(_definition.Content, _snapshotSource, snapshot, texts);
    public CountryTitlePrompt? GetCountryTitlePrompt() => _simulation.GetCountryTitlePrompt();
    public EmperorTransferPrompt? GetEmperorTransferPrompt() => _simulation.GetEmperorTransferPrompt();
    public OfficeRequestPrompt? GetOfficeRequestPrompt() => _simulation.GetOfficeRequestPrompt();
    public SearchRecruitmentPrompt? GetSearchRecruitmentPrompt() => _simulation.GetSearchRecruitmentPrompt();
    public ShanyangGongPrompt? GetShanyangGongPrompt() => _simulation.GetShanyangGongPrompt();
    public MysticBookOfferPrompt? GetMysticBookOfferPrompt() => _simulation.GetMysticBookOfferPrompt();
    public MysticBookReturnPrompt? GetMysticBookReturnPrompt() => _simulation.GetMysticBookReturnPrompt();
    public ScholarInsultPrompt? GetScholarInsultPrompt() => _simulation.GetScholarInsultPrompt();
    public LongevityPrompt? GetLongevityPrompt() => _simulation.GetLongevityPrompt();
    public TreasureMonumentPrompt? GetTreasureMonumentPrompt() => _simulation.GetTreasureMonumentPrompt();
    public ScholarReturnPrompt? GetScholarReturnPrompt() => _simulation.GetScholarReturnPrompt();
    public FoodTradeQuote GetFoodTradeQuote(int forceId, int settlementId, ImmutableArray<int> officerIds, FoodTradeDirection direction) =>
        _simulation.GetFoodTradeQuote(forceId, settlementId, officerIds, direction);
    public MerchantInvestmentPrompt? GetMerchantInvestmentPrompt() => _simulation.GetMerchantInvestmentPrompt();
    public CommunitySearchPrompt? GetCommunitySearchPrompt() => _simulation.GetCommunitySearchPrompt();
    public SearchConflictPrompt? GetSearchConflictPrompt() => _simulation.GetSearchConflictPrompt();
    public TigerSearchPrompt? GetTigerSearchPrompt() => _simulation.GetTigerSearchPrompt();
    public BeastSearchPrompt? GetBeastSearchPrompt() => _simulation.GetBeastSearchPrompt();
    public TerminalResult GetTerminalResult() => Snapshot.Simulation.Boundary == SimulationBoundary.Terminal
        ? TerminalEvaluation.Evaluate(Snapshot, _definition.Content.Terminal) : new(TerminalRoute.Continue, null, null);
    public EndingSnapshot? GetEndingPrompt() => _ending is null ? EndingSession.Create(_definition, Snapshot) : null;
    public EndingResult? Ending => _ending is null ? null : EndingSession.Resolve(_definition, Snapshot, _ending);
    public TacticStudyPrompt? GetTacticStudyPrompt() => _simulation.GetTacticStudyPrompt();

    public SnipingSuccessionPrompt? GetSnipingSuccessionPrompt() => _simulation.GetSnipingSuccessionPrompt();

    public DuelSuccessionPrompt? GetDuelSuccessionPrompt() => _simulation.GetDuelSuccessionPrompt();

    public CommandAdvice GetCounterofferAdvice(Guid taskId, CompensationOffer offer) => _simulation.GetCounterofferAdvice(taskId, offer);
    public ResponseRequest? GetPendingResponse() => _simulation.GetPendingResponse();
    public CapturedRulerPrompt? GetCapturedRulerPrompt() => _simulation.GetCapturedRulerPrompt();
    public ExecutionSuccessionPrompt? GetExecutionSuccessionPrompt() => _simulation.GetExecutionSuccessionPrompt();
    public GameCommand ChooseDomesticCommand(ControlSeat seat, Guid commandId, int turn, bool scripted = false) =>
        DomesticCommandSelection.Choose(Snapshot, _simulation.GetAbilities(Snapshot), Validate, seat, new(seat.ControllerId), commandId, turn, scripted);
    public CommandAdvice GetSurrenderAdvice(int forceId, int officerId, int targetForceId) =>
        _simulation.GetSurrenderAdvice(forceId, officerId, targetForceId);
    public AttackRequestPrompt? GetAttackRequestPrompt() => _simulation.GetAttackRequestPrompt();
    public CommandAdvice GetAttackRequestAdvice(int forceId, int officerId, int targetForceId, int targetSettlementId) =>
        _simulation.GetAttackRequestAdvice(forceId, officerId, targetForceId, targetSettlementId);
    public PrisonerReturnPrompt? GetPrisonerReturnPrompt() => _simulation.GetPrisonerReturnPrompt();
    public CommandAdvice GetPrisonerReturnAdvice(int forceId, int officerId, int targetForceId, int prisonerId) =>
        _simulation.GetPrisonerReturnAdvice(forceId, officerId, targetForceId, prisonerId);
    public CommandAdvice GetGiftAdvice(int forceId, int officerId, int targetForceId, GiftOffer offer) =>
        _simulation.GetGiftAdvice(forceId, officerId, targetForceId, offer);
    public AllianceFormationCandidates GetAllianceCandidates() => _simulation.GetAllianceCandidates();
    public AllianceParticipationPrompt? GetAllianceParticipationPrompt() => _simulation.GetAllianceParticipationPrompt();
    public AttackWarning GetAttackWarning(int forceId, MilitaryOrder order) => _simulation.GetAttackWarning(forceId, order);

    public CommandAdvice GetBurnAdvice(int forceId, int officerId, int targetSettlementId) =>
        _simulation.GetBurnAdvice(forceId, officerId, targetSettlementId);

    public System.Collections.Immutable.ImmutableArray<int> GetSearchPlaces(int forceId) => _simulation.GetSearchPlaces(forceId);
    public System.Collections.Immutable.ImmutableArray<int> GetRecruitTargets(int forceId, int officerId) => _simulation.GetRecruitTargets(forceId, officerId);
    public PersonnelJourneyPreview GetPersonnelJourney(int forceId, int officerId, int targetId, PersonnelJourneyKind kind) =>
        _simulation.GetPersonnelJourney(forceId, officerId, targetId, kind);

    public CommandAdvice GetUnitStrategyAdvice(int forceId, int officerId, UnitStrategyKind strategy, int targetUnitId) =>
        _simulation.GetUnitStrategyAdvice(forceId, officerId, strategy, targetUnitId);
    public CommandAdvice GetAlienateAdvice(int forceId, int officerId, int targetOfficerId) =>
        _simulation.GetAlienateAdvice(forceId, officerId, targetOfficerId);
    public CommandAdvice GetSeizeAdvice(int forceId, int officerId, int targetSettlementId) =>
        _simulation.GetSeizeAdvice(forceId, officerId, targetSettlementId);
    public CommandAdvice GetGossipAdvice(int forceId, int officerId, int firstTargetForceId,
        int secondTargetForceId) => _simulation.GetGossipAdvice(forceId, officerId, firstTargetForceId,
        secondTargetForceId);
    public CommandSubmissionResult Validate(CommandContext context, GameCommand command) => _executing
        ? CommandSubmissionResult.Reject(CommandRejection.NotStable) : _simulation.Validate(context, command);
    public CommandPreparationPreview Preview(CommandContext context, GameCommand command) => _executing
        ? new(CommandRejection.NotStable, null, null) : _simulation.Preview(context, command);
    public ImmutableArray<int> GetGiftTreasures(int forceId) => _executing ? [] : _simulation.GetGiftTreasures(forceId);
    public CommandPreparationPreview GetMissionPreparation(CommandContext context, GameCommand command) => _executing
        ? new(CommandRejection.NotStable, null, null) : _simulation.GetMissionPreparation(context, command);
    public bool CanSelectCorpsDelegation(CommandContext context, int owner, CorpsDelegation delegation) =>
        !_executing && _simulation.CanSelectCorpsDelegation(context, owner, delegation);
    public CommandRejection GetPreparationEligibility(CommandContext context, GameCommand command) => _executing
        ? CommandRejection.NotStable : _simulation.GetPreparationEligibility(context, command);
}
