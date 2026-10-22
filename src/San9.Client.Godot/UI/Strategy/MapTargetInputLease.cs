using San9.Application.Presentation;
using San9.Core.Domain;

namespace San9.Client.Godot;

/// <summary>Releases only the callbacks installed by this owner, including after session replacement.</summary>
internal sealed class MapTargetInputLease : IDisposable
{
    private readonly MapWorld _world;
    private readonly Action<MapObjectTarget?> _target;
    private readonly Action<MapCell>? _waypoint, _site;
    private bool _disposed;
    public MapTargetInputLease(MapWorld world, Action<MapObjectTarget?> target, Action<MapCell>? waypoint = null,
        Action<MapCell>? site = null)
    {
        _world = world; _target = target; _waypoint = waypoint; _site = site;
        world.TargetPicked = target; world.WaypointPicked = waypoint; world.SitePicked = site;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_world.TargetPicked == _target) _world.TargetPicked = null;
        if (_world.WaypointPicked == _waypoint) _world.WaypointPicked = null;
        if (_world.SitePicked == _site) _world.SitePicked = null;
    }
}
