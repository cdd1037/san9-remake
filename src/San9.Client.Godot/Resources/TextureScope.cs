using Godot;
using San9.Content.Images;
using San9.Rendering;

namespace San9.Client.Godot;

public sealed class TextureScope(TextureService service, IReadOnlyDictionary<string, ImageAsset> assets, bool prefetch = false)
    : IDisposable
{
    private sealed class Binding(ImageAsset asset, AtlasTexture view, TextureCache<ImageTexture>.Lease lease)
    {
        public readonly ImageAsset Asset = asset;
        public readonly AtlasTexture View = view;
        public TextureCache<ImageTexture>.Lease Lease = lease;
        public bool Installed;
        public Exception? Error;
    }
    private readonly Dictionary<string, Binding> _bindings = [];
    private readonly List<Binding> _pending = [], _failed = [];
    private int _remaining;
    private bool _disposed;
    private readonly Dictionary<CanvasItem, Action> _consumers = [];
    public long Revision { get; private set; }
    public TextureService Service => service;
    public bool IsReady => _remaining == 0;
    public Exception? Error => _failed.Count == 0 ? null : _failed[0].Error;
    internal bool NeedsRefresh => _pending.Count > 0;
    public IReadOnlyDictionary<string, ImageAsset> Assets => assets;
    public int BindingCount => _bindings.Count;
    internal int RedrawConsumerCount => _consumers.Count;
    /// <summary>Access a resource for skin composition. Drawing users must bind their own canvas item.</summary>
    internal Texture2D GetResource(string key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _bindings.TryGetValue(key, out var binding) ? binding.View :
            throw new InvalidOperationException($"Texture '{key}' must be requested before binding or drawing.");
    }
    /// <summary>Bind the actual drawing node, independently of the scope's creator or scene parent.</summary>
    public Texture2D Get(string key, CanvasItem consumer)
    {
        var texture = GetResource(key);
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
        _bindings.Add(key, binding); _remaining++;
        if (!Install(binding)) { _pending.Add(binding); service.ScheduleRefresh(this); }
    }
    public void RequestPrefixes(params string[] prefixes) => Request(assets.Keys.Where(key =>
        !TextureGroups.IsPortrait(key) && prefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal))));
    public bool AreReady(IEnumerable<string> keys) => keys.All(k => _bindings.TryGetValue(k, out var b) && b.Installed);
    public Exception? ErrorFor(IEnumerable<string> keys)
    {
        foreach (string key in keys)
            if (_bindings.TryGetValue(key, out var binding) && binding.Error is { } error) return error;
        return null;
    }
    public bool TryGetReady(string key, CanvasItem consumer, out Texture2D texture)
    {
        BindRedraw(consumer);
        return TryGetReadyResource(key, out texture);
    }
    /// <summary>Ready resource for native controls, cursor installation and sprite composition; no custom draw subscription.</summary>
    internal bool TryGetReadyResource(string key, out Texture2D texture)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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
        service.RetryFailed(_failed.Select(b => b.Asset.File));
    }
    internal void RetryFailed(IReadOnlySet<ImageFile> files)
    {
        for (int i = _failed.Count - 1; i >= 0; i--)
        {
            var binding = _failed[i];
            if (!files.Contains(binding.Asset.File)) continue;
            binding.Error = null;
            // A completed prefetch released its lease; a visible request still owns its failed lease.
            if (prefetch)
            {
                binding.Lease = service.Cache.Acquire([binding.Asset.File], prefetch: true);
                binding.Installed = false; _remaining++;
            }
            _pending.Add(binding); _failed.RemoveAt(i);
        }
        if (NeedsRefresh) service.ScheduleRefresh(this);
    }
    public void Refresh()
    {
        bool changed = false;
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            if (!Install(_pending[i])) continue;
            _pending.RemoveAt(i); changed = true;
        }
        if (changed)
            foreach (var consumer in _consumers.Keys)
                if (GodotObject.IsInstanceValid(consumer) && consumer.IsInsideTree()) consumer.QueueRedraw();
    }
    // A binding leaves the pending set once installed or failed; ready leases never need polling.
    private bool Install(Binding binding)
    {
        if (prefetch)
        {
            if (!binding.Lease.IsComplete) return false;
            binding.Error = binding.Lease.Error;
            if (binding.Error is not null) _failed.Add(binding);
            binding.Installed = true; _remaining--; binding.Lease.Dispose();
            return true;
        }
        if (binding.Lease.IsReady)
        {
            binding.View.Atlas = service.Cache.Get(binding.Asset.File);
            binding.Installed = true; _remaining--; Revision++;
            return true;
        }
        if (binding.Lease.Error is not { } error) return false;
        binding.Error = error; _failed.Add(binding);
        GD.PushError($"Texture {binding.Asset.Id}: {error.Message}");
        return true;
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
        _pending.Clear(); _failed.Clear();
    }
}
