using Godot;
using San9.Application.Presentation;
using San9.Content.Text;
using San9.Rendering;

namespace San9.Client.Godot;

/// <summary>The shared framed map command list, independent of popup or persistent selection ownership.</summary>
public partial class MapCommandList : Control
{
    public TextureScope Textures { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public Action<MapCommandEntry, InformationMenuButton>? Activated { get; init; }
    private readonly List<(InformationMenuButton Button, MapCommandEntry Entry)> _items = [];
    public IReadOnlyList<(InformationMenuButton Button, MapCommandEntry Entry)> Items => _items;

    public void SetEntries(IReadOnlyList<MapCommandEntry> entries)
    {
        foreach (var item in _items) { RemoveChild(item.Button); item.Button.QueueFree(); }
        _items.Clear();
        Size = new(CommandMenuGeometry.PrimaryWidth, 32 + entries.Count * 28);
        MouseFilter = MouseFilterEnum.Stop;
        foreach (var entry in entries)
        {
            var button = new InformationMenuButton
            {
                Title = entry.Id == "armament-resupply" ? "补充军械" : entry.Id == "armament-configure" ? "调整军械" : CommandText.Resolve(Texts, "title." + entry.Id), Disabled = !entry.Enabled,
                Textures = Textures, Position = new(11, 16 + _items.Count * 28), Size = new(68, 28),
                SkinPrefix = $"command-primary-{entry.Tone.ToString().ToLowerInvariant()}"
            };
            button.Pressed += () => { if (entry.Enabled) Activated?.Invoke(entry, button); };
            AddChild(button); _items.Add((button, entry));
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_items.Count == 0) return;
        DrawTexture(Textures.Get("command-top", this), Vector2.Zero);
        for (int i = 0; i < _items.Count; i++)
            DrawTexture(Textures.Get("command-middle", this), new(0, 16 + i * 28));
        DrawTexture(Textures.Get("command-bottom", this), new(0, Size.Y - 16));
    }
}
