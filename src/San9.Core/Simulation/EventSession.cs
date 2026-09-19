using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace San9.Core.Simulation;

public enum EventChoice { Accept, Reject }

// Selection continuations are owned by the calendar pipeline. They are deliberately
// absent from saves: only a completed event transaction is a persistence boundary.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(AllianceEventSession), "alliance")]
[JsonDerivedType(typeof(OfficeRequestEventSession), "office-request")]
[JsonDerivedType(typeof(CountryTitleEventSession), "country-title")]
public abstract record EventSession
{
    public abstract ResolutionStage Stage { get; }
    public abstract int RespondingForceId { get; }
}

public sealed record AllianceEventSession(AllianceProposal Proposal) : EventSession
{
    public override ResolutionStage Stage => ResolutionStage.QuarterEvents;
    public override int RespondingForceId => Proposal.RespondingForceId!.Value;
}

public sealed record OfficeRequestEventSession(int OfficerId, int ForceId, int SettlementId,
    ImmutableArray<int> RemainingOfficerIds) : EventSession
{
    public override ResolutionStage Stage => ResolutionStage.XunUpdate;
    public override int RespondingForceId => ForceId;
}

public sealed record OfficeRequestPrompt(int OfficerId, int ForceId, int SettlementId);
public sealed record RespondOfficeRequestCommand(Guid CommandId, int IssuerId, long Sequence,
    [property: JsonRequired] int OfficerId, [property: JsonRequired] EventChoice Choice) : GameCommand(CommandId, IssuerId, Sequence);
public sealed record OfficeRequestOfferedEvent(GameClock Clock, int OfficerId, int ForceId, int SettlementId) : DomainEvent;
public sealed record OfficeRequestResolvedEvent(GameClock Clock, int OfficerId, int ForceId, EventChoice Choice,
    bool Invalidated) : DomainEvent;
