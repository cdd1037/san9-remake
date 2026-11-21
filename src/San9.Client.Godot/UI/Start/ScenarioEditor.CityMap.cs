using Godot;
using San9.Content.Interface;
using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Client.Godot;

public partial class ScenarioEditor
{
    public OpeningMapDefinition? Geography { get; init; }
    public TextureService? MapService { get; init; }
    private OpeningMapDefinition? _cityMap;
    private TextureScope? _cityTextures;
    private string? _mapError;
    private int? _selectedCity;

    private async Task LoadCityMap(ContentCatalog catalog, CancellationToken token)
    {
        try
        {
            _cityMap = Geography ?? (await PackageFiles.ReadAsync<OpeningContent>(
                PackageFiles.Resolve(ContentRoot, catalog.Opening), token)).Selection.Map;
            var service = MapService;
            if (service is null)
            {
                string root = PackageFiles.Resolve(ContentRoot, catalog.Textures);
                var textures = await Task.Run(() => TexturePackageFiles.LoadAsync(root, token), token);
                token.ThrowIfCancellationRequested();
                service = new TextureService();
                service.Initialize(textures, root);
                AddChild(service);
            }
            _cityTextures = service.CreateScope(service.LibraryAssets);
            _cityTextures.Request(_cityMap.Assets);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException or KeyNotFoundException)
        {
            _mapError = "地图资源不可用：" + error.Message;
            _cityTextures?.Dispose();
            _cityTextures = null;
        }
    }

    private void BuildCityOverview(MarginContainer body)
    {
        var workspace = _workspace!;
        var cities = workspace.Document.Settlements.Where(s => s.Kind == SettlementKind.City && !s.IsDormant).ToArray();
        if (cities.Length == 0)
        {
            ContentEditorControls.Caption(body, "当前剧本没有已启用的都市。");
            return;
        }
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 18);
        body.AddChild(row);
        var details = new VBoxContainer();
        details.AddThemeConstantOverride("separation", 8);
        var cityHeader = new HBoxContainer();
        details.AddChild(cityHeader);
        var picker = new OptionButton { Name = "OverviewCity", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var city in cities) picker.AddItem(workspace.Name(city.NameKey), city.Id);
        cityHeader.AddChild(picker);
        ContentEditorControls.ActionButton(cityHeader, "查看辖区", () =>
            BuildPages(_pages.GetNode<Control>("据点").GetIndex(), cities[picker.Selected].Id), "ViewCityDistrict");
        var summary = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        details.AddChild(summary);
        var count = new Label();
        details.AddChild(count);
        var roster = new ItemList { CustomMinimumSize = new(0, 105), SizeFlagsVertical = SizeFlags.ExpandFill };
        details.AddChild(roster);
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 8);
        details.AddChild(actions);
        var transfer = ContentEditorControls.ActionButton(actions, "移交据点", () =>
            TransferDialog(cities[picker.Selected]));
        var deploy = ContentEditorControls.ActionButton(actions, "在此安置武将", () =>
            DeploymentDialog(false, cities[picker.Selected].Id));
        transfer.SizeFlagsHorizontal = deploy.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        OpeningMapView? map = null;

        void SelectCity(int id)
        {
            int index = Array.FindIndex(cities, c => c.Id == id);
            if (index < 0) return;
            var city = cities[index];
            _selectedCity = id;
            picker.Select(index);
            if (map is not null) { map.Selected = new HashSet<int> { id }; map.QueueRedraw(); }
            var corps = workspace.Document.Information.Corps.SingleOrDefault(c => c.Id == city.CorpsId);
            string army = corps is null ? "无" : corps.IsRoot ? $"直属军团 [{corps.Id}]" : $"军团 [{corps.Id}]";
            summary.Text = $"归属　{(city.OwnerId is int owner ? ForceName(owner) : "无主")}\n军团　{army}\n兵力　{city.Soldiers:N0}";
            roster.Clear();
            foreach (var person in workspace.Document.Officers.Where(o => o.SettlementId == id && !o.IsDormant && o.Status != OfficerStatus.Dead)
                .OrderBy(o => o.Status).ThenBy(o => o.Id))
                roster.AddItem($"{workspace.OfficerName(person.Id)}　{OfficerStatusNames[(int)person.Status]}");
            count.Text = $"城内武将（{roster.ItemCount}）";
            deploy.Disabled = !city.CanHostPersons || !workspace.UnplacedOfficers.Any();
        }

        if (_cityMap is { } geography && _cityTextures is { } textures)
        {
            var viewport = new Control { ClipContents = true };
            row.AddChild(viewport);
            var colors = workspace.Document.Forces.ToDictionary(f => f.Id, f => $"color-{f.ColorCode}");
            var mapped = cities.Where(c => geography.Cities.ContainsKey(c.Id)).ToArray();
            map = new OpeningMapView { Definition = geography, Textures = textures, FocusOnReady = false,
                Targets = mapped.ToDictionary(c => c.Id, c => c.Id),
                CityColors = mapped.ToDictionary(c => c.Id, c => c.OwnerId is int owner && colors.TryGetValue(owner, out var color) &&
                    geography.Colors.ContainsKey(color) ? color : geography.NeutralColor), Activated = SelectCity };
            viewport.AddChild(map);
            void Fit()
            {
                float scale = Math.Min(viewport.Size.X / geography.Width, viewport.Size.Y / geography.Height);
                map.Scale = Vector2.One * scale;
                map.Position = (viewport.Size - new Vector2(geography.Width, geography.Height) * scale) / 2;
            }
            void SizeMapColumn()
            {
                float maxWidth = row.Size.X - 18 - Math.Clamp(row.Size.X * .32f, 230, 360);
                viewport.CustomMinimumSize = new(Math.Max(0, Math.Min(maxWidth, row.Size.Y * geography.Width / geography.Height)), 0);
            }
            row.Resized += SizeMapColumn;
            SizeMapColumn();
            viewport.Resized += Fit;
            Fit();
        }
        else if (_mapError is not null) ContentEditorControls.Caption(details, _mapError);
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", ContentEditorStyle.Surface("202833", "394758", 14));
        row.AddChild(panel);
        panel.AddChild(details);
        void SizeDetails() => panel.CustomMinimumSize = new(Math.Clamp(row.Size.X * .32f, 230, 360), 0);
        row.Resized += SizeDetails;
        SizeDetails();
        picker.ItemSelected += index => SelectCity(cities[(int)index].Id);
        SelectCity(_selectedCity is int current && cities.Any(c => c.Id == current) ? current : cities[0].Id);
    }
}
