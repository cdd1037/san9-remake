namespace San9.Content.Interface;

public static class AppointmentText
{
    public static IReadOnlyList<string> Roles { get; } = Array.AsReadOnly(new[] { "select", "target", "accept", "cancel" });
    public static string Key(string kind, string role) => $"ui.{kind}.{role}";
}
