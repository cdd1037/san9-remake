namespace San9.Client.Godot.Settings;

/// <summary>One client lifetime; publish preferences only after durable storage succeeds.</summary>
public sealed class UserSettingsSession(UserSettingsStore store)
{
    public UserSettings Current { get; private set; } = UserSettings.Default;
    public void Load() => Current = store.Load();
    public void Apply(UserSettings draft)
    {
        store.Save(draft);
        Current = draft;
    }
}
