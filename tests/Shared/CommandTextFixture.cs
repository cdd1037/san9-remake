using San9.Content.Text;

namespace San9.Tests;

internal static class CommandTextFixture
{
    internal static Dictionary<string, string> Entries(IEnumerable<KeyValuePair<string, string>>? other = null)
    {
        Dictionary<string, string> entries = [];
        CommandText.AddRemakeEntries(entries);
        foreach (var (role, count) in CommandText.Parameters)
            entries.TryAdd(CommandText.Key(role), role + string.Concat(Enumerable.Range(0, count).Select(i => " {" + i + "}")));
        foreach (var (key, value) in other ?? []) entries.Add(key, value);
        return entries;
    }
}
