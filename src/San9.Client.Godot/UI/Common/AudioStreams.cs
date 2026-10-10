using Godot;
using San9.Content.Interface;
using San9.Content.Packages;

namespace San9.Client.Godot;

/// <summary>Each playback owner caches and releases physical assets independently of their roles.</summary>
internal sealed class AudioStreams(string root, IReadOnlyDictionary<string, string> sounds) : IDisposable
{
    private readonly Dictionary<(string Path, bool Loop), AudioStream> _streams = [];

    internal AudioStream Get(string role, bool loop)
    {
        var key = (Path: sounds[role], Loop: loop);
        if (_streams.TryGetValue(key, out var stream)) return stream;
        stream = Load(PackageFiles.Resolve(root, key.Path), loop);
        _streams.Add(key, stream);
        return stream;
    }

    public void Dispose()
    {
        foreach (var stream in _streams.Values) stream.Dispose();
        _streams.Clear();
    }

    private static AudioStream Load(string path, bool loop)
    {
        // WAV import modes are 1 (disabled) and 2 (forward); -1 resolves the last valid frame.
        AudioStream? stream = AudioFileFormats.Identify(path) switch
        {
            AudioFileFormat.OggVorbis => AudioStreamOggVorbis.LoadFromFile(path),
            AudioFileFormat.Mp3 => AudioStreamMP3.LoadFromFile(path),
            AudioFileFormat.Wave => AudioStreamWav.LoadFromFile(path, new()
                { ["edit/loop_mode"] = loop ? 2 : 1, ["edit/loop_end"] = -1 }),
            _ => throw new InvalidDataException("Unsupported audio format: " + path)
        };
        if (stream is null) throw new InvalidDataException("Unable to load audio: " + path);
        switch (stream)
        {
            case AudioStreamOggVorbis ogg: ogg.Loop = loop; break;
            case AudioStreamMP3 mp3: mp3.Loop = loop; break;
        }
        return stream;
    }
}
