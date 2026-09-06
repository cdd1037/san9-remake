namespace San9.Core.Domain;

public sealed record ForceRelation(int FirstId, int SecondId, int Value);

public sealed record CorpsTreasury(uint Money, uint Food);
public sealed record CorpsState(int Id, int OwnerId, int LeaderId, CorpsTreasury? Treasury);
public sealed record TreasureState(int Id, int? HolderId, bool Discovered);
public sealed record ChronicleEntry(int Year, int Month, int Day, int ForceId, string Text);
