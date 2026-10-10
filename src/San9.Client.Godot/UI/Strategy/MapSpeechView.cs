using Godot;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Rendering;

namespace San9.Client.Godot;

/// <summary>Native three-part map speech surface. Its message is already produced; repaint only moves it.</summary>
public partial class MapSpeechView : Control
{
    public MapSpeechMessage Message { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public InterfaceSkin Skin { get; init; } = null!;
    private AsyncPortrait _portrait = null!;
    private readonly GameTextPresenter _text = new();
    private MapSpeechPlacement _placement;
    public float FrameWidth { get; private set; }
    private float _layoutWidth;
    public bool ResourcesReady => Textures.IsReady && _portrait.IsResourceReady;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _placement = Message.Placement;
        _portrait = new() { Service = Service, Role = $"information-person-{Message.SpeakerId}",
            View = San9.Content.Images.PortraitView.Face, Position = new(52,25), Size = new(32,40), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_portrait);
        var name = new Label { Text = Message.SpeakerName, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(name);
        _text.ApplyAtGlyphCell(name, new(16,new(Skin.Colors["map-speech-text"]),GameTextEffect.Shadow,new(Skin.Colors["map-speech-name-shadow"])),new(106,23));
        var body = new GameRichTextLabel { Textures = Textures, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(body);
        body.PresentAtGlyphCell(Message.Message.BbCode,new(12,new(Skin.Colors["map-speech-text"]),GameTextEffect.None,Colors.Transparent),new(92,41));
        body.AddThemeConstantOverride("line_separation",GameTextMetrics.LineSpacing(body.GetThemeFont("normal_font"),12)+2);
        body.Size = new(body.GetContentWidth(),body.GetContentHeight());
        _layoutWidth = body.Size.X+148;
        float left = Textures.Assets[MapSpeechSkin.Role(_placement,"left")].Width;
        float right = Textures.Assets[MapSpeechSkin.Role(_placement,"right")].Width;
        float tile = Textures.Assets[MapSpeechSkin.Role(_placement,"middle")].Width;
        FrameWidth = left + right + Math.Max(0,MathF.Ceiling((_layoutWidth-left-right)/tile))*tile;
        Size = new(FrameWidth,108);
        if (Message.ForceColorCode is int color)
        {
            AddChild(new ColorRect { Position = new(95,26), Size = new(10,10), Color = new("080808"), MouseFilter = MouseFilterEnum.Ignore });
            AddChild(new ColorRect { Position = new(96,27), Size = new(8,8), Color = new(Skin.Colors[$"overview-force-{color}"]), MouseFilter = MouseFilterEnum.Ignore });
        }
    }

    public SpeechBounds Place(Vector2 anchor, IReadOnlyList<SpeechBounds> occupied, bool choose, MapSpeechPlacement? preferred = null)
    {
        if (choose) _placement = MapSpeechLayout.Choose(anchor.X,anchor.Y,_layoutWidth,preferred ?? _placement,occupied);
        var bounds = MapSpeechLayout.Bounds(anchor.X,anchor.Y,FrameWidth,_placement);
        Position = new((float)bounds.X,(float)bounds.Y);
        QueueRedraw();
        return MapSpeechLayout.HitBounds(bounds);
    }

    public bool Contains(Vector2 point) => new Rect2(Position+new Vector2(38,20),new(FrameWidth-54,48)).HasPoint(point);
    public SpeechBounds HitBounds => new(Position.X+38,Position.Y+20,FrameWidth-54,48);

    public override void _Draw()
    {
        var left = Textures[MapSpeechSkin.Role(_placement,"left")];
        var middle = Textures[MapSpeechSkin.Role(_placement,"middle")];
        var right = Textures[MapSpeechSkin.Role(_placement,"right")];
        DrawTexture(left,Vector2.Zero);
        for (float x=left.GetWidth();x<FrameWidth-right.GetWidth();x+=middle.GetWidth()) DrawTexture(middle,new(x,0));
        DrawTexture(right,new(FrameWidth-right.GetWidth(),0));
    }

    public override void _ExitTree() => _text.Dispose();
}
