using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Simulation;

namespace San9.Core.Domain;

public sealed record SunMarriageDefinition(
    [property: JsonRequired] int RecipientRulerId,
    [property: JsonRequired] int BrideId,
    [property: JsonRequired] int DonorBloodlineId,
    [property: JsonRequired] int MinimumActiveForces,
    [property: JsonRequired] GameClock LatestDate,
    [property: JsonRequired] int RequiredRelationExclusive,
    [property: JsonRequired] int RelationBonus,
    [property: JsonRequired] int Loyalty,
    [property: JsonRequired] ImmutableArray<int> GrantedTacticIds,
    [property: JsonRequired] PersistentEventKey ConsumptionKey)
{
    public EventCandidate Candidate() => new("sun-marriage", EventTrigger.Quarter, 0, EventConsumption.OnSuccess, ConsumptionKey);

    public void Validate(IReadOnlySet<int> officers, IReadOnlySet<int> tactics)
    {
        _ = Candidate();
        _ = new GameClock(LatestDate.Year, LatestDate.Month, LatestDate.Day);
        if (RecipientRulerId == BrideId || !officers.Contains(RecipientRulerId) || !officers.Contains(BrideId) ||
            DonorBloodlineId < 0 || MinimumActiveForces < 1 || RequiredRelationExclusive is < 0 or > 100 ||
            RelationBonus is < 0 or > 100 || Loyalty is < 0 or > 255 || GrantedTacticIds.IsDefault ||
            GrantedTacticIds.Distinct().Count() != GrantedTacticIds.Length || GrantedTacticIds.Any(id => !tactics.Contains(id)))
            throw new ArgumentException("Invalid Sun marriage event definition.");
    }
}
