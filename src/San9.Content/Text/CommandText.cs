using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace San9.Content.Text;

/// <summary>Command interface text contract. Content authors supply every role; consumers never fall back to built-in wording.</summary>
public static class CommandText
{
    public static IReadOnlyDictionary<string, int> Parameters { get; } = Read<int>("CommandText.json").ToFrozenDictionary();
    public static string Key(string role) => "ui.commands." + role;

    public static string Resolve(TextCatalog texts, string role, params object?[] values)
    {
        if (!Parameters.TryGetValue(role, out int count) || values.Length != count)
            throw new ArgumentException($"Invalid command text role or parameters: {role}.");
        string template = texts.Resolve(Key(role));
        return string.Format(CultureInfo.InvariantCulture, template, values);
    }

    public static void Validate(TextCatalog texts)
    {
        foreach (var (role, count) in Parameters)
        {
            string template = texts.Resolve(Key(role));
            try
            {
                var format = CompositeFormat.Parse(template);
                var used = new bool[count];
                if (format.MinimumArgumentCount != count)
                    throw new InvalidDataException($"Invalid command text parameters: {role}.");
                _ = string.Format(CultureInfo.InvariantCulture, format,
                    Enumerable.Range(0, count).Select(i => (object)new ParameterProbe(i, used)).ToArray());
                if (used.Any(value => !value)) throw new InvalidDataException($"Missing command text parameter: {role}.");
            }
            catch (FormatException error) { throw new InvalidDataException($"Invalid command text format: {role}.", error); }
        }
    }

    /// <summary>Explicit authoring input for remake-only notices and factual summaries; never called by runtime consumers.</summary>
    public static void AddRemakeEntries(IDictionary<string, string> entries)
    {
        foreach (var (role, text) in Read<string>("RemakeCommandText.json")) entries.Add(Key(role), text);
    }

    private static Dictionary<string, T> Read<T>(string name)
    {
        using var stream = typeof(CommandText).Assembly.GetManifestResourceStream("San9.Content.Text." + name)
            ?? throw new InvalidOperationException($"Missing command text contract: {name}.");
        return JsonSerializer.Deserialize<Dictionary<string, T>>(stream)!;
    }

    private sealed class ParameterProbe(int index, bool[] used) : IFormattable
    {
        public string ToString(string? format, IFormatProvider? provider) { used[index] = true; return ""; }
    }
}
