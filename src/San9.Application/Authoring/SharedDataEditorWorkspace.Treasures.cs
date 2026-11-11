using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Application.Authoring;

public sealed partial class SharedDataEditorWorkspace
{
    private readonly Dictionary<int, TreasureDefinition> _treasureBaselines;
    public IReadOnlyList<TreasureDefinition> Treasures => Definitions.Information.Treasures;
    public TreasureDefinition GetTreasure(int id) => Treasures.Single(t => t.Id == id);
    public bool IsTreasureChanged(int id) => !ContentComparison.Equal(GetTreasure(id), _saved.Information.Treasures.SingleOrDefault(t => t.Id == id));

    public void SetTreasure(TreasureDefinition treasure)
    {
        ContentStructure.Validate(treasure);
        var treasures = Treasures.ToArray();
        int index = Array.FindIndex(treasures, t => t.Id == treasure.Id);
        if (index < 0) throw new InvalidDataException("只能修改现有宝物，不能改变宝物身份。");
        treasures[index] = treasure;
        Definitions = Definitions with { Information = Definitions.Information with { Treasures = treasures } };
    }

    public void RestoreTreasure(int id) => SetTreasure(_treasureBaselines[id]);

    public async Task<TreasureDefinition> CreateTreasureAsync(int? copyFrom = null, CancellationToken token = default)
    {
        var draft = Definitions;
        var source = copyFrom is int original ? GetTreasure(original) : null;
        int id = await AllocateDefinitionIdentityAsync(DefinitionKind.Treasure, token);
        EnsureDefinitionDraft(draft, token);
        var treasure = source is not null ? source with { Id = id, Name = source.Name + "副本" }
            : new TreasureDefinition(id, "新宝物", 11, 0, null);
        Definitions = draft with
        {
            NextTreasureId = (long)id + 1,
            Information = draft.Information with { Treasures = [.. draft.Information.Treasures, treasure] }
        };
        _treasureBaselines.Add(id, treasure);
        return treasure;
    }

    public async Task DeleteTreasureAsync(int id, CancellationToken token = default)
    {
        if (!Treasures.Any(t => t.Id == id)) throw new InvalidDataException("找不到要删除的宝物。");
        var draft = Definitions;
        var candidate = draft with
        {
            Information = draft.Information with { Treasures = draft.Information.Treasures.Where(t => t.Id != id).ToArray() }
        };
        await ValidateDefinitionDeletionAsync(draft, candidate, "宝物", token);
        _treasureBaselines.Remove(id);
    }

    private static IEnumerable<int> TreasureReferenceIds(ScenarioDefinitions definitions)
    {
        IEnumerable<int> eventIds = definitions.SearchEvents.SelectMany<SearchEventDefinition, int>(search => search switch
        {
            MysticBookOfferSearchEventDefinition offer => [offer.TreasureId],
            MysticBookReturnSearchEventDefinition bookReturn => [bookReturn.TreasureId],
            PhysicianVisitSearchEventDefinition physician => physician.Sites.Select(s => s.TreasureId),
            CommemorationSearchEventDefinition commemoration => commemoration.Sites.Select(s => s.RequiredTreasureId).OfType<int>(),
            TreasureMonumentSearchEventDefinition monument => [monument.TreasureId],
            _ => []
        });
        return eventIds.Concat(definitions.Ending?.CulturalTreasureIds ?? [])
            .Concat(definitions.ImperialSealId is int seal ? [seal] : []);
    }
}
