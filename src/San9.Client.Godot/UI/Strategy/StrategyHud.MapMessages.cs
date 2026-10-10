using Godot;
using San9.Application.Presentation;
using San9.Rendering;

namespace San9.Client.Godot;

public partial class StrategyHud
{
    private readonly MapMessageLog _mapRecords = new();
    private readonly MapSpeechPlayback _mapSpeech = new();
    private readonly Dictionary<Guid,(MapSpeechView View,Vector2 Anchor,bool Placed)> _speechViews = [];
    private Control _speechHost = null!;
    private TextureScope _speechTextures = null!;
    private bool _mapRecordsRequested;

    private void RecordNarration(CommandMessage message)
    {
        if (!message.RecordInJournal) return;
        if (message.MapRecord is { } record) PresentMapRecord(record);
        else _executionPanels.Append(message.Message);
    }

    private IReadOnlyList<CommandMessage> PresentExecutionMessages(IReadOnlyList<CommandMessage> messages)
    {
        List<CommandMessage> narration = [];
        foreach (var message in messages)
        {
            if (message.MapSpeech is { } speech) PresentMapSpeech(speech);
            else if (message.RecordInJournal && message.MapRecord is { } record && message.SoundRole is null && message.IllustrationRole is null)
                PresentMapRecord(record);
            else narration.Add(message);
        }
        return narration;
    }

    private void BuildMapMessages()
    {
        _speechTextures = Textures.CreateScope(owner: this);
        _speechTextures.RequestPrefixes("map-speech-","text-icon-");
        // Above map annotations (-2), below the HUD frame and controls (0).
        _speechHost = new Control { Size = Size, ZIndex = -1, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_speechHost);
    }

    /// <summary>The producer owns visibility and supplies a stable identity and occurrence location.</summary>
    public bool PresentMapRecord(MapMessageRecord entry)
    {
        if (!_built) throw new InvalidOperationException("The map HUD is not ready.");
        if (!_executionPanels.Append(entry)) return false;
        _mapRecordsRequested = true;
        _executionPanels.ExpandImmediately();
        if (entry.Location is { } location) _minimap!.PresentRecordLocation(location);
        return true;
    }

    /// <summary>Speech and the result record are independent channels; speech never manufactures a result record.</summary>
    public bool PresentMapSpeech(MapSpeechMessage message)
    {
        if (!_built) throw new InvalidOperationException("The map HUD is not ready.");
        if (!_mapSpeech.Publish(message)) return false;
        var view = new MapSpeechView { Message = message, Service = Textures, Textures = _speechTextures, Skin = Skin, Visible = false };
        _speechHost.AddChild(view);
        var center = MapGeometry.Center(message.Location.X,message.Location.Y);
        _speechViews.Add(message.Id,(view,new(center.X,center.Y),false));
        return true;
    }

    private void UpdateMapMessages(double delta)
    {
        _mapSpeech.Advance(delta,message => _speechViews[message.Id].View.ResourcesReady && World.IsVisibleReady);
        var retained = _mapSpeech.Entries.Select(entry => entry.Message.Id).ToHashSet();
        foreach (var id in _speechViews.Keys.Where(id => !retained.Contains(id)).ToArray())
        {
            var old = _speechViews[id].View;
            _speechHost.RemoveChild(old); old.QueueFree(); _speechViews.Remove(id);
        }
        var occupied = _speechViews.Where(pair => pair.Value.Placed && pair.Value.View.Visible)
            .ToDictionary(pair => pair.Key, pair => pair.Value.View.HitBounds);
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
            occupied.Remove(entry.Message.Id);
            occupied[entry.Message.Id] = state.View.Place(screen,occupied.Values.ToArray(),!state.Placed,preferred);
            state.Placed = true;
            _speechViews[entry.Message.Id] = state;
        }
    }

    private void HandleMapSpeechInput(InputEvent input)
    {
        if (!_built || OverlayHost.HasModal || _notice.Visible) return;
        bool dismiss = input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } ||
            input is InputEventMouseMotion motion && motion.ButtonMask.HasFlag(MouseButtonMask.Left);
        if (!dismiss) return;
        var point = GetLocalMousePosition();
        if (IsBlockedPoint(point)) return;
        bool handled = false;
        foreach (var entry in _mapSpeech.Entries.ToArray())
        {
            var view = _speechViews[entry.Message.Id].View;
            if (!view.Visible || !view.Contains(point)) continue;
            _mapSpeech.Dismiss(entry.Message.Id); view.Hide(); handled = true;
        }
        if (handled) GetViewport().SetInputAsHandled();
    }
}
