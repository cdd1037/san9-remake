using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Application.Presentation;
using San9.Client.Godot.Persistence;
using San9.Client.Godot.Settings;

namespace San9.Client.Godot.Tests;

public sealed class UiPreferencesTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "san9-ui-preferences-tests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "ui-preferences.json");

    [Fact]
    public void NavigationIsSharedInMemoryAndPersistsOnlyAtClientShutdown()
    {
        var settingsStore = new UserSettingsStore(Path.Combine(_directory, "user-settings.json"));
        var settings = new UserSettingsSession(settingsStore);
        var oldDraft = settings.Current with { SoundEnabled = false };
        var store = new UiPreferencesStore(FilePath);
        var preferences = new UiPreferencesSession(store.Load(), store.Save);
        preferences.Save();
        Assert.False(Directory.Exists(_directory));

        foreach (var group in Enum.GetValues<SelectionViewGroup>()) preferences.SetMap(group, false);
        preferences.SetMap(SelectionViewGroup.Cities, true);
        preferences.Update(p => p with { SaveLoadPage = 7, RosterPage = RosterPage.Units,
            EdgeScrollMode = EdgeScrollMode.HoldLeftButton, ShowForces = false, ShowBoundaries = false, AutoAssignOfficerFlag = false });
        var opening = new SaveLoadBrowser();
        opening.Open(SaveLoadMode.Load, preferences.Current.SaveLoadPage);
        opening.Close();
        var inGame = new SaveLoadBrowser();
        inGame.Open(SaveLoadMode.Save, preferences.Current.SaveLoadPage);
        Assert.Equal(opening.Page, inGame.Page);
        settings.Apply(oldDraft);
        Assert.False(File.Exists(FilePath));

        preferences.Save();
        var restarted = new UiPreferencesSession(new UiPreferencesStore(FilePath).Load());
        Assert.Equal(preferences.Current, restarted.Current);
        Assert.All(Enum.GetValues<SelectionViewGroup>(), group =>
            Assert.Equal(group == SelectionViewGroup.Cities, restarted.IsMap(group)));
        Assert.False(restarted.Current.AutoAssignOfficerFlag);
        Assert.Equal(oldDraft, settingsStore.Load());
    }

    [Fact]
    public void DesktopPlacementPersistsIndependentlyOfEnvironmentSettings()
    {
        var store = new UiPreferencesStore(FilePath);
        store.Save(UiPreferences.Default);
        var json = JsonNode.Parse(File.ReadAllText(FilePath, Encoding.UTF8))!;
        json["preferences"]!.AsObject().Remove("window");
        File.WriteAllText(FilePath, json.ToJsonString(), Encoding.UTF8);
        Assert.Null(store.Load().Window);

        var placement = new WindowPlacement(-1400, 80, 1280, 960, true);
        var preferences = new UiPreferencesSession(store.Load(), store.Save);
        preferences.Update(p => p with { Window = placement });
        Assert.Null(store.Load().Window);
        preferences.Save();
        Assert.Equal(placement, store.Load().Window);
        Assert.Throws<InvalidDataException>(() => preferences.Update(p => p with
            { Window = placement with { Width = 0 } }));
        Assert.Equal(placement, preferences.Current.Window);
    }

    [Fact]
    public void UnchangedAndRevertedNavigationDoesNotRewriteAndFailedShutdownCanRetry()
    {
        int writes = 0;
        bool fail = true;
        var preferences = new UiPreferencesSession(save: _ =>
        {
            if (fail) throw new IOException("unavailable");
            writes++;
        });
        preferences.SetMap(SelectionViewGroup.Forces, false);
        preferences.SetMap(SelectionViewGroup.Forces, true);
        preferences.Save();
        preferences.Update(p => p with { RosterPage = RosterPage.Officers });
        Assert.Throws<IOException>(preferences.Save);
        Assert.Equal(RosterPage.Officers, preferences.Current.RosterPage);
        fail = false;
        preferences.Save(); preferences.Save();
        Assert.Equal(1, writes);
    }

    [Theory]
    [InlineData("version", "9999")]
    [InlineData("version", "1")]
    [InlineData("preferences.autoAssignOfficerFlag", "null")]
    [InlineData("preferences", "null")]
    [InlineData("preferences.saveLoadPage", "-1")]
    [InlineData("preferences.rosterPage", "\"Unknown\"")]
    [InlineData("preferences.edgeScrollMode", "0")]
    [InlineData("preferences.selectionViews.cities", "null")]
    public void InvalidDocumentIsReportedWithoutOverwritingIt(string property, string value)
    {
        var store = new UiPreferencesStore(FilePath);
        store.Save(UiPreferences.Default);
        var json = JsonNode.Parse(File.ReadAllText(FilePath, Encoding.UTF8))!;
        var keys = property.Split('.');
        JsonNode owner = json;
        foreach (var key in keys[..^1]) owner = owner[key]!;
        owner[keys[^1]] = JsonNode.Parse(value);
        string invalid = json.ToJsonString();
        File.WriteAllText(FilePath, invalid, Encoding.UTF8);
        Assert.True(Record.Exception(() => store.Load()) is JsonException or InvalidDataException);
        Assert.Equal(invalid, File.ReadAllText(FilePath, Encoding.UTF8));
    }

    [Fact]
    public void RestoredPageIsClampedToTheCurrentBrowserAndAutomaticPagesRemainReachable()
    {
        var browser = new SaveLoadBrowser(5);
        browser.Open(SaveLoadMode.Load, 999);
        Assert.Equal(browser.PageCount - 1, browser.Page);
        Assert.True(browser.IsAutoSavePage);
        browser.Close();
        browser.Open(SaveLoadMode.Save, -1);
        Assert.Equal(0, browser.Page);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
