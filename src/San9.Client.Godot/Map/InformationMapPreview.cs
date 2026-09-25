using Godot;
using San9.Content.Images;
using San9.Content.Maps;
using San9.Content.Text;
using San9.Rendering;

namespace San9.Client.Godot;

public partial class MapWorld
{
    public Control CreateInformationPreview(int regionId, TextCatalog texts)
    {
        if (_map is null || _renderer is null) throw new InvalidOperationException("Map is not ready.");
        var settlement = Session.View.Settlements.FirstOrDefault(s => s.Id == regionId);
        Vector2 center;
        if (settlement is not null)
        {
            var point = MapGeometry.Center(settlement.X, settlement.Y);
            center = new(point.X, point.Y - 48);
        }
        else
        {
            var cells = _map.Squares.Select((s,i) => (s,i)).Where(p => p.s.RegionId == regionId).Select(p => new Vector2(p.i % _map.Width,p.i / _map.Width)).ToArray();
            if (cells.Length == 0) return new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
            var average = cells.Aggregate(Vector2.Zero, (a,b) => a+b)/cells.Length;
            var cell = cells.MinBy(p => p.DistanceSquaredTo(average));
            var point = MapGeometry.Center((int)cell.X,(int)cell.Y); center = new(point.X,point.Y);
        }
        var rect = new Rect2(center-new Vector2(256,128),new(512,256));
        var buildings = _runtimeBuildings.Where(b => b.Sprites.Any(s => rect.Intersects(SpriteBounds(s)))).ToArray();
        return new InformationMapPreview {
            Renderer = _renderer, Service = Textures, Map = _map, Season = _season, Bounds = rect,
            Buildings = buildings, Animations = buildings.SelectMany(b => _buildingAnimations.GetValueOrDefault(b.Id, [])).ToArray(),
            Settlements = Session.View.Settlements.ToDictionary(s => s.Id), Texts = texts
        };
    }
}

/// <summary>Read-only local map view; owns temporary tiles and leases, never the strategic camera.</summary>
public partial class InformationMapPreview : Control
{
    public TerrainRenderer Renderer { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public MapPackage Map { get; init; } = null!;
    public string Season { get; init; } = "";
    public Rect2 Bounds { get; init; }
    public MapBuilding[] Buildings { get; init; } = [];
    public MapAnimatedSprite[] Animations { get; init; } = [];
    public IReadOnlyDictionary<int, San9.Application.Presentation.SettlementView> Settlements { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    private TextureScope _assets = null!, _ui = null!;
    private readonly List<(Vector2 Position, Task<PngImage> Work, ImageTexture? Texture)> _tiles = [];
    private readonly CancellationTokenSource _cancel = new();
    private Dictionary<string, MapAnimationClip> _clips = [];
    private MapSprite[] _bridges = [];
    private MapAnimatedSprite[] _water = [];
    private Node2D _waterLayer = null!, _objects = null!;
    private double _time;
    public override void _Ready()
    {
        ClipContents = true; MouseFilter = MouseFilterEnum.Ignore;
        _assets = Service.CreateScope(Service.MapAssets, owner:this); _ui = Service.CreateScope(owner:this);
        _clips = Map.Animations.Clips.ToDictionary(c => c.Id);
        bool Visible(string asset, int x, int y) => Bounds.Intersects(new(new Vector2(x,y),new(Service.MapAssets[asset].Width,Service.MapAssets[asset].Height)));
        _bridges = Map.Bridges.Where(s => Visible(s.AssetId,s.X,s.Y)).ToArray();
        _water = Map.Animations.Water.Where(s => Visible(Frames(s)[0],s.X,s.Y)).ToArray();
        _assets.Request(_bridges.Concat(Buildings.SelectMany(b => b.Sprites)).Select(s => s.AssetId)
            .Concat(_water.Concat(Animations).SelectMany(Frames)).Distinct());
        var token = _cancel.Token;
        for (int y = Math.Max(0,(int)Math.Floor(Bounds.Position.Y/512)); y <= Math.Min((Map.CanvasHeight-1)/512,(int)Math.Floor((Bounds.End.Y-1)/512)); y++)
        for (int x = Math.Max(0,(int)Math.Floor(Bounds.Position.X/512)); x <= Math.Min((Map.CanvasWidth-1)/512,(int)Math.Floor((Bounds.End.X-1)/512)); x++)
        {
            int column=x,row=y;
            _tiles.Add((new Vector2(x*512,y*512)-Bounds.Position,Task.Run(() => Renderer.RenderChunk(column,row,token),token),null));
        }
        _waterLayer = new Node2D { Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add } };
        _waterLayer.Draw += () => { foreach (var water in _water) DrawAnimation(_waterLayer,water); };
        AddChild(_waterLayer);
        _objects = new Node2D(); _objects.Draw += DrawObjects; AddChild(_objects);
    }
    private string[] Frames(MapAnimatedSprite s) => _clips[s.ClipId].SeasonFrames.GetValueOrDefault(Season,_clips[s.ClipId].Frames);
    public override void _Process(double delta)
    {
        _time += delta * 1000;
        for (int i=0;i<_tiles.Count && Service.CanUpload;i++)
        {
            var tile=_tiles[i];
            if(tile.Texture is null && tile.Work.IsCompletedSuccessfully)
                _tiles[i]=(tile.Position,tile.Work,Service.UploadGenerated(tile.Work.Result));
            else if(tile.Work.IsFaulted) { GD.PushError(tile.Work.Exception!.GetBaseException().Message); SetProcess(false); return; }
        }
        QueueRedraw(); _waterLayer.QueueRedraw(); _objects.QueueRedraw();
    }
    private bool IsPreviewReady => _assets.IsReady && _tiles.All(t => t.Texture is not null);
    public override void _Draw()
    {
        DrawRect(new(Vector2.Zero,Size),new Color("191b18"));
        if (!IsPreviewReady) return;
        foreach (var tile in _tiles) DrawTexture(tile.Texture!,tile.Position);
    }
    private void DrawAnimation(Node2D target, MapAnimatedSprite s)
    {
        if (!IsPreviewReady) return;
        var frames=Frames(s); var clip=_clips[s.ClipId];
        int frame=((int)(_time/Math.Max(1,clip.FrameMilliseconds))+s.Phase)%frames.Length;
        target.DrawTexture(_assets[frames[frame]],new Vector2(s.X,s.Y)-Bounds.Position);
    }
    private void DrawObjects()
    {
        if (!IsPreviewReady) return;
        foreach(var s in _bridges) _objects.DrawTexture(_assets[s.AssetId],new Vector2(s.X,s.Y)-Bounds.Position);
        foreach(var b in Buildings)
        {
            foreach(var s in b.Sprites) _objects.DrawTexture(_assets[s.AssetId],new Vector2(s.X,s.Y)-Bounds.Position);
            foreach(var s in Animations.Where(s => s.BuildingId==b.Id)) DrawAnimation(_objects,s);
            var place=Settlements[b.Id];
            var anchor=BuildingInfoGeometry.Anchor(b.X,b.Y,b.FixedType,Map.Squares[b.Y*Map.Width+b.X].Elevation);
            var p=new Vector2(anchor.X,anchor.Y)-Bounds.Position;
            var font=GetThemeFont("font");
            GameTextPresenter.Draw(_objects,font,place.Soldiers.ToString(),p+new Vector2(0,16),GameTextStyles.Building(false),HorizontalAlignment.Right,72);
            GameTextPresenter.Draw(_objects,font,place.ResolveName(Texts),p+new Vector2(0,53),GameTextStyles.Building(false));
            Bar("durability",place.Durability,place.MaxDurability,p+new Vector2(0,16));
            Bar("morale",place.Morale,100,p+new Vector2(0,29));
        }
    }
    private void Bar(string kind,int current,int maximum,Vector2 point)
    {
        _objects.DrawTexture(_ui[$"bar-{kind}-frame"],point);
        int width=BuildingInfoGeometry.FillWidth(current,maximum);
        if(width<=0)return;
        var texture=_ui[$"bar-{kind}-{BuildingInfoGeometry.FillState(current,maximum)}"];
        _objects.DrawTextureRectRegion(texture,new(point+new Vector2(4,4),new(width,texture.GetHeight())),new(0,0,width,texture.GetHeight()));
    }
    public override void _ExitTree()
    {
        _cancel.Cancel();
        foreach(var tile in _tiles)
        {
            if(tile.Texture is { } texture)Service.Defer(texture.Dispose);
            _ = tile.Work.ContinueWith(t => { _ = t.Exception; },TaskScheduler.Default);
        }
        _cancel.Dispose(); _assets.Dispose(); _ui.Dispose();
    }
}
