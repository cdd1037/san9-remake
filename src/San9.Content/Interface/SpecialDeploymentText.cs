namespace San9.Content.Interface;

public static class SpecialDeploymentText
{
    public static IReadOnlyList<string> Roles { get; } = Array.AsReadOnly(new[] {
        "build", "transport", "officers", "soldiers", "facility", "need-officers", "need-soldiers",
        "need-facility", "no-facility", "next", "cancel", "policy", "site", "facility-catalog", "facility-rockfall", "facility-stone", "facility-range"
    });
    public static string Key(string role) => $"ui.special-deployment.{role}";
}
