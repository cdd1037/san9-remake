using San9.Core.Domain;

namespace San9.Core.Simulation;

internal sealed partial class WorldMutation
{
    private Dictionary<int, int>? _trustBefore;

    internal void MaintainDerivedState(List<DomainEvent> events)
    {
        MaintainLeadership(events);
        PublishTrustChanges(events);
    }

    // Rebuild at new-game/load entry, not at each mutation, query, day or xun.
    internal void RebuildForceTrust()
    {
        EnsureOpen();
        foreach (int id in Forces.Keys.Order().ToArray()) RefreshForceTrust(id);
    }

    private void RefreshForceTrust(int? first, int? second = null)
    {
        void Refresh(int? id)
        {
            if (id is not int key || !Forces.TryGetValue(key, out var force) || force.IsEliminated) return;
            int trust = ForceTrust.Calculate(this, _economy, force);
            if (trust == force.Trust) return;
            (_trustBefore ??= []).TryAdd(key, force.Trust);
            var next = force with { Trust = trust };
            AllForces = AllForces.SetItem(key, next);
            Forces = Forces.SetItem(key, next);
        }
        Refresh(first);
        if (second != first) Refresh(second);
    }

    private void PublishTrustChanges(List<DomainEvent> events)
    {
        if (_trustBefore is null) return;
        foreach (var (id, before) in _trustBefore.OrderBy(p => p.Key))
            if (Forces.TryGetValue(id, out var force) && !force.IsEliminated && before != force.Trust)
                events.Add(new ForceTrustChangedEvent(Clock, id, before, force.Trust));
        _trustBefore = null;
    }
}
