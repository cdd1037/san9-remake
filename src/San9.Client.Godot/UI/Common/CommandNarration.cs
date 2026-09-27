using Godot;
using San9.Application.Presentation;
using San9.Content.Packages;
using San9.Content.Text;

namespace San9.Client.Godot;

/// <summary>A retained sequence: advancing or repainting never re-evaluates messages or gameplay.</summary>
public partial class CommandNarration : Control
{
    private const double ContinuationFrameSeconds = .1;
    private static readonly int[] PortraitFrames = [5,5,5,5,5,5,5,0,1,2,3,2,3,4];
    private static readonly Rect2 ResultFrameBounds = new(280,320,480,136);
    public IReadOnlyList<CommandMessage> Messages { get; init; } = [];
    public StrategySnapshot View { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public IReadOnlyDictionary<string,string> Sounds { get; init; } = null!;
    public string ContentRoot { get; init; } = "";
    public bool SoundEnabled { get; init; } = true;
    public Action Closed { get; init; } = null!;
    private int _index, _frame;
    private double _elapsed;
    private Control? _body;
    private TextureRect? _continuation;
    private AudioStreamPlayer _audio = null!;
    private readonly Dictionary<string,AudioStreamWav> _sounds = [];
    private readonly GameTextPresenter _text = new();
    private StyleBoxTexture _resultFrame = null!;
    private bool HasPerson => Messages[_index].SpeakerId is not null;
    public override void _Ready()
    {
        Size = new(1024,768); MouseFilter = MouseFilterEnum.Stop;
        _resultFrame = new() { Texture = Textures["dialog-frame"], TextureMarginLeft = 96, TextureMarginRight = 96,
            TextureMarginTop = 56, TextureMarginBottom = 56, AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Tile,
            AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Tile };
        _audio = new(); AddChild(_audio); Present();
    }
    private void Present()
    {
        if (_body is not null) { RemoveChild(_body); _body.QueueFree(); }
        _body = new Control { MouseFilter = MouseFilterEnum.Ignore }; AddChild(_body);
        var entry = Messages[_index];
        if (entry.SpeakerId is int id)
        {
            var person = View.MessageSpeaker(id);
            _body.AddChild(new AsyncPortrait { Service = Service, Role = $"information-person-{id}", View = San9.Content.Images.PortraitView.Bust, Position = new(216,300), Size = new(160,180), MouseFilter = MouseFilterEnum.Ignore });
            Art("portrait-frame",new(168,260));
            for (int i=0;i<7;i++) Art("middle",new(544+i*24,332));
            Art("right",new(712,332));
            if (person.OwnerId is int forceId)
            {
                Art("affiliation",new(496,292));
                NameLabel(Texts.Resolve(View.Forces.Single(f=>f.Id==forceId).NameKey) + "军",new(508,324,88,24),16);
            }
            NameLabel(Texts.Resolve(person.NameKey),new(404,316,104,32),24);
        }
        var message = new GameRichTextLabel { Textures = Textures, Position = HasPerson ? new(432,384) : new(356,360),
            Size = HasPerson ? new(376,104) : new(352,72), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _body.AddChild(message);
        message.Present(entry.Message.BbCode, new(24, Colors.Black, GameTextEffect.None, Colors.Transparent));
        if (SoundEnabled && entry.SoundRole is string role)
        {
            if (!_sounds.TryGetValue(role, out var sound))
            {
                sound = AudioStreamWav.LoadFromFile(PackageFiles.Resolve(ContentRoot, Sounds[role]))
                    ?? throw new InvalidDataException($"Cannot load sound: {role}.");
                _sounds.Add(role,sound);
            }
            _audio.Stream = sound; _audio.Play();
        }
        // Result content starts 40px below the frame top; two 24px lines end there +48.
        // The source continuation anchor is 46px above that content bottom.
        _continuation = new TextureRect { Position = HasPerson ? new(632,452) :
            new(ResultFrameBounds.End.X - 84, ResultFrameBounds.Position.Y + 40 + 48 - 46), MouseFilter = MouseFilterEnum.Ignore };
        _body.AddChild(_continuation);
        _frame = 0; _elapsed = 0; UpdateContinuation(); QueueRedraw();
    }
    private void Art(string role, Vector2 position) => _body!.AddChild(new TextureRect { Texture = Textures["narration-"+role], Position = position, MouseFilter = MouseFilterEnum.Ignore });
    private void NameLabel(string value, Rect2 rect, int size)
    {
        var label = new Label { Text = value, Position = rect.Position, Size = rect.Size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        _body!.AddChild(label); _text.Apply(label, GameTextStyles.NarrationName with { FontSize = size });
    }
    private void Next()
    {
        if (++_index == Messages.Count) { _index--; Closed(); return; }
        Present();
    }
    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left or MouseButton.Right }) Next();
        AcceptEvent();
    }
    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Enter or Key.Space or Key.Escape })
        { GetViewport().SetInputAsHandled(); Next(); }
    }
    public override void _Process(double delta)
    {
        _elapsed += delta;
        int frame = (int)(_elapsed / ContinuationFrameSeconds);
        if (frame != _frame) { _frame = frame; UpdateContinuation(); }
    }
    public override void _Draw()
    {
        if (!HasPerson) DrawStyleBox(_resultFrame,ResultFrameBounds);
    }
    private void UpdateContinuation()
    {
        string role = HasPerson ? $"narration-portrait-next-{PortraitFrames[_frame%PortraitFrames.Length]}" : $"narration-result-next-{_frame%7}";
        if (_continuation is not null) _continuation.Texture = Textures[role];
    }
    public override void _ExitTree()
    {
        _audio.Stop(); _audio.Stream = null;
        foreach (var sound in _sounds.Values) sound.Dispose();
        _resultFrame.Dispose(); _text.Dispose();
    }
}
