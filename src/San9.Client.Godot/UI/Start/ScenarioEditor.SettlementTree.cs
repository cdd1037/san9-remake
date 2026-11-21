using Godot;
using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Client.Godot;

public partial class ScenarioEditor
{
    private void BuildSettlementPage(int? selectedId)
    {
        var (side, search, form) = BuildBrowser(_pages, "据点", 320, scrollForm: true);
        var workspace = _workspace!;
        var doc = workspace.Document;
        var tree = new Tree { Name = "据点列表", HideRoot = true, Columns = 3, ColumnTitlesVisible = true,
            SelectMode = Tree.SelectModeEnum.Row, SizeFlagsVertical = SizeFlags.ExpandFill };
        side.AddChild(tree);
        tree.SetColumnTitle(0, "据点");
        tree.SetColumnTitle(1, "类型");
        tree.SetColumnTitle(2, "势力");
        tree.SetColumnExpandRatio(0, 2);
        var root = tree.CreateItem();
        var rows = new Dictionary<int, TreeItem>();
        var typeNames = workspace.Definitions.FacilityNameSuffixKeys;
        var places = doc.MilitaryPlaces.ToDictionary(p => p.Id);
        TreeItem Add(TreeItem parent, SettlementInitialState place)
        {
            var row = tree.CreateItem(parent);
            rows.Add(place.Id, row);
            row.SetMetadata(0, place.Id);
            string name = workspace.Name(place.NameKey);
            row.SetText(0, name);
            row.SetTooltipText(0, $"{name} [{place.Id}]" + (place.IsDormant ? "（休眠）" : ""));
            row.SetText(1, place.Kind == SettlementKind.City ? "都市" :
                places.GetValueOrDefault(place.Id)?.FacilityTypeId is int type && typeNames.TryGetValue(type, out var key)
                    ? workspace.Name(key) : "设施");
            row.SetText(2, place.OwnerId is int owner ? ForceName(owner) : "无主");
            return row;
        }
        var cities = doc.Settlements.Where(s => s.Kind == SettlementKind.City).ToDictionary(s => s.Id, s => Add(root, s));
        var facilities = doc.Settlements.Where(s => s.Kind != SettlementKind.City);
        foreach (var group in facilities.GroupBy(s => workspace.SettlementCity(s) is int city ? cities.GetValueOrDefault(city) : null))
        {
            var parent = group.Key;
            if (parent is null)
            {
                parent = tree.CreateItem(root);
                parent.SetText(0, "其他据点");
                for (int c = 0; c < tree.Columns; c++) parent.SetSelectable(c, false);
            }
            foreach (var place in group) Add(parent, place);
        }
        tree.ItemSelected += () =>
        {
            selectedId = tree.GetSelected().GetMetadata(0).AsInt32();
            ContentEditorControls.ClearChildren(form);
            BuildSettlement(form, selectedId.Value);
        };
        void Filter()
        {
            string query = search.Text.Trim();
            var matches = rows.Where(p => p.Value.GetText(0).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.Key.ToString().Contains(query, StringComparison.Ordinal)).Select(p => p.Value).ToHashSet();
            foreach (var group in root.GetChildren())
            {
                bool groupMatch = matches.Contains(group);
                bool visible = groupMatch;
                foreach (var child in group.GetChildren())
                {
                    child.Visible = groupMatch || matches.Contains(child);
                    visible |= child.Visible;
                }
                group.Visible = visible;
                group.Collapsed = query.Length == 0;
            }
            var target = selectedId is int id && rows.TryGetValue(id, out var current) && matches.Contains(current)
                ? current : rows.Values.FirstOrDefault(matches.Contains);
            if (target is null) { tree.DeselectAll(); ContentEditorControls.ClearChildren(form); return; }
            if (target.GetParent() != root) target.GetParent().Collapsed = false;
            target.Select(0);
            tree.ScrollToItem(target);
        }
        search.TextChanged += _ => Filter();
        var requestedCity = selectedId is int requested ? cities.GetValueOrDefault(requested) : null;
        Filter();
        if (requestedCity is not null) requestedCity.Collapsed = false;
    }
}
