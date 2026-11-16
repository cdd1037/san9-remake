using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Text;
using San9.Rendering;

namespace San9.Client.Godot;

/// <summary>Owns day feedback deduplication, battle views and spatial audio playback.</summary>
public partial class BattlePlaybackFlow : Control
{
    public GameSession Session { get; init; } = null!;
    public MapWorld World { get; init; } = null!;
    public TurnPresentation Turn { get; init; } = null!;
    public TextureService Textures { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public CommandPresentation CommandPresentation { get; init; } = null!;
    public string ContentRoot { get; init; } = "";
    public SceneMusicPlayer Music { get; init; } = null!;
    public SceneSoundPlayer SceneSounds { get; init; } = null!;
    public Func<bool> SoundEnabled { get; init; } = null!;
    public Func<bool> NarrationActive { get; init; } = null!;
    public Action<IReadOnlyList<CommandMessage>, StrategySnapshot, Action?> Narrate { get; init; } = null!;
    public Func<MapMessageRecord, bool> Record { get; init; } = null!;
    public Func<MapSpeechMessage, bool> Speech { get; init; } = null!;
    private MapSoundPlayer _mapSounds = null!;
    private MapAudioPlayback _mapAudioPlayback = null!;
    private readonly MapBuildingAudio _buildingAudio = new();
    private BattlePresentationView _battleView = null!;
    private DayFeedbackDispatcher _feedback = null!;
    public bool IsSceneActive => _battleView.IsSceneActive;
    public bool IsReadyFor(TurnDayPresentation? day) => _battleView.IsReadyFor(day);
    public bool Prepare(TurnDayPresentation? day) => _battleView.Prepare(day);
    public void RetryResources() => _battleView.RetryResources();
    public override void _Ready()
    {
        _mapAudioPlayback = new(Random.Shared.Next, role => CommandPresentation.Sounds[role]);
        _feedback = new(Narrate, Record, Speech, role => SceneSounds.Play(role, SoundEnabled()));
        _mapSounds = new() { ContentRoot = ContentRoot, Sounds = CommandPresentation.Sounds };
        AddChild(_mapSounds);
        _battleView = new()
        {
            Textures = Textures,
            Texts = Texts,
            ZIndex = 3,
            MouseFilter = MouseFilterEnum.Ignore,
            ContentRoot = ContentRoot,
            Sounds = CommandPresentation.Sounds,
            SoundEnabled = SoundEnabled,
            PlaySound = role => SceneSounds.PlayOverlapping(role, SoundEnabled()),
            PlayMapSound = (role, point) => _mapSounds.PlayOnce(role, point, SoundEnabled()),
            IsPaused = () => Turn.Paused,
            Project = obj =>
            {
                Vector2 point;
                if (!obj.IsSettlement && World.TrySpeechUnitAnchor(obj.Id, out var anchor, out _)) point = anchor;
                else
                {
                    var cell = BattleCell(obj); var center = MapGeometry.Center(cell.X, cell.Y, World.Package!.Height);
                    point = new(center.X, center.Y - 10 * (World.Package!.Squares[cell.Y * World.Package.Width + cell.X].Elevation - 1));
                }
                return GetGlobalTransformWithCanvas().AffineInverse() * (World.GetGlobalTransformWithCanvas() * point);
            }
        };
        AddChild(_battleView);
    }
    public void Update()
    {
        // Narration pauses the day clock, not projection: its opening frame may end the previous scene.
        _battleView.UpdatePlayback(Turn.Day);
        _battleView.UpdateAudio(Turn.Day);
        _mapSounds.TransitionGain = _battleView.MapAudioGain;
        SceneSounds.SetTransitionGain(_battleView.SceneAudioGain);
        Music.SetMapTransitionGain(_battleView.MapAudioGain);
        UpdateMapAudio();
        UpdateRetirementAudio();
    }
    public override void _ExitTree()
    {
        Music.SetMapTransitionGain(1);
        SceneSounds.SetTransitionGain(1);
        _feedback.Reset();
        _mapAudioPlayback.Clear();
    }
    private San9.Core.Domain.MapCell BattleCell(San9.Core.Simulation.BattleObjectContext obj)
    {
        if (obj.Position is { } position) return San9.Core.Domain.MapCoordinates.Cell(position);
        var place = (Turn.Day?.Before ?? Session.View).Settlements.First(s => s.Id == obj.ServicePlaceId);
        return new(place.X, place.Y);
    }

    public void PresentDay(TurnDayPresentation day)
    {
        _battleView.PublishMapFeedback(day);
        _feedback.Publish(day);
    }

    private void UpdateMapAudio()
    {
        var audio = CommandPresentation.MapAudio!;
        Music.SetMapTrack(Turn.Screen == TurnScreen.Terminal ? null : MapMusic.Select(Turn.Display ?? Session.View, audio, Turn.UsesExecutionMusic));
        var frame = World.Display?.Presented;
        if (frame is not null && World.IsVisibleReady)
        {
            var damage = _buildingAudio.Observe(frame.Region.Scene, audio);
            if (SoundEnabled() && Turn.IsExecutionPresentation && !NarrationActive() && World.LoadError is null && !_battleView.IsZoomActive)
            {
                var mapTransform = GetGlobalTransformWithCanvas().AffineInverse() * World.GetGlobalTransformWithCanvas();
                var projected = damage.Select(source =>
                {
                    var point = mapTransform * new Vector2((float)source.X, (float)source.Y);
                    return source with { X = point.X, Y = point.Y, Resource = CommandPresentation.Sounds[source.Role] };
                });
                foreach (var voice in MapAudioMix.Select(projected, Size.X, Size.Y))
                    _mapSounds.PlayOnce(voice.Role, new((float)voice.X, (float)voice.Y), true, voice.Gain);
            }
        }
        if (!SoundEnabled() || !Turn.IsExecutionPresentation || NarrationActive() || World.LoadError is not null || _battleView.IsZoomActive)
        {
            if (!SoundEnabled() || World.LoadError is not null) _mapSounds.StopOneShots();
            _mapSounds.UpdateVoices([], Turn.Day);
            _mapAudioPlayback.Clear();
            return;
        }
        // A pending frame does not end the audible scene. Keep loop positions across daily publication.
        if (Turn.Day is null || frame is null || !World.IsDayReady || !ReferenceEquals(frame.Region.Scene.Day, Turn.Day)) return;
        _mapAudioPlayback.BeginDay(Turn.Day);
        var transform = GetGlobalTransformWithCanvas().AffineInverse() * World.GetGlobalTransformWithCanvas();
        var sources = frame.Region.Scene.Playback.SoundSources(audio, frame.Region.Units).Select(source =>
        {
            var p = transform * new Vector2((float)source.X, (float)source.Y);
            return source with { X = p.X, Y = p.Y, Resource = CommandPresentation.Sounds[source.Role] };
        });
        _mapSounds.UpdateVoices(_mapAudioPlayback.Update(sources, Size.X, Size.Y, audio.BattleAmbience,
            frame.Region.Scene.Playback.PlaybackMilliseconds, Turn.Day.Elapsed * 1000 < MapMovementCadence.ArrivalMilliseconds), Turn.Day);
    }

    private void UpdateRetirementAudio()
    {
        if (World.Display?.Presented is not {} frame || !World.IsVisibleReady || CommandPresentation.MapAudio is not {} audio) return;
        var transform = GetGlobalTransformWithCanvas().AffineInverse() * World.GetGlobalTransformWithCanvas();
        var sources = frame.Region.Scene.Playback.TakeRetirementSounds(audio).Select((sound, i) =>
        {
            var point = transform * new Vector2((float)sound.X, (float)sound.Y);
            return new MapSoundSource($"retirement:{i}", sound.Role, point.X, point.Y)
                { Resource = CommandPresentation.Sounds[sound.Role] };
        });
        foreach (var voice in MapAudioMix.Select(sources, Size.X, Size.Y))
            _mapSounds.PlayOnce(voice.Role, new((float)voice.X, (float)voice.Y), SoundEnabled(), voice.Gain);
    }

}
