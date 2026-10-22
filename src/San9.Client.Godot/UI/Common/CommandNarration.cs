using Godot;
using San9.Application.Presentation;
using San9.Content.Text;
using San9.Rendering;

namespace San9.Client.Godot;

/// <summary>A retained sequence: advancing or repainting never re-evaluates messages or gameplay.</summary>
public partial class CommandNarration : FixedPage
{
    private const double ContinuationFrameSeconds = .1;
    private static readonly int[] PortraitFrames = [5,5,5,5,5,5,5,0,1,2,3,2,3,4];
    private Rect2 _resultFrameBounds;
    private Rect2? _imageFrameBounds;
    private TextureScope? _illustration;
    private AsyncPortrait? _portrait;
    public IReadOnlyList<CommandMessage> Messages { get; init; } = [];
    public StrategySnapshot View { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public San9.Content.Interface.DiplomacyPresentationDefinition? DiplomacyPresentation { get; init; }
    public DiplomacyScene? MeetingScene { get; init; }
    public bool RetainMeeting { get; init; }
    public Action? MeetingClosed { get; init; }
    private DiplomacyScene? _ownedMeeting;
    private DiplomacyScene? Meeting => MeetingScene ?? _ownedMeeting;
    private bool _leavingMeeting;
    public IReadOnlyDictionary<string,string> Sounds { get; init; } = null!;
    public string ContentRoot { get; init; } = "";
    public bool SoundEnabled { get; init; } = true;
    public Func<Settings.MessageSpeed> ReadMessageSpeed { get; init; } = () => Settings.MessageSpeed.Normal;
    public double IllustrationFadeSeconds { get; init; }
    private TextureRect? _image;
    private ShaderMaterial? _deceasedMaterial;
    private double _imageElapsed;
    private readonly Settings.MessagePageTimer _pageTimer = new();
    public string? InitialSoundRole { get; init; }
    public Func<string,bool,IDisposable>? BeginMusic { get; init; }
    private IDisposable? _music;
    private string? _musicRole;
    private bool _musicLoop;
    public Action Closed { get; init; } = null!;
    public Action<CommandMessage>? MessagePresented { get; init; }
    public Func<MapSpeechMessage, bool>? IsMapSpeechComplete { get; init; }
    public Action<MapSpeechMessage>? DismissMapSpeech { get; init; }
    public ISceneMedia Media { get; init; } = new SilentSceneMedia();
    public Func<bool> MoviesEnabled { get; init; } = () => true;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _moviePending;
    private bool IsMapSpeech => Messages[_index] is { WaitForMapSpeech: true, MapSpeech: not null };
    private int _index, _frame;
    private double _elapsed;
    private Control? _body;
    private GamePagedText _message = null!;
    private TextureRect? _continuation;
    private AudioStreamPlayer _audio = null!;
    private AudioStreams _streams = null!;
    private readonly GameTextPresenter _text = new();
    private StyleBoxTexture _resultFrame = null!;
    private bool HasIllustration => Messages[_index].IllustrationRole is not null || Messages[_index].IllustrationPersonId is not null;
    private bool HasPerson => !HasIllustration && Messages[_index].SpeakerId is not null;
    public override void _Ready()
    {
        Size = new(1024,768); MouseFilter = MouseFilterEnum.Stop;
        _resultFrame = new() { Texture = Textures.Get("dialog-frame", this), TextureMarginLeft = 96, TextureMarginRight = 96,
            TextureMarginTop = 56, TextureMarginBottom = 56, AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Tile,
            AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Tile };
        _streams = new(ContentRoot, Sounds);
        _audio = new(); AddChild(_audio);
        if (InitialSoundRole is string sound) PlaySound(sound);
        Present();
    }
    private void Present()
    {
        if (!RetainMeeting && Messages[_index].Diplomacy is null && Meeting is { Active: true } active)
        {
            _leavingMeeting = true;
            if (_body is not null) _body.Visible = false;
            active.Finish(() => { _leavingMeeting = false; MeetingClosed?.Invoke(); Present(); });
            QueueRedraw(); return;
        }
        if (_body is not null) { RemoveChild(_body); _body.QueueFree(); }
        _illustration?.Dispose(); _illustration=null; _imageFrameBounds=null;
        _portrait = null;
        _image = null; _imageElapsed = 0;
        var entry = Messages[_index];
        // Diplomacy anchors the shared portrait frame at y=480 rather than the ordinary y=260.
        // Move its complete body so the portrait, labels, text and continuation keep one origin.
        _body = new Control { MouseFilter = MouseFilterEnum.Ignore,
            Position = HasPerson && entry.Diplomacy is not null ? new(0, 480 - 260) : Vector2.Zero };
        AddChild(_body);
        if (entry.Diplomacy is not null)
        {
            if (Meeting is null)
            {
                _ownedMeeting = new() { Definition = DiplomacyPresentation ?? throw new InvalidDataException("Missing diplomacy presentation."),
                    Service = Service, View = View, Texts = Texts, ShowBehindParent = true };
                AddChild(_ownedMeeting);
            }
            Meeting!.Present(entry);
            _body.Visible = Meeting.IsReady;
        }
        if (_musicRole != entry.BackgroundMusicRole || _musicLoop != entry.BackgroundMusicLoop)
        {
            _music?.Dispose(); _music = null; _musicRole = entry.BackgroundMusicRole; _musicLoop = entry.BackgroundMusicLoop;
            if (_musicRole is string music) _music = BeginMusic?.Invoke(music, _musicLoop);
        }
        MessagePresented?.Invoke(entry);
        if (IsMapSpeech)
        {
            _continuation = null;
            if (entry.SoundRole is string speechSound) PlaySound(speechSound);
            QueueRedraw();
            return;
        }
        if (!HasIllustration && entry.SpeakerId is int id)
        {
            var person = View.MessageSpeaker(id);
            _portrait = new AsyncPortrait { Service = Service, Role = $"information-person-{id}", View = San9.Content.Images.PortraitView.Bust, Position = new(216,300), Size = new(160,180), MouseFilter = MouseFilterEnum.Ignore };
            _body.AddChild(_portrait);
            Art("portrait-frame",new(168,260));
            for (int i=0;i<7;i++) Art("middle",new(544+i*24,332));
            Art("right",new(712,332));
            string? forceName = entry.Speaker is { } captured ? captured.ForceName : person.OwnerId is int forceId
                ? Texts.Resolve(View.Forces.Single(f=>f.Id==forceId).NameKey) : null;
            if (forceName is not null)
            {
                Art("affiliation",new(496,292));
                NameLabel(forceName + "军",new(508,324,88,24),16);
            }
            NameLabel(entry.Speaker?.Name ?? Texts.Resolve(person.NameKey),new(404,316,104,32),24);
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
            _illustration = entry.IllustrationPersonId is not null ? Service.CreatePortraitScope(imageRole,San9.Content.Images.PortraitView.Full) :
                Service.CreateScope(new Dictionary<string,San9.Content.Images.ImageAsset> { [imageRole]=Service.Roles[imageRole] });
            _illustration.Request([imageRole]);
            var asset=_illustration.Assets[imageRole];
            float scale=Math.Min(1,Math.Min(928f/asset.Width,(592-resultBodyHeight)/asset.Height));
            var imageSize=new Vector2(asset.Width,asset.Height)*scale;
            float top=(768-imageSize.Y-64-(resultBodyHeight+112))/2;
            _imageFrameBounds=new((1024-imageSize.X-64)/2,top,imageSize.X+64,imageSize.Y+64);
            _image = new TextureRect { Texture=_illustration.Get(imageRole, this),Position=_imageFrameBounds.Value.Position+new Vector2(32,32),Size=imageSize,
                ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.Scale,MouseFilter=MouseFilterEnum.Ignore,
                Modulate = new Color(1, 1, 1, SceneOpacity.Darken(0, IllustrationFadeSeconds)) };
            if(entry.IllustrationDeceased) _image.Material=_deceasedMaterial ??= PortraitMaterials.Deceased();
            _body.AddChild(_image);
            float dy=(imageSize.Y+96)/2;
            _resultFrameBounds=new(_resultFrameBounds.Position+new Vector2(0,dy),_resultFrameBounds.Size);
            _message.Position+=new Vector2(0,dy);
        }
        if (entry.SoundRole is string role) PlaySound(role);
        // Result content has 24px logical lines; the source anchor is 46px above its bottom.
        _continuation = new TextureRect { Position = HasPerson ? new(632,452) :
            new(_message.Position.X + _message.Size.X + 8, _message.Position.Y + resultBodyHeight - 46), MouseFilter = MouseFilterEnum.Ignore };
        _body.AddChild(_continuation);
        _frame = 0; _elapsed = 0; _pageTimer.Reset(); UpdateContinuation(); QueueRedraw();
    }
    private void PlaySound(string role)
    {
        if (!SoundEnabled) return;
        _audio.Stream = _streams.Get(role, loop: false);
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
    private void Art(string role, Vector2 position) => _body!.AddChild(new TextureRect { Texture = Textures.Get("narration-"+role, this), Position = position, MouseFilter = MouseFilterEnum.Ignore });
    private void NameLabel(string value, Rect2 rect, int size)
    {
        var label = new Label { Text = value, Position = rect.Position, Size = rect.Size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        _body!.AddChild(label); _text.Apply(label, GameTextStyles.NarrationName with { FontSize = size });
    }
    private async void Next()
    {
        if (_moviePending) return;
        if (_leavingMeeting) { Meeting?.SkipAnimation(); return; }
        if (Meeting is { Active: true, IsReady: false } meeting)
        { meeting.SkipAnimation(); return; }
        if(_illustration is { IsReady:false }) return;
        _pageTimer.Reset();
        if (IsMapSpeech) DismissMapSpeech?.Invoke(Messages[_index].MapSpeech!);
        else if (_message.Advance())
        {
            _frame = 0; _elapsed = 0; UpdateContinuation();
            return;
        }
        if (MoviesEnabled() && Messages[_index].MovieRole is string movie)
        {
            _moviePending = true;
            try { await Media.PlayMovieAsync(new(movie), _lifetime.Token); }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }
            finally { _moviePending = false; }
            if (_lifetime.IsCancellationRequested) return;
        }
        if (!RetainMeeting && Meeting is { Active: true } active &&
            (_index + 1 == Messages.Count || Messages[_index + 1].Diplomacy != Messages[_index].Diplomacy))
        {
            _leavingMeeting = true; _body!.Visible = false; QueueRedraw();
            active.Finish(() => { _leavingMeeting = false; MeetingClosed?.Invoke(); AdvanceMessage(); });
        }
        else AdvanceMessage();
    }
    private void AdvanceMessage()
    {
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
        if (_leavingMeeting) return;
        if (Messages[_index].Diplomacy is not null && Meeting is { } meeting)
        {
            if (_body is not null) _body.Visible = meeting.IsReady;
            QueueRedraw();
            if (!meeting.IsReady) return;
        }
        if (IsMapSpeech)
        {
            if (IsMapSpeechComplete?.Invoke(Messages[_index].MapSpeech!) == true) Next();
            return;
        }
        if (_image is not null && _illustration?.IsReady == true)
        {
            _imageElapsed += delta;
            _image.Modulate = new Color(1, 1, 1, SceneOpacity.Darken(_imageElapsed, IllustrationFadeSeconds));
        }
        _elapsed += delta;
        int frame = (int)(_elapsed / ContinuationFrameSeconds);
        if (frame != _frame) { _frame = frame; UpdateContinuation(); }
        if (_pageTimer.Advance(delta, ReadMessageSpeed(), IsVisibleInTree() && Textures.IsReady &&
            _illustration is not { IsReady: false } && _portrait is not { IsResourceReady: false } && (_musicRole is null || _musicLoop))) Next();
    }
    public override void _Draw()
    {
        if (IsMapSpeech || _leavingMeeting || _body is { Visible: false }) return;
        if (!HasPerson) DrawStyleBox(_resultFrame,_resultFrameBounds);
        if (_imageFrameBounds is { } image) DrawImageFrame(image);
    }
    private void DrawImageFrame(Rect2 bounds)
    {
        Texture2D Part(string part) => Textures.Get("narration-image-frame-"+part, this);
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
        if (_continuation is not null) _continuation.Texture = Textures.Get(role, this);
    }
    public override void _ExitTree()
    {
        _lifetime.Cancel(); _lifetime.Dispose();
        _music?.Dispose(); _music = null;
        _audio.Stop(); _audio.Stream = null;
        _streams.Dispose();
        _resultFrame.Dispose(); _text.Dispose();
        _illustration?.Dispose();
        if(_deceasedMaterial is not null) { _deceasedMaterial.Shader.Dispose(); _deceasedMaterial.Dispose(); }
    }
}
