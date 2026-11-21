using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Content.Text;

namespace San9.Content.Scenarios;

public sealed record SessionForceDefinition(int Id, int FounderId);

/// <summary>References for opening deployments outside the scenario's initial roster.</summary>
public sealed record SessionDefinitions
{
    [JsonRequired] public ImmutableArray<int> OfficerIds { get; init; } = [];
    [JsonRequired] public ImmutableArray<SessionForceDefinition> Forces { get; init; } = [];
    [JsonIgnore] public ImmutableArray<OfficerDefaults> Officers { get; private init; } = [];
    public static SessionDefinitions Empty { get; } = new();

    public void Validate()
    {
        if (OfficerIds.IsDefault || Forces.IsDefault || OfficerIds.Any(id => id < 0) ||
            OfficerIds.Distinct().Count() != OfficerIds.Length ||
            Forces.Any(f => f is null || f.Id < 0 || !OfficerIds.Contains(f.FounderId)) ||
            Forces.Select(f => f.Id).Distinct().Count() != Forces.Length)
            throw new InvalidDataException("Invalid session definitions.");
    }

    public void Validate(ScenarioInitialState source)
    {
        Validate();
        if (OfficerIds.Any(id => source.Officers.Any(o => o.State.Id == id)) || Forces.Any(f => source.Forces.Any(o => o.Id == f.Id)))
            throw new InvalidDataException("Session definitions collide with base content.");
    }

    public SessionDefinitions Resolve(ScenarioInitialState source)
    {
        Validate(source);
        return this with { Officers = OfficerIds.Select(id => source.OfficerCatalog.TryGetValue(id, out var profile)
            ? profile : throw new MissingOfficerDefinitionException(id)).ToImmutableArray() };
    }

    public TextCatalog AddText(TextCatalog source) => OfficerComposition.ProjectText(source, Officers);

    public Dictionary<string, string> AddPortraits(IReadOnlyDictionary<string, string> source) =>
        OfficerComposition.ProjectPortraits(source, Officers);
}

public sealed class MissingOfficerDefinitionException(int id) : Exception($"当前游戏资料缺少武将 ID {id}。");
