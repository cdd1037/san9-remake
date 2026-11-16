using System.Text.Json.Serialization;
using San9.Core.Domain;

namespace San9.Content.Scenarios;

public sealed record CountryTitleDefinition(
    [property: JsonRequired] CountryTitleRule Rule,
    [property: JsonRequired] string NameKey,
    [property: JsonRequired] int FlagGlyph)
{
    [JsonRequired] public string Description { get; init; } = "";
    [JsonIgnore] public int Id => Rule.Id;
}
