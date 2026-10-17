using System.Text;
using System.Text.Json;
using San9.Client.Godot.Settings;

namespace San9.Client.Godot.Tests;

public sealed class UserSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "san9-settings-tests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "user-settings.json");

    [Fact]
    public void SessionPublishesOnlySavedDraftAndRetainsLoadedPreferencesInMemory()
    {
        var store = new UserSettingsStore(FilePath);
        var session = new UserSettingsSession(store);
        session.Load();
        var draft = session.Current with { MusicEnabled = false, TacticScenesEnabled = false, MessageSpeed = MessageSpeed.Fast };
        Assert.True(session.Current.MusicEnabled);
        session.Apply(draft);
        Assert.Equal(draft, session.Current);
        Assert.Equal(draft, store.Load());
        Assert.Throws<JsonException>(() => session.Apply(draft with { MessageSpeed = (MessageSpeed)99 }));
        Assert.Equal(draft, session.Current);
        File.Delete(FilePath);
        Directory.CreateDirectory(FilePath);
        Assert.ThrowsAny<Exception>(() => session.Apply(UserSettings.Default));
        Assert.Equal(draft, session.Current);
    }

    [Fact]
    public void MissingFile_ReturnsDefaultsWithoutCreatingFile()
    {
        Assert.Equal(UserSettings.Default, new UserSettingsStore(FilePath).Load());
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public void AllPreferences_RoundTripAndReplaceExistingFile()
    {
        var store = new UserSettingsStore(FilePath);
        foreach (var speed in Enum.GetValues<MessageSpeed>())
        foreach (var report in Enum.GetValues<ProgressReportMode>())
        foreach (var playback in Enum.GetValues<PlaybackSpeed>())
        {
            var expected = new UserSettings(false, true, false, speed, report) { TacticScenesEnabled = false, PlaybackSpeed = playback };
            store.Save(expected);
            Assert.Equal(expected, new UserSettingsStore(FilePath).Load());
        }
        store.Save(UserSettings.Default with { SoundEnabled = false });
        Assert.False(store.Load().SoundEnabled);
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void ExistingSettingsWithoutTacticScenes_LoadWithDefaultAndKeepOtherPreferences()
    {
        const string json = "{\"version\":1,\"settings\":{\"musicEnabled\":false,\"soundEnabled\":true,\"moviesEnabled\":false,\"messageSpeed\":\"Fast\",\"progressReport\":\"Summary\"}}";
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, json, Encoding.UTF8);

        var store = new UserSettingsStore(FilePath);
        var loaded = store.Load();

        Assert.Equal(new UserSettings(false, true, false, MessageSpeed.Fast, ProgressReportMode.Summary), loaded);
        Assert.True(loaded.TacticScenesEnabled);
        Assert.Equal(PlaybackSpeed.Normal, loaded.PlaybackSpeed);
        Assert.Equal(json, File.ReadAllText(FilePath, Encoding.UTF8));

        store.Save(loaded with { TacticScenesEnabled = false });
        Assert.False(store.Load().TacticScenesEnabled);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{\"version\":2,\"settings\":null}")]
    [InlineData("{\"version\":1,\"settings\":null}")]
    [InlineData("{\"version\":1,\"settings\":{}}")]
    [InlineData("{\"version\":1,\"settings\":{\"musicEnabled\":true,\"soundEnabled\":true,\"moviesEnabled\":true,\"messageSpeed\":\"Normal\"}}")]
    public void InvalidDocument_IsReportedAndNotOverwritten(string json)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, json, Encoding.UTF8);
        var error = Record.Exception(() => new UserSettingsStore(FilePath).Load());
        Assert.True(error is JsonException or InvalidDataException);
        Assert.Equal(json, File.ReadAllText(FilePath, Encoding.UTF8));
    }

    [Theory]
    [InlineData("\"Fast\"", "\"Unknown\"")]
    [InlineData("\"Fast\"", "0")]
    public void InvalidEnum_IsRejected(string original, string replacement)
    {
        var store = new UserSettingsStore(FilePath);
        store.Save(UserSettings.Default with { MessageSpeed = MessageSpeed.Fast });
        string json = File.ReadAllText(FilePath, Encoding.UTF8).Replace(original, replacement);
        File.WriteAllText(FilePath, json, Encoding.UTF8);
        Assert.Throws<JsonException>(() => store.Load());
        Assert.Equal(json, File.ReadAllText(FilePath, Encoding.UTF8));
    }

    [Fact]
    public void InvalidSave_PreservesExistingPreferences()
    {
        var store = new UserSettingsStore(FilePath);
        store.Save(UserSettings.Default);
        Assert.Throws<JsonException>(() => store.Save(UserSettings.Default with { MessageSpeed = (MessageSpeed)99 }));
        Assert.Throws<JsonException>(() => store.Save(UserSettings.Default with { ProgressReport = (ProgressReportMode)99 }));
        Assert.Throws<JsonException>(() => store.Save(UserSettings.Default with { PlaybackSpeed = (PlaybackSpeed)3 }));
        Assert.Equal(UserSettings.Default, store.Load());
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void FailedReplacement_DoesNotLeaveTemporaryFile()
    {
        Directory.CreateDirectory(FilePath);
        Assert.NotNull(Record.Exception(() => new UserSettingsStore(FilePath).Save(UserSettings.Default)));
        Assert.True(Directory.Exists(FilePath));
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Theory]
    [InlineData("\"Unknown\"")]
    [InlineData("4")]
    public void InvalidPlaybackSpeedDoesNotReplaceActivePreferences(string value)
    {
        var store = new UserSettingsStore(FilePath);
        store.Save(UserSettings.Default with { PlaybackSpeed = PlaybackSpeed.Quadruple });
        string json = File.ReadAllText(FilePath, Encoding.UTF8).Replace("\"Quadruple\"", value);
        File.WriteAllText(FilePath, json, Encoding.UTF8);
        var session = new UserSettingsSession(store);
        Assert.Throws<JsonException>(() => session.Load());
        Assert.Equal(UserSettings.Default, session.Current);
        Assert.Equal(json, File.ReadAllText(FilePath, Encoding.UTF8));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
