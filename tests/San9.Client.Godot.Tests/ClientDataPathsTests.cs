using San9.Client.Godot.Persistence;

namespace San9.Client.Godot.Tests;

public sealed class ClientDataPathsTests
{
    [Theory]
    [InlineData(true, "Windows", true)]
    [InlineData(true, "Linux", true)]
    [InlineData(false, "Windows", false)]
    [InlineData(false, "Linux", false)]
    [InlineData(true, "macOS", false)]
    [InlineData(true, "Android", false)]
    [InlineData(true, "iOS", false)]
    [InlineData(true, "FreeBSD", false)]
    public void StorageFollowsExportPlatformAndKeepsAllFilesUnderOneRoot(bool exported, string platform, bool portable)
    {
        string install = Path.Combine(Path.GetTempPath(), "san9-install");
        string user = Path.Combine(Path.GetTempPath(), "san9-user");
        var paths = new ClientDataPaths(exported, platform, Path.Combine(install, "San9"), user);
        string expected = portable ? Path.Combine(install, "userdata") : user;

        Assert.Equal(expected, paths.Root);
        Assert.Equal(Path.Combine(expected, "user-settings.json"), paths.Settings);
        Assert.Equal(Path.Combine(expected, "profile.json"), paths.Profile);
        Assert.Equal(Path.Combine(expected, "saves", "quick.save.json"), paths.QuickSave);
        Assert.Equal(Path.Combine(expected, "saves", "slots"), paths.SaveSlots);
        Assert.False(Directory.Exists(paths.Root));
    }
}
