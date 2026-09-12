using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application;

public static class SessionProtocol
{
    public const string RulesVersion = "san9-core-g04-v17";
    public const int SaveFormatVersion = 11;
    public const int LogFormatVersion = 9;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(CommandOperation), "command")]
[JsonDerivedType(typeof(EndControlOperation), "end-control")]
[JsonDerivedType(typeof(AdvanceOperation), "advance-xun")]
[JsonDerivedType(typeof(ChronicleOperation), "chronicle-note")]
public abstract record SessionOperation;
public sealed record CommandOperation(GameCommand Command) : SessionOperation;
public sealed record EndControlOperation : SessionOperation;
public sealed record AdvanceOperation : SessionOperation;
public sealed record ChronicleOperation(string Text) : SessionOperation;

public sealed record SessionRequest(Guid Id, long Sequence, string ControllerId, SessionOperation Operation);
public enum SessionDisposition { Accepted, AlreadyProcessed, Rejected }
public enum SessionRejection { None, InvalidRequest, OutOfOrder, CommandRejected, NotStable, Terminal }

public sealed record SessionResult(SessionDisposition Disposition, SessionRejection Rejection,
    IReadOnlyList<DomainEvent> Events, WorldSnapshot Snapshot,
    CommandRejection CommandRejection = CommandRejection.None, DevelopmentOutcome? Development = null);

public sealed record RecordedOperation(SessionRequest Request, string WorldHash, ImmutableArray<DomainEvent> Events);
public sealed record SessionRecording(string ContentFingerprint, SessionSnapshot Initial, ImmutableArray<RecordedOperation> Operations, string FinalHash);

public sealed record SessionSnapshot(WorldSnapshot World, ImmutableArray<SessionRecord> Records,
    ImmutableArray<ChronicleEntry> Chronicle, long NextOperationSequence)
{
    [JsonIgnore]
    public string StateHash => CanonicalJson.Hash(new { version = CanonicalJson.Version,
        world = World.StateHash, records = Records, chronicle = Chronicle, nextOperationSequence = NextOperationSequence });
}
