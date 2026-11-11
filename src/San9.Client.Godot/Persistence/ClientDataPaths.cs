namespace San9.Client.Godot.Persistence;

/// <summary>Client-owned storage paths, selected once at startup independently of the working directory.</summary>
public sealed class ClientDataPaths
{
    public string Root { get; }
    public string Settings => Path.Combine(Root, "user-settings.json");
    public string UiPreferences => Path.Combine(Root, "ui-preferences.json");
    public string CompanionSettings => Path.Combine(Root, "companion-settings.json");
    public string Profile => Path.Combine(Root, "profile.json");
    public string PortraitPack => Path.Combine(Root, "portrait-packs", "my-portraits");
    public string TreasureIllustrations => Path.Combine(Root, "treasure-illustrations");
    public string CreatedOfficers => Path.Combine(Root, "created-officers.json");
    public string QuickSave => Path.Combine(Root, "saves", "quick.save.json");
    public string SaveSlots => Path.Combine(Root, "saves", "slots");

    public ClientDataPaths(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (!Path.IsPathFullyQualified(root)) throw new ArgumentException("An absolute filesystem path is required.", nameof(root));
        Root = Path.GetFullPath(root);
    }
}
