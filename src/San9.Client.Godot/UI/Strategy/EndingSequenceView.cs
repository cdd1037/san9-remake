using Godot;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Rendering;

namespace San9.Client.Godot;

/// <summary>One retained ending stage over a continuous map scene, followed by the succession scroll.</summary>
public partial class EndingSequenceView : FixedPage
{
    protected override Color PageBackground => Colors.Black;
    public EndingPresentationDefinition Definition { get; init; } = null!;
    public IReadOnlyList<(string Role, CommandMessage Message)> Messages { get; init; } = [];
    public bool Expansion { get; init; }
    public TextureScope Textures { get; init; } = null!;
    public TextureScope UiTextures { get; init; } = null!;
    public Func<Settings.MessageSpeed> ReadMessageSpeed { get; init; } = () => Settings.MessageSpeed.Normal;
    public Action Closed { get; init; } = null!;
    private GamePagedText _text = null!;
    private EndingSceneAnimation _animation = null!;
    private int _index, _completedStages;
    private double _mapTime, _readTime;
    private bool _mapStarted, _leaving, _finished;
    private string[] _assets = [];
    private EndingCue Cue => Definition.Cues[Messages[_index].Role];
    private EndingMapPresentation Map => Expansion ? Definition.Expansion : Definition.Domestic;
    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        _assets = Definition.SceneAssets.ToArray();
        Textures.Request(_assets);
        _text = new() { Textures = UiTextures, Position = new(Definition.TextX, Definition.TextY) };
        AddChild(_text); Present();
    }
    private void Present()
    {
        var cue = Cue;
        bool fade = cue.Scene == EndingSceneKind.Map && !_mapStarted;
        if (cue.Scene == EndingSceneKind.Map) _mapStarted = true;
        if (cue.Scene != EndingSceneKind.Inset) _completedStages = Math.Max(_completedStages, cue.CompletedStages);
        bool stable = cue.Scene == EndingSceneKind.StableSuccession;
        bool succession = stable || cue.Scene == EndingSceneKind.UnstableSuccession;
        _animation = new(fade ? Definition.MapFadeSeconds : 0, succession ? Definition.SuccessionScrollSeconds : 0,
            succession ? stable ? Definition.StableHoldSeconds : Definition.UnstableHoldSeconds : 0);
        _leaving = false; _readTime = 0;
        _text.Present(Messages[_index].Message.Message.BbCode, new(24, Colors.White, GameTextEffect.None, Colors.Transparent));
        _text.Paginate(Definition.TextWidth);
        _text.Visible = _animation.Reading && Textures.AreReady(_assets);
        QueueRedraw();
    }
    private void Next()
    {
        if (_finished) return;
        if (!Textures.AreReady(_assets)) return;
        if (!_animation.Reading && !_leaving) { _animation.SkipEntrance(); _text.Show(); _readTime = 0; QueueRedraw(); return; }
        if (_leaving) { _animation.SkipExit(); return; }
        _readTime = 0;
        if (_text.Advance()) return;
        _completedStages = Math.Max(_completedStages, Cue.CompletedStages);
        _text.Hide(); _leaving = true; _animation.Leave();
    }
    public override void _Process(double delta)
    {
        if (!Textures.AreReady(_assets)) return;
        if (_mapStarted && _animation.Reading && Cue.Scene is EndingSceneKind.Map or EndingSceneKind.Inset) _mapTime += delta;
        bool reading = _animation.Reading;
        _animation.Advance(delta);
        _text.Visible = _animation.Reading && !_leaving;
        if (_animation.Complete)
        {
            if (_index + 1 == Messages.Count) { _finished = true; SetProcess(false); Closed(); return; }
            _index++;
            Present(); return;
        }
        if (reading && !_leaving && ReadMessageSpeed() != Settings.MessageSpeed.Pause)
        {
            _readTime += delta;
            double duration = Math.Max(1, _text.CurrentPageCharacterCells) * Definition.TextSecondsPerCell;
            if (ReadMessageSpeed() == Settings.MessageSpeed.Fast) duration /= 2;
            if (_readTime >= duration) Next();
        }
        QueueRedraw();
    }
    private void Sprite(EndingSprite sprite) => DrawTexture(Textures.Get(sprite.Asset, this), new(sprite.X, sprite.Y), new(1, 1, 1, sprite.Opacity));
    public override void _Draw()
    {
        if (!Textures.AreReady(_assets)) return;
        var cue = Cue;
        if (cue.Scene is EndingSceneKind.Map or EndingSceneKind.Inset)
        {
            DrawTexture(Textures.Get(Map.Background, this), Vector2.Zero, new(1, 1, 1, (float)_animation.Opacity));
            if (_animation.Reading || _leaving)
            {
                foreach (var layer in Map.Scenery)
                {
                    int x = (int)EndingSceneAnimation.ScrollX(layer, _mapTime, Definition.ScrollPixelsPerSecond);
                    Sprite(layer.Sprite with { X = x }); Sprite(layer.Sprite with { X = x + layer.RepeatOffset });
                }
                foreach (var stage in Map.Stages.Take(_completedStages)) { Sprite(stage.Frame); Sprite(stage.Picture); }
                foreach (var sprite in Map.Overlays) Sprite(sprite);
            }
            if (cue.Scene == EndingSceneKind.Inset && !_leaving) Inset(cue.Picture!);
        }
        else if (cue.Scene == EndingSceneKind.SuccessionYear) Inset(Definition.SuccessionYearPicture);
        else if (cue.Scene is EndingSceneKind.StableSuccession or EndingSceneKind.UnstableSuccession)
        {
            string asset = cue.Scene == EndingSceneKind.StableSuccession ? Definition.StablePicture : Definition.UnstablePicture;
            var texture = Textures.Get(asset, this);
            float start = 768 - texture.GetHeight();
            DrawTexture(texture, new(0, Mathf.Lerp(start, 0, (float)_animation.ScrollProgress)));
            if (!_leaving) foreach (var curtain in Definition.Curtains) Sprite(curtain);
        }
        else if (cue.Scene == EndingSceneKind.Opening) DrawTexture(Textures.Get(Definition.Opening, this), Vector2.Zero);
    }
    private void Inset(string asset)
    {
        Sprite(Definition.InsetFrame);
        DrawTexture(Textures.Get(asset, this), new(Definition.InsetFrame.X + 16, Definition.InsetFrame.Y + 16));
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
}
