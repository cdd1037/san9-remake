using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Simulation;

namespace San9.Core.Domain;

public sealed record ThreeVisitsDefinition(
    [property: JsonRequired] int RulerId,
    [property: JsonRequired] int RecruitId,
    [property: JsonRequired] int RecommenderId,
    [property: JsonRequired] ImmutableArray<int> CompanionIds,
    [property: JsonRequired] int PlaceId,
    [property: JsonRequired] int RequiredLoyaltyExclusive,
    [property: JsonRequired] int DurationDays,
    [property: JsonRequired] int RecruitmentLoyalty,
    [property: JsonRequired] int BaseTrustBonus,
    [property: JsonRequired] PersistentEventKey StartedKey,
    [property: JsonRequired] PersistentEventKey FirstVisitKey,
    [property: JsonRequired] PersistentEventKey CompletedKey)
{
    public void Validate(IReadOnlySet<int> officers, IReadOnlySet<int> places)
    {
        if (CompanionIds.IsDefaultOrEmpty ||
            new[] { RulerId, RecruitId, RecommenderId }.Concat(CompanionIds).Any(id => !officers.Contains(id)) ||
            new[] { RulerId, RecruitId, RecommenderId }.Concat(CompanionIds).Distinct().Count() != CompanionIds.Length + 3 ||
            !places.Contains(PlaceId) || RequiredLoyaltyExclusive is < 0 or >= 255 || DurationDays <= 0 ||
            RecruitmentLoyalty is < 0 or > 255 || BaseTrustBonus < 0 ||
            new[] { StartedKey, FirstVisitKey, CompletedKey }.Any(k => string.IsNullOrWhiteSpace(k.Value)) ||
            new[] { StartedKey, FirstVisitKey, CompletedKey }.Distinct().Count() != 3)
            throw new ArgumentException("Invalid Three Visits definition.");
    }

    public void ValidateStarted(GameClock? started, GameClock current, IEnumerable<PersistentEventKey> consumed)
    {
        if (started is not { } date) return;
        _ = new GameClock(date.Year, date.Month, date.Day);
        if (!date.IsXunBoundary || date.DayNumber > current.DayNumber || !consumed.Contains(StartedKey))
            throw new ArgumentException("Invalid Three Visits starting date.");
    }
}
