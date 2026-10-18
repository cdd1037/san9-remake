using San9.Application.Presentation;
using San9.Content.Interface;

namespace San9.Client.Godot;

/// <summary>Independent elapsed visual time. Pausing simulation does not stop the visible lifetime.</summary>
public sealed class MapSpeechPlayback
{
    private readonly HashSet<string> _published = [];
    private readonly List<Speech> _entries = [];
    public IReadOnlyList<Speech> Entries => _entries.AsReadOnly();

    public sealed class Speech(MapSpeechMessage message)
    {
        public MapSpeechMessage Message { get; } = message;
        private double _elapsed;
        public bool Visible => _elapsed + 1e-9 >= Message.DelaySeconds && !Expired;
        public bool Expired => _elapsed + 1e-9 >= Message.DelaySeconds + 5;
        public void Advance(double seconds) => _elapsed += seconds;
    }

    public bool Publish(MapSpeechMessage message)
    {
        if (message.Id == string.Empty || !Enum.IsDefined(message.Placement) ||
            !double.IsFinite(message.DelaySeconds) || message.DelaySeconds < 0 || string.IsNullOrWhiteSpace(message.SpeakerName))
            throw new ArgumentException("Invalid retained map speech.", nameof(message));
        if (!_published.Add(message.Id)) return false;
        _entries.Add(new(message));
        return true;
    }

    public void Advance(double seconds, Func<MapSpeechMessage, bool> ready)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        foreach (var entry in _entries)
            if (ready(entry.Message)) entry.Advance(seconds);
        _entries.RemoveAll(entry => entry.Expired);
    }

    public void Dismiss(string id) => _entries.RemoveAll(entry => entry.Message.Id == id);
    public bool IsComplete(string id) => _published.Contains(id) && _entries.All(entry => entry.Message.Id != id);
}
