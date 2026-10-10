namespace San9.Content.Interface;

public enum AudioFileFormat { Wave, OggVorbis, Mp3 }

public static class AudioFileFormats
{
    public static AudioFileFormat? Identify(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".wav" => AudioFileFormat.Wave,
        ".ogg" => AudioFileFormat.OggVorbis,
        ".mp3" => AudioFileFormat.Mp3,
        _ => null
    };
}
