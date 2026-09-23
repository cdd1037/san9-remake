using System.Text;
using System.Text.Json;
using San9.Application.Profiles;
using San9.Client.Godot.Profiles;
using San9.Client.Godot.Settings;

namespace San9.Client.Godot.Tests;

public sealed class PlayerProfileStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "san9-profile-tests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "profile.json");

    [Fact]
    public void MissingFile_DoesNotCreateProfile()
    {
        Assert.Same(PlayerProfile.Empty, new PlayerProfileStore(FilePath).Load());
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public void RoundTrip_PreservesUnknownFactsAndDoesNotChangeSettings()
    {
        var settings = new UserSettingsStore(Path.Combine(_directory, "user-settings.json"));
        settings.Save(UserSettings.Default with { MusicEnabled = false });
        var store = new PlayerProfileStore(FilePath);
        store.Save(PlayerProfile.Empty);
        Assert.Empty(store.Load().CompletedMilestones);
        var expected = store.Load().Complete("mod:教学").Complete("test:victory");
        store.Save(expected);
        Assert.True(expected.CompletedMilestones.SetEquals(new PlayerProfileStore(FilePath).Load().CompletedMilestones));
        string json = File.ReadAllText(FilePath, Encoding.UTF8);
        store.Save(new PlayerProfile(expected.CompletedMilestones.Reverse()));
        Assert.Equal(json, File.ReadAllText(FilePath, Encoding.UTF8));
        Assert.False(settings.Load().MusicEnabled);
        Assert.Equal(2, Directory.GetFiles(_directory).Length);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"version\":1}")]
    [InlineData("{\"version\":2,\"completedMilestones\":[]}")]
    [InlineData("{\"version\":1,\"completedMilestones\":null}")]
    [InlineData("{\"version\":1,\"completedMilestones\":[null]}")]
    [InlineData("{\"version\":1,\"completedMilestones\":[\" \" ]}")]
    [InlineData("{\"version\":1,\"completedMilestones\":[],\"unexpected\":true}")]
    public void InvalidDocument_IsReportedWithoutOverwriting(string json)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, json, Encoding.UTF8);
        var error = Record.Exception(() => new PlayerProfileStore(FilePath).Load());
        Assert.True(error is JsonException or InvalidDataException);
        Assert.Equal(json, File.ReadAllText(FilePath, Encoding.UTF8));
    }

    [Fact]
    public void FailedReplacement_ReportsFailureAndCleansTemporaryFile()
    {
        Directory.CreateDirectory(FilePath);
        Assert.NotNull(Record.Exception(() => new PlayerProfileStore(FilePath).Save(PlayerProfile.Empty)));
        Assert.True(Directory.Exists(FilePath));
        Assert.Empty(Directory.GetFiles(_directory));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
