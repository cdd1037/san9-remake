using San9.Importer.San9PkTc101.Interface;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class AudioAssetCompilerTests
{
    [Fact]
    public void ConstructionSoundsSeparateFortsFromBothOrientationsOfObstacles()
    {
        var sounds = MapAudioCompiler.ConstructionSounds("SE21", "SE22");
        foreach (int facility in new[] { 6, 7, 8, 9 })
            Assert.Equal("SE21", sounds[facility]);
        foreach (int facility in new[] { 10, 11, 12, 13, 14, 15, 16 })
            Assert.Equal("SE22", sounds[facility]);
    }

    [Fact]
    public void SharedPlaybackRolesReferenceOnePhysicalEffect()
    {
        var sources = CommandPresentationCompiler.SoundSources;
        var study = sources.Single(s => s.Role == "decision-study");
        var departure = sources.Single(s => s.Role == "mission-departure");
        Assert.Equal(AudioAssetCompiler.Path(study.File), AudioAssetCompiler.Path(departure.File));
        Assert.StartsWith("audio/sound-effects/", AudioAssetCompiler.Path(study.File));
    }

    [Fact]
    public void MusicIdentityDoesNotBindEncodingAndUnknownMeaningPreservesSourceName()
    {
        Assert.All(MapAudioCompiler.MusicSources, source =>
        {
            Assert.Empty(System.IO.Path.GetExtension(source.File));
            Assert.StartsWith("audio/music/", AudioAssetCompiler.Path(source.File));
        });
        Assert.Equal("audio/music/title.ogg", AudioAssetCompiler.Path("BGM/BGM_01.ogg"));
        Assert.Equal("audio/sound-effects/se_custom.wav", AudioAssetCompiler.Path("SE/SE_CUSTOM.WAV"));
        Assert.Equal(AudioAssetCompiler.Path("SE/SE_G011.WAV"), AudioAssetCompiler.Path("se/se_g011.wav"));
    }

    [Fact]
    public void ReplacingFormatChangesExportExtensionWithoutChangingSemanticName()
    {
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "san9-audio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(System.IO.Path.Combine(root, "BGM"));
        Directory.CreateDirectory(System.IO.Path.Combine(root, "SE"));
        try
        {
            string ogg = System.IO.Path.Combine(root, "BGM", "BGM_04.ogg");
            File.WriteAllBytes(ogg, []);
            File.WriteAllBytes(System.IO.Path.Combine(root, "BGM", "BGM_04.WMA"), []);
            Assert.Equal("audio/music/map-spring.ogg", AudioAssetCompiler.Path(
                AudioAssetCompiler.Resolve(AudioAssetCompiler.Discover(root), "BGM/BGM_04")));
            File.Delete(ogg);
            File.WriteAllBytes(System.IO.Path.Combine(root, "BGM", "BGM_04.MP3"), []);
            Assert.Equal("audio/music/map-spring.mp3", AudioAssetCompiler.Path(
                AudioAssetCompiler.Resolve(AudioAssetCompiler.Discover(root), "BGM/BGM_04")));
            File.WriteAllBytes(ogg, []);
            Assert.Throws<InvalidDataException>(() => AudioAssetCompiler.Discover(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void MissingPreparedMusicDoesNotFallBackToTranscodingSource()
    {
        Assert.Throws<InvalidDataException>(() => AudioAssetCompiler.Resolve(new Dictionary<string, string>(), "BGM/BGM_04"));
    }

    [Fact]
    public void ActionCandidatesIncludeThirdVariantsWithoutMixingFixedShipAttackFamilies()
    {
        foreach (int first in new[] { 30, 33, 36, 39, 42, 45, 56, 59, 64, 67 })
            Assert.Equal(new[] { first, first + 1, first + 2 }, MapAudioCompiler.ActionVariants(first));
        Assert.Equal(new[] { 73 }, MapAudioCompiler.ActionVariants(73));
        Assert.Equal(new[] { 74 }, MapAudioCompiler.ActionVariants(74));
        Assert.Equal(new[] { 70 }, MapAudioCompiler.ActionVariants(70));
        Assert.Empty(MapAudioCompiler.ActionVariants(-1));
    }
}
