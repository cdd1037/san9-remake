using Godot;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Text;
using San9.Rendering;

namespace San9.Client.Godot;

/// <summary>Retains the hall and its actors across dialogue, choices and counteroffers.</summary>
public partial class DiplomacyScene : Control
{
    public DiplomacyPresentationDefinition Definition { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public StrategySnapshot View { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    private TextureScope _textures = null!;
    private readonly GameTextPresenter _text = new();
    private readonly Dictionary<DiplomacyActorRole, (DiplomacyActorDefinition Definition, DiplomacyAnimation Animation)> _actors = [];
    private DiplomacyMeeting? _meeting;
    private bool _greeted;
    private bool _farewell;
    private double _ambientTime;
    private Action? _finished;
    private Label? _host, _envoy;
    public bool IsReady => _textures.IsReady && _actors.Values.All(a => a.Animation.IsComplete);
    public bool Active => _meeting is not null;
    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Ignore;
        var tint = Color.FromHtml(Definition.TintColor); tint.A = Definition.TintOpacity;
        AddChild(new ViewportFill { Color = tint });
        _textures = Service.CreateScope(Service.LibraryAssets);
        _textures.Request(Definition.Assets);
        _textures.BindRedraw(this);
        Visible = false;
    }
    public void Present(CommandMessage message)
    {
        if (message.Diplomacy is not { } meeting) return;
        if (_meeting != meeting)
        {
            _meeting = meeting; _greeted = false; _farewell = false; _ambientTime = 0;
            _actors.Clear();
            var world = View.World;
            var hostForce = world.Forces.Single(f => f.Id == meeting.HostForceId);
            var host = world.Officers.Single(p => p.Id == hostForce.LeaderId);
            var envoy = world.Officers.Single(p => p.Id == meeting.EnvoyId);
            AddActor(DiplomacyActorRole.Ruler, host.IsFemale);
            if (hostForce.AdviserId is int adviser) AddActor(DiplomacyActorRole.Adviser, world.Officers.Single(p => p.Id == adviser).IsFemale);
            AddActor(DiplomacyActorRole.Envoy, envoy.IsFemale, world.Forces.Any(f => f.LeaderId == envoy.Id));
            AddActor(DiplomacyActorRole.LeftGuard); AddActor(DiplomacyActorRole.RightGuard);
            string Name(int id) => Texts.Resolve(View.MessageSpeaker(id).NameKey);
            string Force(int id) => Texts.Resolve(View.Forces.Single(f => f.Id == id).NameKey);
            int? hostPlace = View.Officers.Single(p => p.Id == host.Id).ServicePlaceId;
            _host ??= Title(32); _envoy ??= Title(64);
            _host.Text = string.Format(System.Globalization.CultureInfo.InvariantCulture, Definition.HostTitle, Force(hostForce.Id),
                hostPlace is int place ? View.Settlements.Single(p => p.Id == place).ResolveName(Texts) : "");
            _envoy.Text = string.Format(System.Globalization.CultureInfo.InvariantCulture, Definition.EnvoyTitle, Name(envoy.Id), Force(envoy.OwnerId!.Value));
            Visible = true;
        }
        _farewell |= message.DiplomacyFarewell;
        if (message.SpeakerId is int speaker)
        {
            var host = View.World.Forces.Single(f => f.Id == meeting.HostForceId);
            // A ruler can also be their own adviser; the ruler's actor keeps that identity.
            var role = speaker == meeting.EnvoyId ? DiplomacyActorRole.Envoy : speaker == host.LeaderId ? DiplomacyActorRole.Ruler :
                speaker == host.AdviserId ? DiplomacyActorRole.Adviser : (DiplomacyActorRole?)null;
            if (role is { } actor)
            {
                var gesture = actor == DiplomacyActorRole.Envoy && !_greeted ? DiplomacyGesture.Salute : DiplomacyGesture.Bow;
                if (actor == DiplomacyActorRole.Envoy) _greeted = true;
                Play(actor, gesture);
            }
        }
        QueueRedraw();
    }
    private Label Title(int y)
    {
        var label = new Label { Position = new(0, y), Size = new(1024, 32), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(label); _text.Apply(label, GameTextStyles.NarrationName with { FontSize = 24 }); return label;
    }
    private void AddActor(DiplomacyActorRole role, bool female = false, bool ruler = false) => _actors.Add(role,
        (Definition.Actors.Single(a => a.Role == role && a.Female == female && a.RulerEnvoy == ruler), new()));
    private void Play(DiplomacyActorRole role, DiplomacyGesture gesture)
    {
        var actor = _actors[role];
        var sprite = gesture == DiplomacyGesture.Salute ? actor.Definition.Salute : actor.Definition.Bow;
        actor.Animation.Play(gesture, sprite.Columns, sprite.FrameMilliseconds);
    }
    public void SkipAnimation()
    {
        foreach (var actor in _actors.Values) actor.Animation.Skip();
        QueueRedraw();
    }
    public void Finish(Action completed)
    {
        if (!Active) { completed(); return; }
        SkipAnimation();
        if (_farewell)
        {
            Play(DiplomacyActorRole.LeftGuard, DiplomacyGesture.Salute);
            Play(DiplomacyActorRole.RightGuard, DiplomacyGesture.Salute);
        }
        _finished = completed;
    }
    public override void _Process(double delta)
    {
        if (!Active || !_textures.IsReady) return;
        _ambientTime += Math.Max(0, delta);
        foreach (var actor in _actors.Values) actor.Animation.Advance(delta);
        QueueRedraw();
        if (_finished is { } finished && IsReady)
        {
            _finished = null; _meeting = null; Visible = false; finished();
        }
    }
    public override void _Draw()
    {
        if (!Active) return;
        if (!_textures.IsReady) return;
        DrawLayer(Definition.Floor, 0); DrawLayer(Definition.Furniture, 0);
        float width = Math.Max(_host!.GetMinimumSize().X, _envoy!.GetMinimumSize().X);
        var titleMiddle = _textures.Get(Definition.TitleMiddle, this);
        int tileWidth = titleMiddle.GetWidth();
        int tiles = Math.Max(1, (int)Math.Ceiling(width / tileWidth));
        int left = 512 - tiles * tileWidth / 2;
        var titleLeft = _textures.Get(Definition.TitleLeft, this);
        DrawTexture(titleLeft, new(left - titleLeft.GetWidth(), 0));
        for (int i = 0; i < tiles; i++) DrawTexture(titleMiddle, new(left + i * tileWidth, 0));
        DrawTexture(_textures.Get(Definition.TitleRight, this), new(left + tiles * tileWidth, 0));
        List<(DiplomacyLayer Layer, int Frame)> layers = Definition.Ambience.Select(l =>
            (l, (int)(_ambientTime * 1000 / l.Sprite.FrameMilliseconds) % l.Sprite.Columns)).ToList();
        foreach (var (definition, animation) in _actors.Values)
        {
            var body = animation.Gesture switch { DiplomacyGesture.Salute => definition.Salute, DiplomacyGesture.Bow => definition.Bow, _ => definition.Rest.Sprite };
            var shadow = animation.Gesture switch { DiplomacyGesture.Salute => definition.SaluteShadow, DiplomacyGesture.Bow => definition.BowShadow, _ => definition.Shadow.Sprite };
            layers.Add((definition.Rest with { Sprite = body }, animation.Frame));
            layers.Add((definition.Shadow with { Sprite = shadow }, animation.Frame));
        }
        foreach (var (layer, frame) in layers.OrderBy(l => l.Layer.Depth)) DrawLayer(layer, frame);
    }
    private void DrawLayer(DiplomacyLayer layer, int frame)
    {
        var texture = _textures.Get(layer.Sprite.Asset, this);
        var size = new Vector2(texture.GetWidth() / layer.Sprite.Columns, texture.GetHeight());
        DrawTextureRectRegion(texture, new(new Vector2(layer.X, layer.Y), size), new(new Vector2(frame * size.X, 0), size));
    }
    public override void _Input(InputEvent input)
    {
        if (_finished is null || !IsVisibleInTree()) return;
        if (input is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left or MouseButton.Right } ||
            input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Enter or Key.Space or Key.Escape })
        { SkipAnimation(); GetViewport().SetInputAsHandled(); }
    }
    public override void _ExitTree() { _finished = null; _textures.Dispose(); _text.Dispose(); }
}
