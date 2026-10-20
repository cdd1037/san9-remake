namespace San9.Client.Godot.Persistence;

/// <summary>Client-owned storage paths, selected once at startup independently of the working directory.</summary>
public sealed class ClientDataPaths
{
    public string Root { get; }
    public string Settings => Path.Combine(Root, "user-settings.json");
    public string Profile => Path.Combine(Root, "profile.json");
    public string QuickSave => Path.Combine(Root, "saves", "quick.save.json");
    public string SaveSlots => Path.Combine(Root, "saves", "slots");

    public ClientDataPaths(bool isExportTemplate, string platform, string executablePath, string userDataDirectory)
    {
        Root = Path.GetFullPath(isExportTemplate && platform is "Windows" or "Linux"
            ? Path.Combine(Path.GetDirectoryName(executablePath)!, "userdata")
            : userDataDirectory);
    }
}
