using System.Collections.ObjectModel;
using System.Globalization;
using San9.Content.Text;

namespace San9.Importer.San9PkTc101.Text;

/// <summary>TC101 archive coverage and confirmed file groups; message identities retain source ordinals.</summary>
public static class PublicationMessageCatalog
{
    public static IReadOnlyDictionary<string, string> Groups { get; } = BuildGroups();

    public static Dictionary<string, MessageDefinition> Compile(IReadOnlyDictionary<string, MessageDefinition> source)
    {
        if (source.Count != Groups.Count || source.Keys.Any(key => !Groups.ContainsKey(key)))
            throw new InvalidDataException("Message archive coverage differs from the reviewed publication catalog.");
        MessageGraph.Validate(source);
        // Archive order is explicit, independent of input enumeration and file grouping.
        return Groups.Keys.ToDictionary(key => key, key => source[key], StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, string> BuildGroups()
    {
        Dictionary<string, string> groups = new(StringComparer.Ordinal);
        for (int id = 0; id < 14209; id++)
        {
            string group = id switch
            {
                >= 72 and <= 135 or >= 12923 and <= 14208 => "campaign",
                >= 8577 and <= 9323 => "help",
                >= 9422 and <= 9904 or >= 10122 and <= 10139 => "tutorial",
                >= 11455 and <= 12612 => "names",
                _ => "messages"
            };
            groups.Add($"messages.{id.ToString(CultureInfo.InvariantCulture)}", group);
        }
        for (int id = 0; id < 679; id++)
            groups.Add($"biographies.{id.ToString(CultureInfo.InvariantCulture)}", "biographies");
        return new ReadOnlyDictionary<string, string>(groups);
    }
}
