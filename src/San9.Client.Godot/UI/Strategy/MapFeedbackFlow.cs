using Godot;
using San9.Application.Presentation;
using San9.Rendering;

namespace San9.Client.Godot;

public partial class MapFeedbackFlow : Control
{
    public MapWorld World { get; init; } = null!;
    public TextureService Textures { get; init; } = null!;
    public San9.Content.Interface.InterfaceSkin Skin { get; init; } = null!;
    public string ContentRoot { get; init; } = "";
    public San9.Content.Interface.CommandPresentation CommandPresentation { get; init; } = null!;
    public ExecutionPanels Panels { get; init; } = null!;
    public StrategyMinimap Minimap { get; init; } = null!;
    public SceneSoundPlayer SceneSounds { get; init; } = null!;
    public Func<bool> SoundEnabled { get; init; } = null!;
    public Func<IEnumerable<OverlayBounds>> Obstacles { get; init; } = null!;
    public Func<Vector2, bool> InputBlocked { get; init; } = null!;
    public Func<bool> ModalOpen { get; init; } = null!;
    public Func<bool> CanRevealRecords { get; init; } = null!;
    public bool ResourcesReady => _speechTextures.IsReady;
    public bool RecordsRequested { get; private set; }
    public bool IsComplete(string id) => _mapSpeech.IsComplete(id);
    public void Dismiss(string id) => _mapSpeech.Dismiss(id);
    public void ResetRecords() => RecordsRequested = false;
    public void StopSounds() => _recordSounds.Stop();
    public void FocusRecord(San9.Core.Domain.MapCell location)
    {
        World.FocusCell(location); Minimap.PresentRecordLocation(location);
        _recordSounds.Play(CommandPresentation.MapAudio!.RecordNotificationSound, SoundEnabled());
    }
    private readonly MapSpeechPlayback _mapSpeech = new();
    private readonly Dictionary<string,(MapSpeechView View,Vector2 Anchor,bool Placed)> _speechViews = [];
    private Control _speechHost = null!;
    private TextureScope _speechTextures = null!;
    private SceneSoundPlayer _recordSounds = null!;

    public IReadOnlyList<CommandMessage> PresentExecutionMessages(IReadOnlyList<CommandMessage> messages)
    {
        List<CommandMessage> narration = [];
        foreach (var message in messages)
        {
            if (message.MapSpeech is { } speech && !message.WaitForMapSpeech) PresentMapSpeech(speech);
            else narration.Add(message);
        }
        return narration;
    }

    public override void _Ready()
    {
        _recordSounds = new() { ContentRoot = ContentRoot, Sounds = CommandPresentation.Sounds, MaxPolyphony = 3 };
        AddChild(_recordSounds);
        _speechTextures = Textures.CreateScope();
        _speechTextures.RequestPrefixes("map-speech-","text-icon-");
        // Above map annotations (-2), below the HUD frame and controls (0).
        _speechHost = new Control { Size = Size, ZIndex = -1, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_speechHost);
    }

    /// <summary>The producer owns visibility and supplies a stable identity and occurrence location.</summary>
    public bool PresentMapRecord(MapMessageRecord entry)
    {
        if (!Panels.Append(entry)) return false;
        RecordsRequested = true;
        if (CanRevealRecords()) Panels.ExpandImmediately();
        if (entry.SoundRole is { } sound) SceneSounds.Play(sound, SoundEnabled());
        if (entry.Location is { } location)
        {
            Minimap.PresentRecordLocation(location);
            _recordSounds.Play(CommandPresentation.MapAudio!.RecordNotificationSound, SoundEnabled());
        }
        return true;
    }

    /// <summary>Speech and the result record are independent channels; speech never manufactures a result record.</summary>
    public bool PresentMapSpeech(MapSpeechMessage message)
    {
        if (!_mapSpeech.Publish(message)) return false;
        var view = new MapSpeechView { Message = message, Service = Textures, Textures = _speechTextures, Skin = Skin, Visible = false };
        _speechHost.AddChild(view);
        var center = MapGeometry.Center(message.Location.X,message.Location.Y, World.Package!.Height);
        _speechViews.Add(message.Id,(view,new(center.X,center.Y),false));
        return true;
    }

    public void Advance(double delta)
    {
        _mapSpeech.Advance(delta,message => _speechViews[message.Id].View.ResourcesReady && World.IsVisibleReady);
        var retained = _mapSpeech.Entries.Select(entry => entry.Message.Id).ToHashSet();
        foreach (var id in _speechViews.Keys.Where(id => !retained.Contains(id)).ToArray())
        {
            var old = _speechViews[id].View;
            _speechHost.RemoveChild(old); old.QueueFree(); _speechViews.Remove(id);
        }
        var viewport = World.PresentedViewportBounds;
        var bounds = new OverlayBounds(viewport.Position.X, viewport.Position.Y, viewport.Size.X, viewport.Size.Y);
        var occupied = Obstacles().ToList();
        foreach (var entry in _mapSpeech.Entries)
        {
            var state = _speechViews[entry.Message.Id];
            state.View.Visible = entry.Visible && state.View.ResourcesReady;
            if (!state.View.Visible) continue;
            San9.Content.Interface.MapSpeechPlacement? preferred = null;
            if (entry.Message.UnitId is int unit && World.TrySpeechUnitAnchor(unit,out var current,out int direction))
            {
                state.Anchor = current;
                preferred = MapSpeechLayout.FromUnitDirection(direction);
            }
            // All map speech uses the map canvas transform; text/frame sizes remain logical UI pixels.
            var screen = GetGlobalTransformWithCanvas().AffineInverse() * (World.GetGlobalTransformWithCanvas() * state.Anchor);
            state.View.Visible = viewport.HasPoint(screen);
            if (state.View.Visible)
            {
                occupied.Add(state.View.Place(screen, bounds, occupied, state.Placed ? null : preferred));
                state.Placed = true;
            }
            _speechViews[entry.Message.Id] = state;
        }
    }

    public bool IsSpeechPoint(Vector2 point) => _speechViews.Values.Any(state => state.View.IsVisibleInTree() && state.View.Contains(point));

    public void HandleInput(InputEvent input)
    {
        if (ModalOpen()) return;
        bool dismiss = input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } ||
            input is InputEventMouseMotion motion && motion.ButtonMask.HasFlag(MouseButtonMask.Left);
        if (!dismiss) return;
        var point = GetLocalMousePosition();
        if (InputBlocked(point)) return;
        bool handled = false;
        foreach (var entry in _mapSpeech.Entries.ToArray())
        {
            var view = _speechViews[entry.Message.Id].View;
            if (!view.Visible || !view.Contains(point)) continue;
            _mapSpeech.Dismiss(entry.Message.Id); view.Hide(); handled = true;
        }
        if (handled) GetViewport().SetInputAsHandled();
    }
    public override void _ExitTree() => _speechTextures.Dispose();
}
