using System.Text.Json.Serialization;

namespace San9.Core.Domain;

[JsonPolymorphic]
[JsonDerivedType(typeof(GeneralDelegation), "general")]
[JsonDerivedType(typeof(DomesticDelegation), "domestic")]
[JsonDerivedType(typeof(DefeatForceDelegation), "defeat-force")]
[JsonDerivedType(typeof(CaptureSettlementDelegation), "capture-settlement")]
public abstract record CorpsDelegation;
public sealed record GeneralDelegation : CorpsDelegation;
public sealed record DomesticDelegation : CorpsDelegation;
public sealed record DefeatForceDelegation([property: JsonRequired] int ForceId) : CorpsDelegation;
public sealed record CaptureSettlementDelegation([property: JsonRequired] int SettlementId) : CorpsDelegation;
