using Godot;
using San9.Content.Images;
using San9.Rendering;

namespace San9.Client.Godot;

public sealed class TextureScope(TextureService service, IReadOnlyDictionary<string, ImageAsset> assets, bool prefetch = false)
    : IDisposable
{
    private sealed record Binding(ImageAsset Asset, AtlasTexture View, TextureCache<ImageTexture>.Lease Lease)
    { public bool Installed; public bool Reported; }
    private readonly Dictionary<string, Binding> _bindings = [];
    private bool _disposed;
    private readonly Dictionary<CanvasItem, Action> _consumers = [];
    public long Revision { get; private set; }
    public TextureService Service => service;
    public bool IsReady => _bindings.Values.All(b => b.Installed);
    public Exception? Error => _bindings.Values.Select(b => b.Lease.Error).FirstOrDefault(e => e is not null);
    public IReadOnlyDictionary<string, ImageAsset> Assets => assets;
    public int BindingCount => _bindings.Count;
    internal int RedrawConsumerCount => _consumers.Count;
    public Texture2D this[string key]
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _bindings.TryGetValue(key, out var binding) ? binding.View :
                throw new InvalidOperationException($"Texture '{key}' must be requested before binding or drawing.");
        }
    }
    /// <summary>Bind the actual drawing node, independently of the scope's creator or scene parent.</summary>
    public Texture2D Get(string key, CanvasItem consumer)
    {
        var texture = this[key];
        BindRedraw(consumer);
        return texture;
    }
    public void BindRedraw(CanvasItem consumer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_consumers.ContainsKey(consumer)) AttachConsumer(consumer);
    }
    private void AttachConsumer(CanvasItem consumer)
    {
        void Detach()
        {
            consumer.TreeExiting -= Detach;
            _consumers.Remove(consumer);
        }
        _consumers.Add(consumer, Detach);
        consumer.TreeExiting += Detach;
    }
    private void Request(string key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_bindings.ContainsKey(key)) return;
        var asset = assets[key];
        var view = new AtlasTexture { Region = new(asset.X, asset.Y, asset.Width, asset.Height), FilterClip = true };
        var binding = new Binding(asset, view, service.Cache.Acquire([asset.File], prefetch));
        _bindings.Add(key, binding); Refresh(binding);
    }
    public void RequestPrefixes(params string[] prefixes) => Request(assets.Keys.Where(key =>
        !TextureGroups.IsPortrait(key) && prefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal))));
    public bool AreReady(IEnumerable<string> keys) => keys.All(k => _bindings.TryGetValue(k, out var b) && b.Installed);
    public Exception? ErrorFor(IEnumerable<string> keys) => keys.Select(k => _bindings.GetValueOrDefault(k)?.Lease.Error).FirstOrDefault(e => e is not null);
    public bool TryGetReady(string key, out Texture2D texture)
    {
        if (_bindings.TryGetValue(key, out var binding) && binding.Installed && !prefetch)
        { texture = binding.View; return true; }
        texture = null!; return false;
    }
    public void RequestCached(IEnumerable<string> keys)
    {
        foreach (string key in keys)
            if (service.Cache.Contains(assets[key].File)) Request(key);
    }
    public void Request(IEnumerable<string> keys) { foreach (string key in keys) Request(key); }
    public void RetryFailed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        service.Cache.RetryFailed(_bindings.Values.Select(b => b.Asset.File));
        foreach (var binding in _bindings.Values.Where(b => !b.Installed)) binding.Reported = false;
    }
    public void Refresh()
    {
        bool changed = false;
        foreach (var binding in _bindings.Values) changed |= Refresh(binding);
        if (changed)
            foreach (var consumer in _consumers.Keys)
                if (GodotObject.IsInstanceValid(consumer) && consumer.IsInsideTree()) consumer.QueueRedraw();
    }
    private bool Refresh(Binding binding)
    {
        if (prefetch && !binding.Installed && binding.Lease.IsComplete)
        { binding.Installed = true; binding.Lease.Dispose(); return false; }
        bool changed = false;
        if (!binding.Installed && binding.Lease.IsReady)
        { binding.View.Atlas = service.Cache.Get(binding.Asset.File); binding.Installed = true; Revision++; changed = true; }
        if (!binding.Reported && binding.Lease.Error is { } error)
        { GD.PushError($"Texture {binding.Asset.Id}: {error.Message}"); binding.Reported = true; changed = true; }
        return changed;
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        foreach (var (consumer, detach) in _consumers)
            if (GodotObject.IsInstanceValid(consumer)) consumer.TreeExiting -= detach;
        _consumers.Clear();
        service.Retire(this, () =>
        {
            foreach (var b in _bindings.Values)
            { b.View.Atlas = null; b.View.Dispose(); b.Lease.Dispose(); }
            _bindings.Clear();
        });
    }
}
