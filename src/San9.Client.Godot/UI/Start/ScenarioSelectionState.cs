using San9.Content.Interface;

namespace San9.Client.Godot;

/// <summary>Owns the preview choice only; selecting a row never creates or replaces a game session.</summary>
public sealed class ScenarioSelectionState
{
    private readonly ScenarioSelectionPresentation _definition;
    public string CategoryId { get; private set; }
    public ScenarioPreviewEntry Selected { get; private set; }
    public IReadOnlyList<ScenarioPreviewEntry> Entries { get; private set; }
    public ScenarioSelectionState(ScenarioSelectionPresentation definition)
    {
        _definition = definition;
        CategoryId = definition.Categories[0].Id;
        Entries = InCategory(CategoryId);
        Selected = Entries[0];
    }
    private ScenarioPreviewEntry[] InCategory(string id) => _definition.Entries.Where(e => e.CategoryId == id).ToArray();
    public bool SelectCategory(string id)
    {
        if (id == CategoryId || !_definition.Categories.Any(c => c.Id == id)) return false;
        CategoryId = id; Entries = InCategory(id); Selected = Entries[0]; return true;
    }
    public bool Preview(string id)
    {
        var entry = Entries.FirstOrDefault(e => e.Id == id);
        if (entry is null || entry == Selected) return false;
        Selected = entry; return true;
    }
}
