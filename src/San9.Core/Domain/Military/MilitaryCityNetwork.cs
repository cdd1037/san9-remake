using System.Collections.Immutable;

namespace San9.Core.Domain;

/// <summary>Model-owned traversal geometry, independent of the player's available or default formations.</summary>
public sealed record MilitaryCityReferenceProfile(Footprint LandShape, Footprint? WaterShape,
    int LandCostScale = 1, int WaterCostScale = 1)
{
    public static MilitaryCityReferenceProfile Calibrated { get; } = new(new(5, 5), new Footprint(4, 4));
}
public sealed record MilitaryCityNetworkDiagnostics(int DistanceFields, int ClearanceSearches, int BypassSearches, long ExpandedNodes);

/// <summary>Immutable strategic city links. Geography, ownership and current troop plans are separate queries.</summary>
public sealed partial class MilitaryCityNetwork
{
    private readonly ImmutableDictionary<int, ImmutableArray<int>> neighbors;
    private readonly HashSet<(int From, int To)> membership;
    public ImmutableArray<CityConnection> Connections { get; }
    public MilitaryCityNetworkDiagnostics Diagnostics { get; }
    public static MilitaryCityNetwork Empty { get; } = new([]);

    public MilitaryCityNetwork(IEnumerable<CityConnection> connections)
        : this(connections.ToImmutableArray(), new(0, 0, 0, 0)) { }

    private MilitaryCityNetwork(ImmutableArray<CityConnection> connections, MilitaryCityNetworkDiagnostics diagnostics)
    {
        if (connections.Any(c => c.FromCityId < 0 || c.ToCityId < 0 || c.FromCityId == c.ToCityId) ||
            connections.Distinct().Count() != connections.Length)
            throw new ArgumentException("Invalid military city connections.");
        Connections = connections;
        Diagnostics = diagnostics;
        membership = connections.Select(c => (c.FromCityId, c.ToCityId)).ToHashSet();
        neighbors = connections.GroupBy(c => c.FromCityId)
            .ToImmutableDictionary(g => g.Key, g => g.Select(c => c.ToCityId).ToImmutableArray());
    }

    public ImmutableArray<int> Neighbors(int cityId) => neighbors.GetValueOrDefault(cityId, []);
    public IEnumerable<int> NearbyCities(int cityId) => Neighbors(cityId).Prepend(cityId);
    public bool AreNeighbors(int first, int second) => membership.Contains((first, second));

    /// <summary>Generate once from immutable authored map inputs; no world-state or random-stream access.</summary>
    public static MilitaryCityNetwork Generate(MilitaryRulebook rules, IEnumerable<int> cityIds,
        MilitaryCityReferenceProfile? reference = null)
    {
        var ids = cityIds.Order().ToArray();
        if (ids.Distinct().Count() != ids.Length || ids.Any(id => !rules.Places.ContainsKey(id)))
            throw new ArgumentException("Military cities require distinct place references.");
        if (ids.Length < 2) return Empty;
        reference ??= MilitaryCityReferenceProfile.Calibrated;
        if (!reference.LandShape.IsValid || reference.WaterShape is { IsValid: false } ||
            reference.LandCostScale <= 0 || reference.WaterCostScale <= 0)
            throw new ArgumentException("Invalid military city reference profile.");
        // Fine-coordinate cities in one navigation cell share a strategic site. Expand
        // site links back to identities rather than imposing a coarse-grid identity limit.
        var sites = ids.GroupBy(id => MapCoordinates.Cell(rules.Places[id].Position)).Select(g => g.ToArray()).ToArray();
        var generated = sites.Length > 1 ? new Generator(rules, sites, reference).Build() : Empty;
        var members = sites.ToDictionary(s => s[0]);
        var expanded = generated.Connections.SelectMany(c => members[c.FromCityId]
            .SelectMany(from => members[c.ToCityId].Select(to => new CityConnection(from, to))));
        var local = sites.SelectMany(s => s.SelectMany(from => s.Where(to => to != from).Select(to => new CityConnection(from, to))));
        return new(expanded.Concat(local).OrderBy(c => c.FromCityId).ThenBy(c => c.ToCityId).ToImmutableArray(), generated.Diagnostics);
    }
}
