using Godot;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Rendering;

namespace San9.Client.Godot;

/// <summary>Samples a prepared plan; drawing has no random choices, publication, sound or gameplay side effects.</summary>
public partial class BattlePresentationView : Control
{
    public TextureService Textures { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public Func<BattleObjectContext, Vector2> Project { get; init; } = null!;
    public Action<BattleObjectContext>? FocusRequested { get; init; }
    public Action<string>? PlaySound { get; init; }
    public Action<string, Vector2>? PlayMapSound { get; init; }
    public Action? PauseRequested { get; init; }
    public Func<bool> IsPaused { get; init; } = () => false;
    public string ContentRoot { get; init; } = "";
    public IReadOnlyDictionary<string, string> Sounds { get; init; } = null!;
    public Func<bool> SoundEnabled { get; init; } = () => true;
    public bool IsZoomActive => _zoom is not null;
    public bool IsSceneActive => _zoom is not null || _entering;
    public float MapAudioGain { get; private set; } = 1;
    public float SceneAudioGain { get; private set; } = 1;
    private SceneTransitionOverlay _transition = null!;
    private bool _entering;
    private AudioStreams _streams = null!;
    private AudioStreamPlayer _ambience = null!;
    private AudioStreamPlayer _numberRoll = null!;
    private BattleNumberLayer _numberLayer = null!;
    private readonly HashSet<(string Cue, int Index)> _sounds = [];
    private string? _ambienceCue;
    private TextureScope? _assets;
    private TextureScope? _flags;
    private readonly List<TextureScope> _portraits = [];
    private TurnDayPresentation? _day;
    private readonly HashSet<string> _started = [];
    private readonly HashSet<string> _doubles = [];
    private readonly CanvasItemMaterial _additive = new() { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
    private readonly List<Sprite2D> _effects = [];
    private BattleVisualCue? _zoom;
    public void RetryResources() { _assets?.RetryFailed(); _flags?.RetryFailed(); foreach (var portrait in _portraits) portrait.RetryFailed(); }
    public bool ResourcesReady => _assets?.IsReady != false && _flags?.IsReady != false && _portraits.All(p => p.IsReady);

    public bool Prepare(TurnDayPresentation? day)
    {
        if (ReferenceEquals(_day, day))
        {
            if (_assets?.Error is { } error) throw new InvalidDataException("Battle presentation resources failed.", error);
            if (_flags?.Error is { } flagError) throw new InvalidDataException("Battle officer flag resources failed.", flagError);
            if (_portraits.Select(p => p.Error).FirstOrDefault(e => e is not null) is { } portraitError)
                throw new InvalidDataException("Battle speech portrait failed.", portraitError);
            return ResourcesReady;
        }
        _assets?.Dispose(); _assets = null; _day = day;
        _flags?.Dispose(); _flags = null;
        _started.Clear(); _sounds.Clear(); _doubles.Clear(); _zoom = null; _entering = false;
        MapAudioGain = SceneAudioGain = 1;
        _transition?.Sample(0);
        if (_ambience is not null) { _ambience.Stop(); _ambience.Stream = null; _ambienceCue = null; }
        if (_numberRoll is not null) { _numberRoll.Stop(); _numberRoll.Stream = null; }
        foreach (var portrait in _portraits) portrait.Dispose(); _portraits.Clear();
        foreach (var sprite in _effects) sprite.Hide();
        if (day?.Battle is not { } plan) { QueueRedraw(); return true; }
        HashSet<string> resources = [];
        HashSet<string> flagResources = [];
        void Sprite(BattleSprite? sprite) { if (sprite is not null) resources.Add(sprite.Asset); }
        void Body(BattleZoomBody body)
        {
            Sprite(body.Idle); Sprite(body.Attack); Sprite(body.Advance); Sprite(body.Ranged); Sprite(body.Wake);
            foreach (var effect in body.Effects.Concat(body.DamageEffects)) Sprite(effect.Sprite);
        }
        foreach (var effect in plan.MapEffects) Sprite(effect.Sprite);
        if (plan.MapNumbers.Count > 0 && plan.NumberDefinition is { } numbers) resources.Add(numbers.Asset);
        foreach (var cue in plan.Visuals)
        {
            Sprite(cue.Effect);
            if (cue.Zoom is not { } zoom) continue;
            if (cue.Officers is { } officers)
            {
                Sprite(officers.Definition.Idle);
                foreach (string clip in officers.FlagClips) flagResources.UnionWith(Textures.AnimationClips[clip].Frames);
            }
            resources.UnionWith([zoom.SourcePanel, zoom.TargetPanel]);
            if (zoom.Numbers is { } digits) resources.UnionWith([digits.Small.Asset, digits.Total.Asset]);
            resources.UnionWith(cue.Backdrop.Select(layer => layer.Asset));
            Body(zoom.Source); Body(zoom.Target); Sprite(zoom.Impact); Sprite(zoom.Block);
            Sprite(zoom.Projectile); Sprite(zoom.Wave);
            foreach (var effect in zoom.WaveEffects) Sprite(effect.Sprite);
            foreach (var sprite in zoom.DoubleEffect) Sprite(sprite);
        }
        _assets = Textures.CreateScope(Textures.LibraryAssets, owner: this); _assets.Request(resources);
        _flags = Textures.CreateScope(Textures.MapAssets, owner: this); _flags.Request(flagResources);
        foreach (var portrait in plan.Messages.Where(c => c.Message.SpeakerId is not null)
            .Select(c => (Speaker: c.Message.SpeakerId!.Value, View: c.Channel == BattleMessageChannel.Dialogue
                ? San9.Content.Images.PortraitView.Bust : San9.Content.Images.PortraitView.Face)).Distinct())
        {
            var role = $"information-person-{portrait.Speaker}";
            var scope = Textures.CreatePortraitScope(role, portrait.View, this);
            scope.Request([role]); _portraits.Add(scope);
        }
        QueueRedraw(); return ResourcesReady;
    }
    public void UpdatePlayback(TurnDayPresentation? day)
    {
        if (!ReferenceEquals(_day, day)) Prepare(day);
        if (day?.Battle is not { } plan || !ResourcesReady || _assets?.Error is not null || _flags?.Error is not null || _portraits.Any(p => p.Error is not null)) return;
        double time = day.Elapsed;
        var entrance = plan.Visuals.FirstOrDefault(c => c.EntranceStart is { } start && time >= start && time < c.End);
        double entranceElapsed = entrance is null ? 0 : time - entrance.EntranceStart!.Value;
        _entering = entrance is not null && time < entrance!.Start;
        _transition.Sample(entrance is null ? 0 : BattleSceneTransition.Opacity(entranceElapsed));
        MapAudioGain = entrance is null ? 1 : BattleSceneTransition.OutgoingGain(entranceElapsed);
        SceneAudioGain = _entering ? MapAudioGain : 1;
        foreach (var cue in plan.Visuals.Where(c => time >= c.Start && time < c.End))
        {
            if (_started.Add(cue.Id))
            {
                if (cue.Effect?.Sound is { } sound) PlaySound?.Invoke(sound);
                if (cue.Zoom is null && !cue.ScreenEffect) FocusRequested?.Invoke(cue.Target);
            }
            if (cue.Doubled && cue.Zoom is { } doubledZoom &&
                BattleReinforcementPlayback.Sample(doubledZoom.DoubleEffect, time - cue.Start) is not null &&
                _doubles.Add(cue.Id) && doubledZoom.DoubleEffect[0].Sound is { } doubledSound)
                PlaySound?.Invoke(doubledSound);
            if (cue.Playback is { } playback)
                for (int i = 0; i < playback.Sounds.Length; i++)
                    if (time >= cue.Start + playback.Sounds[i].Time && _sounds.Add((cue.Id, i)))
                        PlaySound?.Invoke(playback.Sounds[i].Role);
        }
        _zoom = plan.Visuals.FirstOrDefault(c => c.Zoom is not null && time >= c.Start && time < c.End);
        if (_zoom is null && !_entering)
            foreach (var cue in plan.MapEffects.Where(c => time >= c.Start && time < c.Start + c.Sprite.Columns * c.Sprite.FrameMilliseconds / 1000.0))
                if (_started.Add(cue.Id) && cue.Sprite.Sound is { } role) PlayMapSound?.Invoke(role, Project(cue.Target));
        if (_zoom is { } current && SoundEnabled())
        {
            _ambience.VolumeDb = Mathf.LinearToDb(BattleSceneTransition.IncomingGain(time - (current.EntranceStart ?? current.Start - BattleSceneTransition.DarkenDuration)));
            if (_ambienceCue != current.Id)
            { _ambience.Stop(); _ambience.Stream = _streams.Get(current.Zoom!.AmbienceSound, loop: true); _ambienceCue = current.Id; }
            if (!_ambience.Playing) _ambience.Play();
        }
        else _ambience.Stop();
        if (_zoom?.Playback?.Numbers is { } rolling && rolling.IsRolling(time - _zoom.Start) && SoundEnabled() && !IsPaused())
        {
            var stream = _streams.Get(rolling.Definition.RollSound, loop: true);
            if (_numberRoll.Stream != stream) { _numberRoll.Stop(); _numberRoll.Stream = stream; }
            if (!_numberRoll.Playing) _numberRoll.Play();
        }
        else _numberRoll.Stop();
        int slot = 0;
        void Effect(BattleSprite asset, double elapsed, Vector2 point, float opacity = 1, int? frameIndex = null)
        {
            if (!_assets!.TryGetReady(asset.Asset, out var texture) && (_flags is null || !_flags.TryGetReady(asset.Asset, out texture))) return;
            Sprite2D sprite;
            if (slot == _effects.Count) { sprite = new(); AddChild(sprite); _effects.Add(sprite); } else sprite = _effects[slot];
            slot++; sprite.Show(); sprite.Texture = texture; sprite.Hframes = asset.Columns;
            sprite.Frame = frameIndex ?? Math.Clamp((int)(elapsed * 1000 / asset.FrameMilliseconds), 0, asset.Columns - 1);
            sprite.Centered = false; sprite.Position = point + new Vector2(asset.OffsetX - (asset.Centered ? texture.GetWidth() / asset.Columns / 2f : 0), asset.OffsetY);
            sprite.Modulate = new Color(1, 1, 1, asset.Opacity * opacity);
            sprite.Material = asset.Additive ? _additive : null;
        }
        foreach (var cue in plan.Visuals.Where(c => c.Effect is not null && time >= c.Start && time < c.End))
            if (cue.FadeOutStart is not null || time - cue.Start < cue.Effect!.Columns * cue.Effect.FrameMilliseconds / 1000.0)
                Effect(cue.Effect!, time - cue.Start, cue.ScreenEffect ? new(512, 0) : Project(cue.Target),
                    cue.FadeOutStart is { } fadeStart ? BattleEffectFade.Opacity(time - fadeStart) : 1);
        if (_zoom is null && !_entering)
            foreach (var cue in plan.MapEffects.Where(c => time >= c.Start && time < c.Start + c.Sprite.Columns * c.Sprite.FrameMilliseconds / 1000.0))
                Effect(cue.Sprite, time - cue.Start, Project(cue.Target));
        if (_zoom is { } active)
        {
            double elapsed = time - active.Start;
            if (active.Playback is { } actorPlayback)
            {
                var actors = active.Officers?.Sample(elapsed, Textures.AnimationClips) ?? [];
                foreach (var frame in actorPlayback.SampleBodies(elapsed).Concat(actorPlayback.SampleProjectiles(elapsed)).Concat(actorPlayback.SampleLocalEffects(elapsed)).Concat(actors).OrderBy(f => f.Depth ?? f.Y))
                {
                    var texture = _assets!.Assets.ContainsKey(frame.Sprite.Asset) ? _assets[frame.Sprite.Asset] : _flags![frame.Sprite.Asset];
                    var point = new Vector2((float)frame.X, (float)frame.Y);
                    if (frame.Centered) point -= new Vector2(texture.GetWidth() / frame.Sprite.Columns / 2f, texture.GetHeight() / 2f);
                    Effect(frame.Sprite with { Centered = false }, frame.Frame * frame.Sprite.FrameMilliseconds / 1000.0, point, frame.Opacity, frame.Frame);
                }
            }
            if (active.Playback is { } playback)
                foreach (var impact in playback.Impacts)
                {
                    var asset = active.Blocked ? active.Zoom!.Block : active.Zoom!.Impact;
                    if (elapsed < impact.Time || elapsed >= impact.Time + 16 * asset.FrameMilliseconds / 1000.0) continue;
                    var texture = _assets![asset.Asset];
                    int step = (int)((elapsed - impact.Time) * 1000 / asset.FrameMilliseconds);
                    Effect(asset with { Centered = impact.Centered }, step % asset.Columns * asset.FrameMilliseconds / 1000.0,
                        new(impact.X, impact.Y - (impact.Centered ? texture.GetHeight() / 2f : 0) - step * 2), Math.Max(0, 128 - step * 8) / 256f, step % asset.Columns);
                }
            if (active.Doubled && BattleReinforcementPlayback.Sample(active.Zoom!.DoubleEffect, elapsed) is { } reinforcement)
            {
                Effect(reinforcement.Sprite, 0, new(512, (768 - _assets![reinforcement.Sprite.Asset].GetHeight()) / 2f), reinforcement.Opacity);
            }
        }
        for (int i = slot; i < _effects.Count; i++) _effects[i].Hide();
        QueueRedraw();
    }
    public override void _GuiInput(InputEvent input)
    {
        if ((_zoom is null && !_entering) || input is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mouse) return;
        if (new Rect2(820, 700, 88, 44).HasPoint(mouse.Position)) PauseRequested?.Invoke();
        else if (new Rect2(920, 700, 96, 44).HasPoint(mouse.Position)) _day?.SkipVisual();
        AcceptEvent();
    }
    public override void _Draw()
    {
        MouseFilter = _zoom is null && !_entering ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop;
        if (_day?.Battle is { } plan)
        {
            var feedbackFont = GetThemeFont("font");
            foreach (var feedback in plan.Visuals.Where(c => c.Zoom is null && c.MoraleGain > 0 && _day.Elapsed >= c.Start + 0.8 && _day.Elapsed < c.End))
            {
                string text = CommandText.Resolve(Texts, "battle.morale-change", $"+{feedback.MoraleGain}");
                if (text.Length > 0) DrawString(feedbackFont, Project(feedback.Target) + new Vector2(-28, -52), text, fontSize: 24,
                    modulate: feedback.SoldierGain > 0 ? Colors.LightGreen : Colors.OrangeRed);
            }
        }
        _numberLayer.QueueRedraw();
        if (_zoom is not { } cue || _assets is null || cue.Zoom is not { } zoom || _day is null) return;
        DrawRect(new(0, 0, 1024, 768), new Color(0.03f, 0.04f, 0.05f));
        foreach (var layer in cue.Backdrop) DrawTexture(_assets[layer.Asset], new(layer.X, layer.Y));
        DrawTexture(_assets[zoom.SourcePanel], new(0, 632)); DrawTexture(_assets[zoom.TargetPanel], new(680, 0));
        var font = GetThemeFont("font");
        string Person(int id) => Texts.Resolve(_day.Before.MessageSpeaker(id).NameKey);
        string Name(BattleObjectContext obj) => obj.IsSettlement ? _day.Before.Settlements.First(s => s.Id == obj.Id).ResolveName(Texts) :
            obj.CommanderId is int commander ? CommandText.Resolve(Texts, "battle.unit-name", Person(commander)) : CommandText.Resolve(Texts, "battle.unit");
        string Force(BattleObjectContext obj) => obj.ForceId is int force
            ? CommandText.Resolve(Texts, "battle.force-name", Texts.Resolve(_day.Before.Forces.Single(f => f.Id == force).NameKey)) : "";
        void PanelText(string text, Vector2 topLeft) => DrawString(font, topLeft + new Vector2(0, font.GetAscent(24)), text, fontSize: 24);
        PanelText(Force(cue.Source), new(72, 676));
        PanelText(Name(cue.Source), new(72, 708));
        PanelText(Texts.Resolve(_day.Before.Information.Tactics.Single(t => t.Id == cue.TacticId).NameKey), new(232, 692));
        PanelText(Force(cue.Target), new(816, 40));
        PanelText(Name(cue.Target), new(816, 72));
        DrawString(font, new(832, 730), CommandText.Resolve(Texts, IsPaused() ? "battle.resume" : "battle.pause"), fontSize: 20);
        DrawString(font, new(932, 730), CommandText.Resolve(Texts, "battle.skip"), fontSize: 20);
    }
    private void DrawNumbers(BattleNumberLayer layer)
    {
        if (_day?.Battle is not { } plan || _assets is null || !ResourcesReady) return;
        if (_zoom is null && !_entering && plan.NumberDefinition is { } numbers)
            foreach (var numberCue in plan.MapNumbers)
            {
                if (BattleMapNumberPlayback.Sample(numbers, numberCue.Amount, _day.Elapsed - numberCue.Start) is not { } frame) continue;
                var point = Project(numberCue.Target) + new Vector2((float)frame.X, (float)frame.Y);
                for (int i = 0; i < frame.Text.Length; i++)
                    layer.DrawTextureRectRegion(_assets[numbers.Asset], new(point + new Vector2(i * numbers.GlyphWidth, 0), new(numbers.GlyphWidth, numbers.GlyphHeight)),
                        new(BattleMapNumberPlayback.Column(frame.Text[i]) * numbers.GlyphWidth, frame.Row * numbers.GlyphHeight, numbers.GlyphWidth, numbers.GlyphHeight));
            }
        if (_zoom is { Playback.Numbers: { } numberPlayback } cue)
        {
            double elapsed = _day.Elapsed - cue.Start;
            var glyphs = numberPlayback.SampleTotal(elapsed);
            if (!cue.Blocked) glyphs = glyphs.Concat(cue.Playback!.Impacts.SelectMany(p => numberPlayback.SampleSmall(p, elapsed)));
            foreach (var glyph in glyphs)
                layer.DrawTextureRectRegion(_assets[glyph.Sheet.Asset], new(new((float)glyph.X, (float)glyph.Y), new(glyph.Sheet.Width, glyph.Sheet.Height)),
                    new(0, (float)glyph.SourceY, glyph.Sheet.Width, glyph.Sheet.Height), new Color(1, 1, 1, glyph.Opacity));
        }
    }
    public override void _Ready()
    {
        _streams = new(ContentRoot, Sounds); _ambience = new(); AddChild(_ambience); _numberRoll = new(); AddChild(_numberRoll);
        _numberLayer = new() { DrawNumbers = DrawNumbers, ZIndex = 1 }; AddChild(_numberLayer);
        _transition = new(); AddChild(_transition);
    }
    public override void _ExitTree()
    { _ambience.Stop(); _ambience.Stream = null; _numberRoll.Stop(); _numberRoll.Stream = null; _streams.Dispose(); _assets?.Dispose(); _flags?.Dispose(); foreach (var portrait in _portraits) portrait.Dispose(); Textures.Defer(_additive.Dispose); }
}
