using Godot;
using San9.Content.Interface;

namespace San9.Client.Godot;

internal static class AudioStreams
{
    internal static AudioStream Load(string path, bool loop)
    {
        AudioStream? stream = AudioFileFormats.Identify(path) switch
        {
            AudioFileFormat.OggVorbis => AudioStreamOggVorbis.LoadFromFile(path),
            AudioFileFormat.Mp3 => AudioStreamMP3.LoadFromFile(path),
            AudioFileFormat.Wave => AudioStreamWav.LoadFromFile(path),
            _ => throw new InvalidDataException("Unsupported audio format: " + path)
        };
        if (stream is null) throw new InvalidDataException("Unable to load audio: " + path);
        switch (stream)
        {
            case AudioStreamOggVorbis ogg: ogg.Loop = loop; break;
            case AudioStreamMP3 mp3: mp3.Loop = loop; break;
            case AudioStreamWav wav:
                wav.LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled;
                if (loop) wav.LoopEnd = (int)(wav.GetLength() * wav.MixRate);
                break;
        }
        return stream;
    }
}
