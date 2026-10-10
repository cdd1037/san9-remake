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
    private Rect2 _resultFrameBounds;
    private Rect2? _imageFrameBounds;
    private TextureScope? _illustration;
    public IReadOnlyList<CommandMessage> Messages { get; init; } = [];
    public StrategySnapshot View { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public IReadOnlyDictionary<string,string> Sounds { get; init; } = null!;
    public string ContentRoot { get; init; } = "";
    public bool SoundEnabled { get; init; } = true;
    public string? InitialSoundRole { get; init; }
    public Func<string,IDisposable>? BeginMusic { get; init; }
    private IDisposable? _music;
    private string? _musicRole;
    public Action Closed { get; init; } = null!;
    public Action<CommandMessage>? MessagePresented { get; init; }
    private int _index, _frame;
    private double _elapsed;
    private Control? _body;
    private GamePagedText _message = null!;
    private TextureRect? _continuation;
    private AudioStreamPlayer _audio = null!;
    private readonly Dictionary<string,AudioStreamWav> _sounds = [];
    private readonly GameTextPresenter _text = new();
    private StyleBoxTexture _resultFrame = null!;
    private bool HasIllustration => Messages[_index].IllustrationRole is not null || Messages[_index].IllustrationPersonId is not null;
    private bool HasPerson => !HasIllustration && Messages[_index].SpeakerId is not null;
    public override void _Ready()
    {
        Size = new(1024,768); MouseFilter = MouseFilterEnum.Stop;
        _resultFrame = new() { Texture = Textures["dialog-frame"], TextureMarginLeft = 96, TextureMarginRight = 96,
            TextureMarginTop = 56, TextureMarginBottom = 56, AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Tile,
            AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Tile };
        _audio = new(); AddChild(_audio);
        if (InitialSoundRole is string sound) PlaySound(sound);
        Present();
    }
    private void Present()
    {
        if (_body is not null) { RemoveChild(_body); _body.QueueFree(); }
        _illustration?.Dispose(); _illustration=null; _imageFrameBounds=null;
        _body = new Control { MouseFilter = MouseFilterEnum.Ignore }; AddChild(_body);
        var entry = Messages[_index];
        if (_musicRole != entry.BackgroundMusicRole)
        {
            _music?.Dispose(); _music = null; _musicRole = entry.BackgroundMusicRole;
            if (SoundEnabled && _musicRole is string music) _music = BeginMusic?.Invoke(music);
        }
        MessagePresented?.Invoke(entry);
        if (!HasIllustration && entry.SpeakerId is int id)
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
        _message = new GamePagedText { Textures = Textures };
        _body.AddChild(_message);
        _message.Present(entry.Message.BbCode, new(24, Colors.Black, GameTextEffect.None, Colors.Transparent));
        float resultBodyHeight = 0;
        if (HasPerson)
        {
            _message.Paginate(376);
            _message.Position = new(432,384);
        }
        else resultBodyHeight = LayoutResult(_message);
        if (HasIllustration)
        {
            string imageRole = entry.IllustrationPersonId is int imagePerson ? $"information-person-{imagePerson}" : entry.IllustrationRole!;
            _illustration = entry.IllustrationPersonId is not null ? Service.CreatePortraitScope(imageRole,San9.Content.Images.PortraitView.Full,this) :
                Service.CreateScope(new Dictionary<string,San9.Content.Images.ImageAsset> { [imageRole]=Service.Roles[imageRole] },owner:this);
            _illustration.Request([imageRole]);
            var asset=_illustration.Assets[imageRole];
            float scale=Math.Min(1,Math.Min(928f/asset.Width,(592-resultBodyHeight)/asset.Height));
            var imageSize=new Vector2(asset.Width,asset.Height)*scale;
            float top=(768-imageSize.Y-64-(resultBodyHeight+112))/2;
            _imageFrameBounds=new((1024-imageSize.X-64)/2,top,imageSize.X+64,imageSize.Y+64);
            _body.AddChild(new TextureRect { Texture=_illustration[imageRole],Position=_imageFrameBounds.Value.Position+new Vector2(32,32),Size=imageSize,
                ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.Scale,MouseFilter=MouseFilterEnum.Ignore });
            float dy=(imageSize.Y+96)/2;
            _resultFrameBounds=new(_resultFrameBounds.Position+new Vector2(0,dy),_resultFrameBounds.Size);
            _message.Position+=new Vector2(0,dy);
        }
        if (entry.SoundRole is string role) PlaySound(role);
        // Result content has 24px logical lines; the source anchor is 46px above its bottom.
        _continuation = new TextureRect { Position = HasPerson ? new(632,452) :
            new(_message.Position.X + _message.Size.X + 8, _message.Position.Y + resultBodyHeight - 46), MouseFilter = MouseFilterEnum.Ignore };
        _body.AddChild(_continuation);
        _frame = 0; _elapsed = 0; UpdateContinuation(); QueueRedraw();
    }
    private void PlaySound(string role)
    {
        if (!SoundEnabled) return;
        if (!_sounds.TryGetValue(role, out var sound))
        {
            sound = AudioStreamWav.LoadFromFile(PackageFiles.Resolve(ContentRoot, Sounds[role]))
                ?? throw new InvalidDataException($"Cannot load sound: {role}.");
            _sounds.Add(role, sound);
        }
        _audio.Stream = sound;
        _audio.Play();
    }
    private float LayoutResult(GamePagedText message)
    {
        // Measure shaped rich text before wrapping, including styles and inline images.
        // Text owns the centered anchor; the decorative frame fits around it in native 24px tiles.
        float contentWidth = Math.Clamp(message.UnwrappedWidth,24,848);
        message.Paginate(contentWidth);
        float bodyHeight = Math.Max(24, message.MaximumPageLayoutHeight);
        float width = 192 + 24 * MathF.Ceiling(Math.Max(0, contentWidth - 32) / 24f);
        float height = 112 + 24 * MathF.Ceiling(Math.Max(0, bodyHeight - 32) / 24f);
        message.Position = new(512-contentWidth/2,384-bodyHeight/2);
        _resultFrameBounds = new(message.Position-new Vector2(80,40),new(width,height));
        return bodyHeight;
    }
    private void Art(string role, Vector2 position) => _body!.AddChild(new TextureRect { Texture = Textures["narration-"+role], Position = position, MouseFilter = MouseFilterEnum.Ignore });
    private void NameLabel(string value, Rect2 rect, int size)
    {
        var label = new Label { Text = value, Position = rect.Position, Size = rect.Size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        _body!.AddChild(label); _text.Apply(label, GameTextStyles.NarrationName with { FontSize = size });
    }
    private void Next()
    {
        if(_illustration is { IsReady:false }) return;
        if (_message.Advance())
        {
            _frame = 0; _elapsed = 0; UpdateContinuation();
            return;
        }
        if (++_index == Messages.Count) { _index--; Closed(); return; }
        Present();
    }
    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left }) Next();
        AcceptEvent();
    }
    public override void _Input(InputEvent input)
    {
        if (!IsVisibleInTree()) return;
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Right } mouse)
        {
            GetViewport().SetInputAsHandled();
            if (mouse.Pressed) Next();
        }
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
        if (!HasPerson) DrawStyleBox(_resultFrame,_resultFrameBounds);
        if (_imageFrameBounds is { } image) DrawImageFrame(image);
    }
    private void DrawImageFrame(Rect2 bounds)
    {
        Texture2D Part(string part) => Textures["narration-image-frame-"+part];
        foreach (var edge in new[] { "top", "bottom" })
        {
            var texture=Part(edge);
            float gap = bounds.Size.X-Part("top-left").GetWidth()-Part("top-right").GetWidth();
            int count = Math.Max(1,(int)Math.Ceiling(gap/texture.GetWidth()));
            if (count%2==0) count++;
            float x=(bounds.Size.X-count*texture.GetWidth())/2;
            for(int i=0;i<count;i++) DrawTexture(texture,bounds.Position+new Vector2(x+i*texture.GetWidth(),edge=="top" ? 0 : bounds.Size.Y-texture.GetHeight()));
        }
        foreach(var edge in new[] { "left", "right" })
        {
            var texture=Part(edge);
            float top = Part("top-"+edge).GetHeight();
            float gap = bounds.Size.Y-top-Part("bottom-"+edge).GetHeight();
            int count = Math.Max(0,(int)Math.Ceiling(gap/texture.GetHeight()));
            for(int i=0;i<count;i++) DrawTexture(texture,bounds.Position+new Vector2(edge=="left" ? 0 : bounds.Size.X-texture.GetWidth(),top+i*texture.GetHeight()));
        }
        foreach(var corner in new[] { "top-left", "top-right", "bottom-left", "bottom-right" })
        {
            var texture=Part(corner);
            DrawTexture(texture,bounds.Position+new Vector2(corner.EndsWith("right",StringComparison.Ordinal) ? bounds.Size.X-texture.GetWidth() : 0,
                corner.StartsWith("bottom",StringComparison.Ordinal) ? bounds.Size.Y-texture.GetHeight() : 0));
        }
    }
    private void UpdateContinuation()
    {
        string role = HasPerson ? $"narration-portrait-next-{PortraitFrames[_frame%PortraitFrames.Length]}" : $"narration-result-next-{_frame%7}";
        if (_continuation is not null) _continuation.Texture = Textures[role];
    }
    public override void _ExitTree()
    {
        _music?.Dispose(); _music = null;
        _audio.Stop(); _audio.Stream = null;
        foreach (var sound in _sounds.Values) sound.Dispose();
        _resultFrame.Dispose(); _text.Dispose();
        _illustration?.Dispose();
    }
}
