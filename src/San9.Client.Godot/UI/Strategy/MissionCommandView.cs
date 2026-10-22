using San9.Content.Text;
using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Client.Godot;

public partial class MissionCommandView : CommandPreparationView
{
    public MissionDraft Draft { get; init; } = null!;
    public GameSession Session { get; init; } = null!;
    public MapWorld World { get; init; } = null!;
    public Action SelectionChanged { get; init; } = null!;
    protected override bool IsCurrent => Draft.IsCurrent;
    // Original content rectangles and edit/card anchors, verified in RE-0182.
    private Rect2 Bounds => Draft.Kind switch
    {
        "false-report" or "disorder" => new(248, 168, 528, 432),
        "inspire" or "rescue" => new(276, 164, 472, 440),
        "alienate" => new(288, 236, 448, 296), "burn" => new(264, 264, 496, 240),
        "seize" => new(264, 248, 496, 272), "rumor" => new(292, 248, 440, 272),
        "gift" => new(304, 220, 416, 328), "request" => new(304, 240, 416, 288),
        "return" => new(264, 236, 496, 296), _ => new(304, 256, 416, 256)
    };
    private string TargetName(MissionTargetKind kind, int? id)
    {
        if (id is not int value) return "";
        return kind switch
        {
            MissionTargetKind.Officer => Texts.Resolve(Draft.View.Officers.Single(p => p.Id == value).NameKey),
            MissionTargetKind.Settlement => Draft.View.Settlements.Single(p => p.Id == value).ResolveName(Texts),
            MissionTargetKind.Unit => new InformationQuery(Session, Texts).Read(InformationCategory.Units).Rows.Single(r => r.Key.Id == value).Cells[0].Text,
            _ => Texts.Resolve(Draft.View.Forces.Single(p => p.Id == value).NameKey)
        };
    }
    protected override void Build()
    {
        var bounds = Bounds; var at = bounds.Position;
        var window = new CommandWindow { Texts = Texts, Textures = Textures, Title = CommandText.Resolve(Texts, "title." + Draft.Kind), Position = at, Size = bounds.Size,
            CanAccept = Draft.CanExecute, Accepted = Submit, Closed = Closed,
            Help = () => Notice(Texts.Resolve(CommandMenuSkin.DescriptionKey(Draft.Kind))) };
        Body.AddChild(window);
        string source = CommandText.Resolve(Texts, "value.source", Draft.View.Settlements.Single(p => p.Id == Draft.Home.Id).ResolveName(Texts));
        float sourceWidth = source.Sum(c => c < 128 ? 8 : 16) + 32;
        Body.AddChild(new MissionCommandFields { Textures = Textures, Bounds = bounds, SourceWidth = sourceWidth,
            DividerWidth = Draft.IsDiplomacy ? 416 : bounds.Size.X });
        Label(source, at, new(sourceWidth,32));
        int arrivalX = Draft.Kind switch { "alienate" or "gift" or "request" or "surrender" => 256, "rumor" => 280,
            "burn" or "return" or "seize" => 336, _ => (int)bounds.Size.X - 152 };
        int arrivalWidth = Draft.Kind is "alienate" or "burn" or "seize" or "rumor" ? 152 : 136;
        Body.AddChild(new CommandArrivalField { Textures = Textures, Position = at + new Vector2(arrivalX,0), Size = new(arrivalWidth,32),
            Text = Draft.Kind is ("gift" or "return") && Draft.TargetId is null ? "" :
                CommandArrivalField.FormatDays(Texts, Draft.Preview.Arrival is {} arrival ? arrival.DayNumber - Draft.View.World.Clock.DayNumber : null) });
        int actorY = Draft.Kind switch { "false-report" or "disorder" => 312, "inspire" or "rescue" => 320,
            "alienate" or "return" => 176, "seize" => 152, "burn" => 120, "rumor" => 160,
            "gift" => 88, _ => 128 };
        window.ActionButton(Draft.Kind == "return" ? CommandText.Resolve(Texts, "label.prisoner") : Draft.TargetKind == MissionTargetKind.Unit ? CommandText.Resolve(Texts, "label.target-unit") : Draft.TargetKind == MissionTargetKind.Officer ? CommandText.Resolve(Texts, "label.target-officer") :
            Draft.TargetKind == MissionTargetKind.Settlement ? CommandText.Resolve(Texts, "label.target-facility") : CommandText.Resolve(Texts, "label.recipient-force"), "edit", new(0,48,104,40),
            () => { if (Draft.Kind == "return") Select(InformationCategory.Officers, Draft.Prisoners, Draft.SecondaryId, id => { Draft.SetPrisoner(id); Changed(); }, officerTargets: true); else SelectTarget(false); });
        if (Draft.Kind == "alienate")
        {
            Person(Draft.View, Draft.TargetId, at + new Vector2(104,48));
            var target = Draft.View.World.Officers.SingleOrDefault(p => p.Id == Draft.TargetId);
            Properties(at + new Vector2(312,48), new(CommandText.Resolve(Texts, "label.force"), target?.OwnerId is int owner ? TargetName(MissionTargetKind.Force,owner) : "",80),
                new(CommandText.Resolve(Texts, "label.loyalty"), target is null ? "" : Math.Min(100,target.Loyalty).ToString(),40),
                new(CommandText.Resolve(Texts, "label.intelligence"), target is null ? "" : Draft.View.Officers.Single(p => p.Id == target.Id).Abilities.Intelligence.ToString(),40));
        }
        else if (Draft.Kind == "return")
        {
            // The original first selector is the prisoner; its captor determines the receiving force.
            Person(Draft.View, Draft.SecondaryId, at + new Vector2(104,48));
            WideProperties(at+new Vector2(312,48),80,new(CommandText.Resolve(Texts, "label.recipient-force"),TargetName(MissionTargetKind.Force,Draft.TargetId),80),
                new(CommandText.Resolve(Texts, "label.relation"),Relation(Draft.TargetId),48));
        }
        else
        {
            int targetWidth = Draft.TargetKind switch { MissionTargetKind.Unit => 96, MissionTargetKind.Force => 80, _ => 144 };
            Value(TargetName(Draft.TargetKind,Draft.TargetId),at+new Vector2(104,48),new(targetWidth,32));
            if (Draft.TargetKind == MissionTargetKind.Unit)
            {
                var unit = Draft.View.World.Units.SingleOrDefault(u => u.Id == Draft.TargetId);
                if (unit is not null && Draft.TargetRegionId is int mapRegion)
                {
                    var preview = World.CreateInformationPreview(mapRegion,Texts,unit.Id);
                    preview.Position = at+new Vector2(104,88); preview.Size = new(248,216); Body.AddChild(preview);
                }
                if (Draft.Kind is "false-report" or "disorder")
                    WideProperties(at+new Vector2(360,56),80,new(CommandText.Resolve(Texts, "label.force"),TargetName(MissionTargetKind.Force,unit?.OwnerId),80),
                        new(CommandText.Resolve(Texts, "label.soldiers"),unit?.Soldiers.ToString() ?? "",56),new(CommandText.Resolve(Texts, "label.morale"),unit?.Morale.ToString() ?? "",40),
                        new(Draft.Kind == "false-report" ? CommandText.Resolve(Texts, "label.max-intelligence") : CommandText.Resolve(Texts, "label.commander-leadership"),unit is null ? "" : Draft.Kind == "false-report" ?
                            Draft.View.Officers.Where(o => o.UnitId == unit.Id && o.IsServing).Select(o => o.Abilities.Intelligence).DefaultIfEmpty(0).Max().ToString() :
                            Ability(unit.CommanderId,leadership:true),40));
                else
                    Properties(at+new Vector2(360,88),new(CommandText.Resolve(Texts, "label.soldiers"),unit?.Soldiers.ToString() ?? "",56),new(CommandText.Resolve(Texts, "label.morale"),unit?.Morale.ToString() ?? "",40),
                        new(CommandText.Resolve(Texts, "label.state"),unit is null ? "" : new InformationQuery(Session,Texts).Read(InformationCategory.Units,1).Rows.Single(r => r.Key.Id == unit.Id).Cells[4].Text,48));
            }
            if (Draft.TargetKind == MissionTargetKind.Settlement)
            {
                var place = Draft.View.World.Settlements.SingleOrDefault(p => p.Id == Draft.TargetId);
                int? commander = place is null ? null : new AdministrativeCatalog(Draft.View,Texts).Commander(place.Id)?.Id;
                List<OfficerProperty> properties = [new(CommandText.Resolve(Texts, "label.force"),TargetName(MissionTargetKind.Force,place?.OwnerId),80),new(CommandText.Resolve(Texts, "label.commander-leadership"),Ability(commander,leadership:true),40)];
                if (Draft.Kind == "seize") properties.Add(new(CommandText.Resolve(Texts, "label.soldiers"),place?.Soldiers.ToString() ?? "",64));
                SpacedProperties(at+new Vector2(312,56),80,24,properties.ToArray());
            }
            if (Draft.TargetKind == MissionTargetKind.Force)
            {
                var force = Draft.View.World.Forces.SingleOrDefault(f => f.Id == Draft.TargetId);
                if (Draft.Kind == "rumor") WideProperties(at+new Vector2(312,52),80,new(CommandText.Resolve(Texts, "label.ruler-intelligence"),Ability(force?.LeaderId),40),new(CommandText.Resolve(Texts, "label.adviser-intelligence"),Ability(force?.AdviserId),40));
                else if (Draft.Kind == "request")
                {
                    WideProperties(at+new Vector2(208,52),80,new OfficerProperty(CommandText.Resolve(Texts, "label.relation"),Relation(Draft.TargetId),48));
                    int? owner = Draft.View.World.Settlements.SingleOrDefault(p => p.Id == Draft.SecondaryId)?.OwnerId;
                    WideProperties(at+new Vector2(208,92),80,new OfficerProperty(CommandText.Resolve(Texts, "label.target-force"),TargetName(MissionTargetKind.Force,owner),80));
                }
                else if (Draft.Kind == "surrender")
                {
                    Caption(CommandText.Resolve(Texts, "label.actor-force"),at+new Vector2(0,88),new(96,32));
                    Value(TargetName(MissionTargetKind.Force,Draft.View.PlayerForceId),at+new Vector2(104,88),new(80,32));
                    Properties(at+new Vector2(192,52),new OfficerProperty(CommandText.Resolve(Texts, "label.trust"),force?.Trust.ToString() ?? "",48));
                    Properties(at+new Vector2(192,92),new OfficerProperty(CommandText.Resolve(Texts, "label.trust"),Draft.View.World.Forces.Single(f => f.Id == Draft.View.PlayerForceId).Trust.ToString(),48));
                }
                else Properties(at+new Vector2(192,52),new OfficerProperty(CommandText.Resolve(Texts, "label.relation"),Relation(Draft.TargetId),48));
            }
            if (Draft.Kind is "rumor" or "request")
            {
                window.ActionButton(Draft.Kind == "rumor" ? CommandText.Resolve(Texts, "label.target-force") : CommandText.Resolve(Texts, "label.target-facility"), "edit", new(0,Draft.Kind == "rumor" ? 112 : 88,104,40), () => SelectTarget(true));
                Value(TargetName(Draft.SecondaryKind,Draft.SecondaryId),at+new Vector2(104,Draft.Kind == "rumor" ? 112 : 88),new(Draft.Kind == "rumor" ? 80 : 96,32));
            }
        }
        window.ActionButton(CommandText.Resolve(Texts, "label.actor"), "edit", new(0,actorY,104,40), SelectActor);
        Person(Draft.View,Draft.ActorId,at+new Vector2(104,actorY));
        SpacedProperties(at+new Vector2(Draft.TargetKind == MissionTargetKind.Unit ? 360 : 312,actorY),
            Draft.Kind is "false-report" or "disorder" or "rumor" or "return" or "burn" or "seize" ? 80 : 48,
            Draft.Kind is "burn" or "seize" ? 24 : 8,
            new OfficerProperty(Draft.IsDiplomacy ? CommandText.Resolve(Texts, "label.politics") : Draft.Kind is "burn" or "seize" ? CommandText.Resolve(Texts, "label.war") : Draft.Kind == "rescue" ? CommandText.Resolve(Texts, "label.leadership") : CommandText.Resolve(Texts, "label.intelligence"),
            Draft.ActorId is int actor ? (Draft.IsDiplomacy ? Draft.View.Officers.Single(p => p.Id == actor).Abilities.Politics :
                Draft.Kind is "burn" or "seize" ? Draft.View.Officers.Single(p => p.Id == actor).Abilities.War :
                Draft.Kind == "rescue" ? Draft.View.Officers.Single(p => p.Id == actor).Abilities.Leadership :
                Draft.View.Officers.Single(p => p.Id == actor).Abilities.Intelligence).ToString() : "",40));
        if (Draft.Kind == "inspire")
        {
            Properties(at+new Vector2(360,352),new OfficerProperty(CommandText.Resolve(Texts, "label.inspiration"),Draft.ActorKnowsInspiration is null ? "" : Draft.ActorKnowsInspiration == true ? "○" : "×",32));
        }
        if (Draft.Kind == "gift") BuildGift(window,at);
        if (Draft.Kind == "request")
        {
            Caption(CommandText.Resolve(Texts,"label.request-period"),at+new Vector2(0,256),new(96,32));
            Value(CommandText.Resolve(Texts,"value.days",180),at+new Vector2(104,256),new(56,32));
        }
        Body.AddChild(new GameMessageBar { Textures = Textures, Text = Draft.ActorId is null ? CommandText.Resolve(Texts, "failure.no-actor") :
            Texts.Resolve(CommandMenuSkin.HintKey(Draft.Kind)) });
    }
    private string Ability(int? id, bool leadership = false) => Draft.View.Officers.SingleOrDefault(o => o.Id == id) is {} person ?
        (leadership ? person.Abilities.Leadership : person.Abilities.Intelligence).ToString() : "";
    private string Relation(int? id) => id is int force ? new AdministrativeCatalog(Draft.View,Texts).Cell(InformationCategory.Forces,force,"relation").Text : "";
    private void Changed() { CloseChild(); SelectionChanged(); }
    private void SelectActor() => Select(InformationCategory.Officers, Draft.Actors, Draft.ActorId, id => { Draft.SetActor(id); Changed(); });
    private void SelectTarget(bool secondary)
    {
        var kind = secondary ? Draft.SecondaryKind : Draft.TargetKind;
        var ids = secondary ? Draft.SecondaryTargets : Draft.Targets;
        int? current = secondary ? Draft.SecondaryId : Draft.TargetId;
        void Selected(int id) { Draft.SetTarget(id,secondary); Changed(); }
        if (kind == MissionTargetKind.Officer)
        { Select(InformationCategory.Officers,ids,current,Selected,officerTargets:true); return; }
        if (kind == MissionTargetKind.Force)
        { SelectAdministrative(Session,Draft.View,InformationCategory.Forces,ids,CommandText.Resolve(Texts, "select.force"),Selected,current:current); return; }
        if (ids.IsEmpty) { Notice(CommandText.Resolve(Texts, "failure.no-target")); return; }
        var category = kind == MissionTargetKind.Unit ? InformationCategory.Units : InformationCategory.Regions;
        var facilities = kind == MissionTargetKind.Settlement ? new CommandFacilityCatalog(Draft.View,Texts,Draft.Kind,ids,Draft.ArrivalDaysToSettlement) : null;
        var pages = kind == MissionTargetKind.Unit ? UnitCatalog.Pages : facilities!.Pages;
        string title = kind == MissionTargetKind.Unit ? CommandText.Resolve(Texts, "select.unit") : CommandText.Resolve(Texts, "select.facility");
        var query = new InformationQuery(Session,Texts);
        SelectMapTargets(new(Draft.View,kind == MissionTargetKind.Unit ? CommandMapTargetKind.Unit : CommandMapTargetKind.Settlement,
            ids,Draft.MapMarker(secondary)),title,pages,
            page => facilities?.Read(page) ?? query.Read(category,page).Rows.Where(r => ids.Contains(r.Key.Id))
                .Select(r => new CatalogRow(r.Key.Id,r.Cells.ToArray())).ToArray(),Selected,CloseChild,current);
    }
    private void Select(InformationCategory category, IEnumerable<int> candidates, int? current, Action<int> selected, bool officerTargets = false)
    {
        var ids = candidates.ToHashSet();
        var query = new InformationQuery(Session,Texts);
        IReadOnlyList<CatalogPage> pages = category switch { InformationCategory.Officers => MissionOfficerCatalog.Pages(Draft,Texts,officerTargets),
            InformationCategory.Units => UnitCatalog.Pages, InformationCategory.Treasures => TreasureCatalog.Pages,
            _ => AdministrativeCatalog.Pages(category) };
        if (ids.Count == 0) { Notice(CommandText.Resolve(Texts, "failure.no-mission-target")); return; }
        ReplaceChild(new CatalogView { Title = CommandText.Resolve(Texts, category switch { InformationCategory.Officers => "select.officer",
                InformationCategory.Treasures => "select.treasure", InformationCategory.Units => "select.unit",
                _ => throw new InvalidOperationException("Unsupported mission selection category.") }), CloseTitle = CommandText.Resolve(Texts, "action.cancel"), Service = Service, Textures = Textures,
            HintText = category == InformationCategory.Officers && !officerTargets ? Texts.Resolve(CommandMenuSkin.OfficerSelectionHintKey) : CommandText.Resolve(Texts, "hint.object"),
            Pages = pages, ReadRows = page => category == InformationCategory.Officers ? MissionOfficerCatalog.Read(Draft,Texts,page,officerTargets) :
                query.Read(category,page).Rows.Where(r => ids.Contains(r.Key.Id)).Select(r => new CatalogRow(r.Key.Id,r.Cells.ToArray())).ToArray(),
            Portrait = category == InformationCategory.Officers ? id => $"catalog-person-{id}" : null,
            InitialCurrentId = current, Activated = selected, Closed = CloseChild });
    }
    private void BuildGift(CommandWindow window, Vector2 at)
    {
        var root = Draft.View.World.Corps.Single(c => c.OwnerId == Draft.View.PlayerForceId && c.IsRoot);
        window.ActionButton(CommandText.Resolve(Texts, "label.money"), "edit", new(0,216,104,40), () => Number(GiftKind.Money,root.Treasury.Money));
        window.ActionButton(CommandText.Resolve(Texts, "label.food"), "edit", new(0,256,104,40), () => Number(GiftKind.Food,root.Treasury.Food));
        window.ActionButton(CommandText.Resolve(Texts, "label.treasure"), "edit", new(0,296,104,40), () => Select(InformationCategory.Treasures,Draft.Treasures,Draft.Gift?.TreasureId,
            id => { Draft.SetGift(new(GiftKind.Treasure,0,id)); Changed(); }));
        Value(Amount(GiftKind.Money,root.Treasury.Money),at+new Vector2(104,216),new(120,32));
        Value(Amount(GiftKind.Food,root.Treasury.Food),at+new Vector2(104,256),new(128,32));
        Value(Draft.Gift?.TreasureId is int item ? Texts.Resolve(Draft.View.Information.Treasures.Single(t => t.Id == item).NameKey) : "",at+new Vector2(104,296),new(112,32));
        string Amount(GiftKind kind, uint available) => Draft.Gift is { } gift && gift.Kind == kind ?
            CommandText.Resolve(Texts,"value.gift-amount",gift.Amount,available) : CommandText.Resolve(Texts,"value.gift-amount-empty",available);
    }
    private void Number(GiftKind kind,uint maximum)
    {
        if (maximum == 0) { Notice(kind == GiftKind.Money ? CommandText.Resolve(Texts, "failure.money") : CommandText.Resolve(Texts, "failure.food")); return; }
        ReplaceChild(new NumberInputDialog { Textures = Textures, Subject = kind == GiftKind.Money ? CommandText.Resolve(Texts, "gift.money") : CommandText.Resolve(Texts, "gift.food"),
            State = new(1,Math.Min(1000000,maximum),Draft.Gift is { } gift && gift.Kind == kind ? gift.Amount : 1),
            Confirmed = value => { Draft.SetGift(new(kind,value,null)); Changed(); }, Closed = CloseChild });
    }
}

internal partial class MissionCommandFields : Control
{
    public TextureScope Textures { get; init; } = null!;
    public Rect2 Bounds { get; init; }
    public float SourceWidth { get; init; }
    public float DividerWidth { get; init; }
    public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;
    public override void _Draw()
    {
        CommandPanelArt.Strip(this,Textures,"deployment-facility",new(Bounds.Position,new(SourceWidth,32)));
        SkinPainter.Tile(this,Textures.Get("deployment-divider", this),new(Bounds.Position+new Vector2(0,40),new(DividerWidth,5)));
    }
}
