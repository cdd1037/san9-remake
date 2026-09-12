namespace San9.Core.Simulation;


[System.Text.Json.Serialization.JsonDerivedType(typeof(UnitDeployedEvent), "unit-deployed")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(UnitOrderChangedEvent), "unit-order-changed")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(UnitMovedEvent), "unit-moved")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(UnitAbsorbedEvent), "unit-absorbed")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(AdviserAppointedEvent), "adviser-appointed")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(SearchResolvedEvent), "search-resolved")]

[System.Text.Json.Serialization.JsonDerivedType(typeof(RecruitmentResolvedEvent), "recruitment-resolved")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(PersonAffiliationChangedEvent), "person-affiliation-changed")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(OfficerMovedEvent), "officer-moved")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(ForceRelationChangedEvent), "force-relation-changed")]
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
