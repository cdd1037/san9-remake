using System.Text.Json.Serialization;
using San9.Core.Simulation;

namespace San9.Core.Domain;

public sealed record ShanyangGongDefinition(
    [property: JsonRequired] int EmperorId,
    [property: JsonRequired] int PlaceId,
    [property: JsonRequired] string ImperialSurname,
    [property: JsonRequired] int MinimumTrust,
    [property: JsonRequired] int MinimumControlledProvinces,
    [property: JsonRequired] int BaseTrustBonus,
    [property: JsonRequired] int LoyaltyBonus,
    [property: JsonRequired] int MinimumRighteousness,
    [property: JsonRequired] int ExecutionLoyaltyPenalty,
    [property: JsonRequired] int RelationPenalty,
    [property: JsonRequired] PersistentEventKey ConsumptionKey)
{
    public void Validate(IReadOnlySet<int> officers, IReadOnlySet<int> places, bool hasImperialRank)
    {
        if (!officers.Contains(EmperorId) || !places.Contains(PlaceId) || !hasImperialRank ||
            string.IsNullOrWhiteSpace(ImperialSurname) || MinimumTrust < 0 || MinimumControlledProvinces < 0 ||
            BaseTrustBonus < 0 || LoyaltyBonus < 0 || MinimumRighteousness is < 0 or > 15 ||
            ExecutionLoyaltyPenalty < 0 || RelationPenalty < 0 || string.IsNullOrWhiteSpace(ConsumptionKey.Value))
            throw new ArgumentException("Invalid Shanyang Gong definition.");
    }
}
