namespace San9.Core.Simulation;

public sealed record OfficerCapturedEvent(GameClock Clock, int OfficerId, int CaptorId, int? SettlementId, int? UnitId) : DomainEvent;

[System.Text.Json.Serialization.JsonDerivedType(typeof(AttackRequestEstablishedEvent), "attack-request-established")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(GiftResolvedEvent), "gift-resolved")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(AttackRequestEndedEvent), "attack-request-ended")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(HighSpiritsExpiredEvent), "high-spirits-expired")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(CityBattleDamageEvent), "city-battle-damage")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(CityCapturePublicOrderEvent), "city-capture-public-order")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BattleDamageEvent), "battle-damage")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(PrisonerEscapedEvent), "prisoner-escaped")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(SettlementCapturedEvent), "settlement-captured")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(FacilityDestroyedEvent), "facility-destroyed")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(FacilityDemolishedEvent), "facility-demolished")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(FacilityConstructionChangedEvent), "facility-construction-changed")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(ForceEliminatedEvent), "force-eliminated")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(ForceLeaderChangedEvent), "force-leader-changed")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(OfficerCapturedEvent), "officer-captured")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(UnitTerminatedEvent), "unit-terminated")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(OfficerEscapedBattleEvent), "officer-escaped-battle")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(TacticSelectedEvent), "tactic-selected")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BattleConditionChangedEvent), "battle-condition-changed")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(IntelligenceEffectResolvedEvent), "intelligence-effect-resolved")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(TacticMoraleChangedEvent), "tactic-morale-changed")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(TacticHealingEvent), "tactic-healing")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(TacticActivatedEvent), "tactic-activated")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(TacticProficiencyGrownEvent), "tactic-proficiency-grown")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(TacticLearnedEvent), "tactic-learned")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(UnitDeployedEvent), "unit-deployed")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(UnitReformedEvent), "unit-reformed")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(DailyAttritionEvent), "daily-attrition")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(GarrisonMoraleRecoveredEvent), "garrison-morale-recovered")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(PeriodicMoraleLostEvent), "periodic-morale-lost")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(UnitOrderChangedEvent), "unit-order-changed")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(UnitPoliciesChangedEvent), "unit-policies-changed")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(UnitEngagementChangedEvent), "unit-engagement-changed")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(UnitMovedEvent), "unit-moved")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(UnitAbsorbedEvent), "unit-absorbed")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(AdviserAppointedEvent), "adviser-appointed")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(SearchResolvedEvent), "search-resolved")]

[System.Text.Json.Serialization.JsonDerivedType(typeof(RecruitmentResolvedEvent), "recruitment-resolved")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(PersonAffiliationChangedEvent), "person-affiliation-changed")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(OfficerMovedEvent), "officer-moved")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(ForceRelationChangedEvent), "force-relation-changed")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(WarDeclaredEvent), "war-declared")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(AllianceEndedEvent), "alliance-ended")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(AllianceProposedEvent), "alliance-proposed")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(AllianceParticipationDecidedEvent), "alliance-participation-decided")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(AllianceFormedEvent), "alliance-formed")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(AllianceFormationFailedEvent), "alliance-formation-failed")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BurnResolvedEvent), "strategy-burn-resolved")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(AlienateResolvedEvent), "strategy-alienate-resolved")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(SeizeResolvedEvent), "strategy-seize-resolved")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(GossipResolvedEvent), "strategy-gossip-resolved")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(PrisonerRecoveredEvent), "prisoner-recovered")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(UnitStrategyResolvedEvent), "unit-strategy-resolved")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(AllianceMemberRemovedEvent), "alliance-member-removed")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(OfficerAbilityGrownEvent), "officer-ability-grown")]

[System.Text.Json.Serialization.JsonDerivedType(typeof(OfficerRewardedEvent), "officer-rewarded")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(ResourceChangedEvent), "resource-changed")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(OfficerLoyaltyChangedEvent), "loyalty-changed")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(ForceTrustChangedEvent), "trust-changed")]
[System.Text.Json.Serialization.JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(DevelopmentCompletedEvent), "development-completed")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(CommandResolvedEvent), "command-resolved")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(XunAdvancedEvent), "xun-advanced")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(StageCompletedEvent), "stage-completed")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(TaskStartedEvent), "task-started")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(TaskCompletedEvent), "task-completed")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(TaskCancelledEvent), "task-cancelled")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(SimulationEndedEvent), "simulation-ended")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(ControlEndedEvent), "control-ended")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(TreasureTransferredEvent), "treasure-transferred")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(OfficerExiledEvent), "officer-exiled")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(CorpsDissolvedEvent), "corps-dissolved")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(CorpsCreatedEvent), "corps-created")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(CorpsChangedEvent), "corps-changed")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(PersonnelReturnedEvent), "personnel-returned")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(CommanderChangedEvent), "commander-changed")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(CorpsLeaderChangedEvent), "corps-leader-changed")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(PrisonerReleasedEvent), "prisoner-released")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(OfficeAssignedEvent), "office-assigned")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(CorpsControlChangedEvent), "corps-control-changed")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(PersonnelTerritoryTransferredEvent), "personnel-territory-transferred")]
public abstract record DomainEvent;

public sealed record DevelopmentCompletedEvent(
    GameClock Clock,
    San9.Core.Domain.DevelopmentCommand Command,
    int Increment,
    int Cost) : DomainEvent;

public sealed record CommandResolvedEvent(
    Guid CommandId,
    int IssuerId,
    string CommandType)
    : DomainEvent
{
    public long Sequence { get; init; }
}

public sealed record XunAdvancedEvent(
    GameClock Previous,
    GameClock Current)
    : DomainEvent;
