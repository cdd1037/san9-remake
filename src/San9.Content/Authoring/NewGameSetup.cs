using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace San9.Content.Authoring;

public sealed record OfficerPlacement(int OfficerId, int CityId, bool JoinForce);
public sealed record CreatedForce(int RulerId, int CityId);

/// <summary>Transient opening draft. Consumed at startup; never retained in a session or save.</summary>
public sealed record NewGameSetup
{
    [JsonRequired] public ImmutableArray<OfficerPlacement> Placements { get; init; } = [];
    [JsonRequired] public ImmutableArray<CreatedForce> Forces { get; init; } = [];
    public static NewGameSetup Empty { get; } = new();
    public const int MaximumCreatedForces = 8;

    public void Validate()
    {
        if (Placements.IsDefault || Forces.IsDefault ||
            Placements.Any(p => p is null || p.CityId < 0 || p.OfficerId < 0) || Forces.Any(f => f is null || f.CityId < 0 || f.RulerId < 0) ||
            Placements.Select(p => p.OfficerId).Distinct().Count() != Placements.Length ||
            Forces.Length > MaximumCreatedForces || Forces.Select(f => f.RulerId).Distinct().Count() != Forces.Length ||
            Forces.Select(f => f.CityId).Distinct().Count() != Forces.Length)
            throw new InvalidDataException("开局部署资料无效。");
        if (Forces.Any(f => Placements.Any(p => p.OfficerId == f.RulerId)))
            throw new InvalidDataException("新势力君主与部署人物重复，或人物不在本次人物库中。");
    }
}
