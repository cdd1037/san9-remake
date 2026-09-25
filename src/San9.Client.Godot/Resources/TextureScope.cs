using System.Collections;
using Godot;
using San9.Content.Images;
using San9.Rendering;

namespace San9.Client.Godot;

public sealed class TextureScope(TextureService service, IReadOnlyDictionary<string, ImageAsset> assets, bool prefetch = false)
    : IReadOnlyDictionary<string, Texture2D>, IDisposable
{
    private sealed record Binding(ImageAsset Asset, AtlasTexture View, TextureCache<ImageTexture>.Lease Lease)
    { public bool Installed; public bool Reported; }
    private readonly Dictionary<string, Binding> _bindings = [];
    private bool _disposed;
    public event Action? Changed;
    public TextureService Service => service;
    public bool IsReady => _bindings.Values.All(b => b.Installed);
    public Exception? Error => _bindings.Values.Select(b => b.Lease.Error).FirstOrDefault(e => e is not null);
    public Texture2D this[string key]
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_bindings.TryGetValue(key, out var existing)) return existing.View;
            var asset = assets[key];
            // Region preserves layout dimensions; an unbound atlas draws nothing while loading.
            var view = new AtlasTexture { Region = new(asset.X, asset.Y, asset.Width, asset.Height), FilterClip = true };
            var binding = new Binding(asset, view, service.Cache.Acquire([asset.File], prefetch));
            _bindings.Add(key, binding); Refresh(binding); return view;
        }
    }
    public void Request(IEnumerable<string> keys) { foreach (string key in keys) _ = this[key]; }
    public void Refresh()
    {
        bool changed = false;
        foreach (var binding in _bindings.Values) changed |= Refresh(binding);
        if (changed) Changed?.Invoke();
    }
    private bool Refresh(Binding binding)
    {
        if (prefetch && !binding.Installed && binding.Lease.IsComplete)
        { binding.Installed = true; binding.Lease.Dispose(); return false; }
        bool changed = false;
        if (!binding.Installed && binding.Lease.IsReady)
        { binding.View.Atlas = service.Cache.Get(binding.Asset.File); binding.Installed = true; changed = true; }
        if (!binding.Reported && binding.Lease.Error is { } error)
        { GD.PushError($"Texture {binding.Asset.Id}: {error.Message}"); binding.Reported = true; changed = true; }
        return changed;
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; Changed = null;
        service.Retire(this, () =>
        {
            foreach (var b in _bindings.Values)
            { b.View.Atlas = null; b.View.Dispose(); b.Lease.Dispose(); }
            _bindings.Clear();
        });
    }
    public IEnumerable<string> Keys => assets.Keys;
    public IEnumerable<Texture2D> Values => Keys.Select(k => this[k]);
    public int Count => assets.Count;
    public bool ContainsKey(string key) => assets.ContainsKey(key);
    public bool TryGetValue(string key, out Texture2D value) { if (!ContainsKey(key)) { value = null!; return false; } value = this[key]; return true; }
    public IEnumerator<KeyValuePair<string, Texture2D>> GetEnumerator() => Keys.Select(k => new KeyValuePair<string, Texture2D>(k, this[k])).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
