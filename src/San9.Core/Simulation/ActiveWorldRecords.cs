using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Immutable participation indices shared by branches of the same committed world.</summary>
internal sealed record ActiveWorldRecords(
    ImmutableDictionary<int, ForceState> Forces,
    ImmutableDictionary<int, SettlementState> Settlements,
    ImmutableDictionary<int, OfficerState> Officers)
{
    internal static ActiveWorldRecords Create(ImmutableDictionary<int, ForceState> forces,
        ImmutableDictionary<int, SettlementState> settlements, ImmutableDictionary<int, OfficerState> officers) => new(
            forces.RemoveRange(forces.Values.Where(f => f.IsDormant).Select(f => f.Id)),
            settlements.RemoveRange(settlements.Values.Where(s => s.IsDormant).Select(s => s.Id)),
            officers.RemoveRange(officers.Values.Where(p => p.IsDormant).Select(p => p.Id)));
}
