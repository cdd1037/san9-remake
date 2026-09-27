namespace San9.Content.Interface;

public static class ExecutionPanelSkin
{
    public static IReadOnlyList<string> Columns { get; } = Array.AsReadOnly(new[] { "officer", "command", "target", "remaining" });
    public static string HeaderKey(string column) => $"ui.execution-panel.{column}";
}
