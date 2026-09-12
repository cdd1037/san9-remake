using Godot;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Content.Interface;
using San9.Content.Images;
using San9.Rendering;

namespace San9.Client.Godot;

public partial class MapAnnotations : Node2D
{
    public MapWorld World { get; init; } = null!;
    public StrategySelection Selection { get; init; } = null!;
    public San9.Application.GameSession Session { get; init; } = null!;
    public San9.Application.Presentation.StrategySnapshot Scenario => Session.View;
    public TextCatalog Texts { get; init; } = null!;
    public InterfaceSkin Skin { get; init; } = null!;
    public TextureService Textures { get; init; } = null!;
    public Font UiFont { get; init; } = null!;
    private TextureScope _textures = null!;
    private Dictionary<int, SettlementView> _settlements = [];
    private readonly Dictionary<int, (string Soldiers, float Width, string Name)> _labels = [];
    private long _viewRevision = -1, _stateRevision = -1;
    private int? _hovered;
    public long DrawCount { get; private set; }
    public override void _Ready()
    {
        _settlements = Scenario.Settlements.ToDictionary(s => s.Id);
        foreach (var s in Scenario.Settlements)
        {
            string soldiers = s.Soldiers.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _labels.Add(s.Id, (soldiers, UiFont.GetStringSize(soldiers, fontSize: 16).X, Texts.Resolve(s.NameKey)));
        }
        _textures = Textures.CreateScope(owner: this);
    }
    public override void _Process(double delta)
    {
        if (_hovered != World.HoveredBuildingId) { _hovered = World.HoveredBuildingId; QueueRedraw(); }
        if (_stateRevision != Session.Snapshot.Revision)
        {
            _stateRevision = Session.Snapshot.Revision;
            _settlements = Scenario.Settlements.ToDictionary(s => s.Id);
            foreach (var s in _settlements.Values)
            {
                string soldiers=s.Soldiers.ToString(System.Globalization.CultureInfo.InvariantCulture);
                _labels[s.Id]=(soldiers,UiFont.GetStringSize(soldiers,fontSize:16).X,Texts.Resolve(s.NameKey));
            }
            QueueRedraw();
        }
        if (_viewRevision == World.ViewRevision) return;
        _viewRevision = World.ViewRevision; QueueRedraw();
    }
    public override void _Draw()
    {
        DrawCount++;
        if (World.Package is not { } map || !World.HasCompleteView) return;
        foreach (var b in World.RuntimeBuildings)
        {
            if (_settlements[b.Id].IsDestroyed) continue;
            var anchor = BuildingInfoGeometry.Anchor(b.X, b.Y, b.FixedType, map.Squares[b.Y * 200 + b.X].Elevation);
            var point = World.WorldToViewport(new(anchor.X, anchor.Y));
            float zoom = World.ZoomFactor;
            if (!new Rect2(-116 * zoom, -55 * zoom, 1024 + 116 * zoom, 768 + 55 * zoom).HasPoint(point)) continue;
            DrawSetTransform(point, 0, Vector2.One * zoom);
            var s = _settlements[b.Id];
            var label = _labels[b.Id];
            Text(label.Soldiers, new(72 - label.Width, 16), b.Id == _hovered);
            Bar("durability", s.Durability, s.MaxDurability, new(0, 16));
            Bar("morale", s.Morale, 100, new(0, 29));
            Text(label.Name, new(0, 53), b.Id == _hovered);
            foreach (string icon in s.Icons) DrawTexture(_textures["icon-" + icon], icon == "emperor" ? new(96, 17) : new(75, 17));
        }
        DrawSetTransform(Vector2.Zero);
    }
    private void Bar(string kind, int current, int max, Vector2 position)
    {
        DrawTexture(_textures[$"bar-{kind}-frame"], position);
        int width = BuildingInfoGeometry.FillWidth(current, max);
        if (width == 0) return;
        var fill = _textures[$"bar-{kind}-{BuildingInfoGeometry.FillState(current, max)}"];
        DrawTextureRectRegion(fill, new(position + new Vector2(4, 4), new Vector2(width, fill.GetHeight())), new(0, 0, width, fill.GetHeight()));
    }
    private void Text(string text, Vector2 point, bool hovered) =>
        GameTextPresenter.Draw(this, UiFont, text, point, GameTextStyles.Building(hovered));
    public override void _ExitTree() { _textures.Dispose(); }
}
