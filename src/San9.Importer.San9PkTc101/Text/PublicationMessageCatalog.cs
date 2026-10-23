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
        var reviewed = ReviewedGroups();
        for (int id = 0; id < 14209; id++)
        {
            string group = reviewed.TryGetValue(id, out string? semanticGroup) ? semanticGroup : id switch
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
    private static Dictionary<int, string> ReviewedGroups()
    {
        Dictionary<int, string> groups = [];
        void Add(string category, IEnumerable<int> ids)
        {
            foreach (int id in ids) groups.Add(id, category);
        }
        // These are the source bindings already consumed by ScenarioTextReader and
        // scenario preview compilation. Unused columns remain in the unclassified file.
        Add("scenario-catalog", Enumerable.Range(0, 40));
        Add("person-names", Enumerable.Range(0, 700).SelectMany(id => new[] { 1678 + 4 * id, 1679 + 4 * id, 1680 + 4 * id }));
        Add("tactics", Enumerable.Range(0, San9ScenarioTacticArchive.RecordCount).SelectMany(id => new[] { 1219 + 3 * id, 1221 + 3 * id }));
        Add("formations", Enumerable.Range(0, San9ScenarioFormationArchive.RecordCount).SelectMany(id => new[] { 5363 + 3 * id, 5365 + 3 * id }));
        Add("offices", Enumerable.Range(0, San9ScenarioOfficeArchive.AssignableOfficeCount).SelectMany(id => new[] { 5065 + 2 * id, 7770 + id }));
        Add("ranks", Enumerable.Range(0, San9ScenarioRankArchive.RecordCount).Select(id => 1652 + 2 * id)
            .Concat(Enumerable.Range(0, San9ScenarioRankArchive.RecordCount - 1).Select(id => 7761 + id)));
        Add("geography", Enumerable.Range(0, San9ScenarioAreaArchive.RecordCount).SelectMany(id =>
            new[] { San9ScenarioAreaArchive.GetNameMessageId(id), San9ScenarioAreaArchive.GetNameReadingMessageId(id) }));
        Add("country-titles", Enumerable.Range(0, San9ScenarioCountryTitleArchive.RecordCount).SelectMany(id =>
            new[] { San9ScenarioCountryTitleArchive.GetNameMessageId(id), San9ScenarioCountryTitleArchive.GetNameReadingMessageId(id) }));
        return groups;
    }

}
