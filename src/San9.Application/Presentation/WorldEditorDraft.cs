using System.Collections.Immutable;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

public sealed record EditorTarget(int Id, string Name);
public sealed record EditorField(string Label, uint Minimum, uint Maximum, uint Value, Action<uint> Set,
    bool Toggle = false, string? Display = null, int? TacticId = null);

/// <summary>Owns the uncommitted values for an entire editor visit. Navigation never writes the session.</summary>
public sealed class WorldEditorDraft
{
    private readonly GameSession _session;
    private readonly TextCatalog _texts;
    private readonly Dictionary<int, WorldEdit> _initial = [];
    private readonly Dictionary<int, WorldEdit> _edits = [];
    private StrategySnapshot? _preview;
    private WorldSnapshot? _previewSource;
    public WorldEditorKind Kind { get; }
    public CatalogTableState Catalog { get; } = new();
    public InformationCategory Category => Kind switch
    {
        WorldEditorKind.Officers => InformationCategory.Officers, WorldEditorKind.Corps => InformationCategory.Forces,
        WorldEditorKind.Cities => InformationCategory.Cities, _ => InformationCategory.Regions
    };
    public IReadOnlyList<CatalogPage> Pages => Kind switch
    {
        WorldEditorKind.Officers => OfficerCatalog.Pages, WorldEditorKind.Facilities => WorldEditorCatalog.Facilities,
        WorldEditorKind.Obstacles => WorldEditorCatalog.Obstacles, _ => AdministrativeCatalog.Pages(Category)
    };
    public StrategySnapshot Preview
    {
        get
        {
            var world = _session.Snapshot;
            if (_preview is not null && ReferenceEquals(_previewSource, world)) return _preview;
            _previewSource = world;
            return _preview = _session.View.ForWorld(world with
            {
                Officers = world.Officers.Select(p => _edits.GetValueOrDefault(p.Id) is OfficerEdit edit ? edit.ApplyTo(p) : p).ToArray(),
                Corps = world.Corps.Select(c => _edits.GetValueOrDefault(c.Id) is CorpsEdit edit ? edit.ApplyTo(c) : c).ToArray(),
                Settlements = world.Settlements.Select(s => _edits.GetValueOrDefault(s.Id) is SettlementEdit edit ? edit.ApplyTo(s) : s).ToArray()
            });
        }
    }
    public CatalogRow[] ReadRows(int page)
    {
        var view = Preview;
        var allowed = (Kind == WorldEditorKind.Corps ? view.World.Corps.Where(c => _edits.ContainsKey(c.Id)).Select(c => c.OwnerId) : _edits.Keys).ToHashSet();
        if (Kind is WorldEditorKind.Facilities or WorldEditorKind.Obstacles)
            return WorldEditorCatalog.Read(view, _texts, Kind, page, allowed);
        var table = Kind == WorldEditorKind.Officers ? OfficerCatalog.Read(view, _texts, page, null, null) : new AdministrativeCatalog(view, _texts).Read(Category, page);
        var rows = table.Rows.Where(r => allowed.Contains(r.Key.Id));
        return rows.Select(r => new CatalogRow(r.Key.Id, r.Cells)).ToArray();
    }
    public int TargetFromRow(int rowId) => Kind == WorldEditorKind.Corps ? _session.Snapshot.Corps.Single(c => c.OwnerId == rowId && c.IsRoot).Id : rowId;
    public IReadOnlyList<EditorTarget> ForceCorps(int id)
    {
        int force = _session.Snapshot.Corps.Single(c => c.Id == id).OwnerId;
        var ids = _session.Snapshot.Corps.Where(c => c.OwnerId == force).Select(c => c.Id).ToHashSet();
        return Targets.Where(t => ids.Contains(t.Id)).ToArray();
    }
    public IReadOnlyList<(EditorField Field, int Category)> OfficerTacticFields(int id)
    {
        var fields = Fields(id);
        return _session.View.Information.Tactics.Where(t => WorldEditing.OfficerTactics(_session.MilitaryRules).Contains(t.Id))
            .Select(t => (fields.Single(f => f.TacticId == t.Id), _session.MilitaryRules!.Tactics[t.Id].Category!.Value)).ToArray();
    }
    public IReadOnlyList<(EditorField Field, TacticEffect Effect)> CityTacticFields(int id)
    {
        var fields = Fields(id);
        var city = _session.Snapshot.Settlements.Single(s => s.Id == id);
        return WorldEditing.CityTactics(_session.MilitaryRules, city).Select(tacticId =>
        {
            return (fields.Single(f => f.TacticId == tacticId), _session.MilitaryRules!.Tactics[tacticId].Effect);
        }).ToArray();
    }
    public IReadOnlyList<EditorTarget> Targets { get; }
    public string DisplayName(int id)
    {
        if (Kind != WorldEditorKind.Officers) return Name(id);
        var person = _session.View.Officers.Single(p => p.Id == id);
        string name = _texts.Resolve(person.NameKey);
        return person.CourtesyNameKey is string courtesy ? name + $"〈{_texts.Resolve(courtesy)}〉" : name;
    }
    public void Reset(int id)
    {
        _edits[id] = _initial[id];
        _preview = null;
    }
    public bool IsChanged(int id) => _edits[id] != _initial[id];
    public AdministrativeDetailData ReadAdministrativeDetail(int id)
    {
        var key = Kind == WorldEditorKind.Corps
            ? new InformationKey(InformationCategory.Forces, _session.Snapshot.Corps.Single(c => c.Id == id).OwnerId)
            : new InformationKey(Kind == WorldEditorKind.Cities ? InformationCategory.Cities : InformationCategory.Regions, id);
        return AdministrativeDetails.Read(Preview, _texts, key);
    }
    public bool HasChanges => _edits.Any(p => p.Value != _initial[p.Key]);
    public WorldEditOperation Operation => new(Kind, _edits.Where(p => p.Value != _initial[p.Key]).OrderBy(p => p.Key)
        .Select(p => p.Value).ToImmutableArray());

    public WorldEditorDraft(GameSession session, WorldEditorKind kind, TextCatalog texts)
    {
        _session = session; Kind = kind; _texts = texts;
        var world = session.Snapshot;
        Targets = WorldEditing.Candidates(world, kind).Select(id => new EditorTarget(id, Name(id))).ToArray();
        foreach (var target in Targets)
        {
            int id = target.Id;
            WorldEdit edit;
            if (kind == WorldEditorKind.Officers)
            {
                var p = world.Officers.Single(p => p.Id == id);
                edit = new OfficerEdit(id, p.BaseAbilities, p.Personality, p.Righteousness, p.Ambition, p.Affinity,
                    p.LifespanTier, p.OwnerId is null ? null : p.Loyalty, p.TacticProficiencies, p.MasteredCategories, p.LearnedTactics);
            }
            else if (kind == WorldEditorKind.Corps)
            {
                var c = world.Corps.Single(c => c.Id == id);
                edit = new CorpsEdit(id, c.Treasury.Money, c.Treasury.Food);
            }
            else
            {
                var s = world.Settlements.Single(s => s.Id == id);
                edit = new SettlementEdit(id, s.Durability, s.Soldiers, s.Morale, s.PublicOrder, s.Commerce,
                    s.MaxCommerce, s.Cultivation, s.MaxCultivation, s.MaxDurability, s.Population, s.DraftPopulation, s.EnabledTacticIds);
            }
            _initial.Add(id, edit); _edits.Add(id, edit);
        }
    }

    private string Name(int id)
    {
        if (Kind == WorldEditorKind.Officers) return _texts.Resolve(_session.View.MessageSpeaker(id).NameKey);
        if (Kind != WorldEditorKind.Corps) return _session.View.Settlements.Single(s => s.Id == id).ResolveName(_texts);
        var corps = _session.Snapshot.Corps.Single(c => c.Id == id);
        string force = _texts.Resolve(_session.View.Forces.Single(f => f.Id == corps.OwnerId).NameKey);
        string leader = corps.LeaderId is int person ? _texts.Resolve(_session.View.MessageSpeaker(person).NameKey) : "无军团长";
        return corps.IsRoot ? force : leader;
    }

    public IReadOnlyList<EditorField> Fields(int id)
    {
        List<EditorField> rows = [];
        void Number<T>(string label, uint min, uint max, Func<T, uint> read, Func<T, uint, T> write,
            bool toggle = false, string? display = null, int? tacticId = null) where T : WorldEdit
        {
            uint value = read((T)_edits[id]);
            rows.Add(new(label, min, max, value, value =>
            {
                if (value < min || value > max) throw new ArgumentOutOfRangeException(nameof(value));
                var next = write((T)_edits[id], value);
                // Reuse the original record when values return to their initial state, including array fields.
                _edits[id] = Equal(next, _initial[id]) ? _initial[id] : next;
                _preview = null;
            }, toggle, display, tacticId));
        }
        if (_edits[id] is OfficerEdit)
        {
            Number<OfficerEdit>("统率", 1, 100, p => (uint)p.Abilities.Leadership, (p, v) => p with { Abilities = p.Abilities with { Leadership = (int)v } });
            Number<OfficerEdit>("武力", 1, 100, p => (uint)p.Abilities.War, (p, v) => p with { Abilities = p.Abilities with { War = (int)v } });
            Number<OfficerEdit>("智力", 1, 100, p => (uint)p.Abilities.Intelligence, (p, v) => p with { Abilities = p.Abilities with { Intelligence = (int)v } });
            Number<OfficerEdit>("政治", 1, 100, p => (uint)p.Abilities.Politics, (p, v) => p with { Abilities = p.Abilities with { Politics = (int)v } });
            string[] personalities = ["莽撞", "刚胆", "冷静", "慎重"];
            for (int i = 0; i < personalities.Length; i++)
            {
                int personality = i;
                bool selected = ((OfficerEdit)_edits[id]).Personality == personality;
                Number<OfficerEdit>("性格 · " + personalities[i], 0, 1, p => p.Personality == personality ? 1u : 0u,
                    (p, v) => v == 0 ? p : p with { Personality = personality }, true, selected ? "已选" : "选用");
            }
            Number<OfficerEdit>("情义", 0, 15, p => (uint)p.Righteousness, (p, v) => p with { Righteousness = (int)v });
            Number<OfficerEdit>("野心", 0, 15, p => (uint)p.Ambition, (p, v) => p with { Ambition = (int)v });
            Number<OfficerEdit>("相性", 0, 149, p => (uint)p.Affinity, (p, v) => p with { Affinity = (int)v });
            Number<OfficerEdit>("寿命", 0, 7, p => (uint)p.LifespanTier, (p, v) => p with { LifespanTier = (int)v });
            if (((OfficerEdit)_edits[id]).Loyalty is not null)
                Number<OfficerEdit>("忠诚", 0, 100, p => (uint)p.Loyalty!.Value, (p, v) => p with { Loyalty = (int)v });
            string[] categories = ["步兵", "骑兵", "弓骑", "弩兵", "水军", "攻城", "智略", "谋略", "策略"];
            for (int i = 0; i < categories.Length; i++)
            {
                int category = i;
                Number<OfficerEdit>($"{categories[i]}熟练", 0, 1000, p => (uint)p.Proficiencies[category],
                    (p, v) => p with { Proficiencies = p.Proficiencies.SetItem(category, (int)v) });
                Number<OfficerEdit>($"{categories[i]}精通", 0, 1, p => p.MasteredCategories.Contains(category) ? 1u : 0u,
                    (p, v) => p with { MasteredCategories = Toggle(p.MasteredCategories, category, v != 0) }, true);
            }
            var learnable = WorldEditing.OfficerTactics(_session.MilitaryRules).ToHashSet();
            foreach (var tactic in _session.View.Information.Tactics.Where(t => learnable.Contains(t.Id)))
            {
                int tacticId = tactic.Id;
                Number<OfficerEdit>(_texts.Resolve(tactic.NameKey), 0, 1, p => p.LearnedTactics.Contains(tacticId) ? 1u : 0u,
                    (p, v) => p with { LearnedTactics = v != 0 ? p.LearnedTactics.Add(tacticId) : p.LearnedTactics.Remove(tacticId) }, true, tacticId: tacticId);
            }
        }
        else if (_edits[id] is CorpsEdit)
        {
            Number<CorpsEdit>("资金", 0, 1000000, c => c.Money, (c, v) => c with { Money = v });
            Number<CorpsEdit>("兵粮", 0, 1000000, c => c.Food, (c, v) => c with { Food = v });
        }
        else
        {
            var state = _session.Snapshot.Settlements.Single(s => s.Id == id);
            Number<SettlementEdit>("耐久", 1, (uint)((SettlementEdit)_edits[id]).MaxDurability, s => (uint)s.Durability, (s, v) => s with { Durability = (int)v });
            if (Kind != WorldEditorKind.Obstacles)
            {
                Number<SettlementEdit>("士兵", 0, state.SoldierCapacity, s => s.Soldiers, (s, v) => s with
                { Soldiers = v, Morale = v == 0 ? 0 : s.Soldiers == 0 ? 70 : s.Morale });
                if (((SettlementEdit)_edits[id]).Soldiers > 0)
                    Number<SettlementEdit>("士气", 1, 100, s => (uint)s.Morale, (s, v) => s with { Morale = (int)v });
            }
            if (Kind == WorldEditorKind.Cities)
            {
                Number<SettlementEdit>("民心", 0, 1000, s => (uint)s.PublicOrder, (s, v) => s with { PublicOrder = (int)v });
                Number<SettlementEdit>("收益", 0, (uint)((SettlementEdit)_edits[id]).MaxCommerce, s => (uint)s.Commerce, (s, v) => s with { Commerce = (int)v });
                Number<SettlementEdit>("最大收益", 100, 1000, s => (uint)s.MaxCommerce,
                    (s, v) => s with { MaxCommerce = (int)v, Commerce = Math.Min(s.Commerce, (int)v) });
                Number<SettlementEdit>("收获", 0, (uint)((SettlementEdit)_edits[id]).MaxCultivation, s => (uint)s.Cultivation, (s, v) => s with { Cultivation = (int)v });
                Number<SettlementEdit>("最大收获", 100, 1000, s => (uint)s.MaxCultivation,
                    (s, v) => s with { MaxCultivation = (int)v, Cultivation = Math.Min(s.Cultivation, (int)v) });
                Number<SettlementEdit>("最大耐久", 100, 1000, s => (uint)s.MaxDurability,
                    (s, v) => s with { MaxDurability = (int)v, Durability = Math.Min(s.Durability, (int)v) });
                Number<SettlementEdit>("非兵役人口", 3000, 1000000, s => s.Population, (s, v) => s with { Population = v });
                Number<SettlementEdit>("兵役人口", 0, 500000, s => s.DraftPopulation, (s, v) => s with { DraftPopulation = v });
                foreach (int tacticId in WorldEditing.CityTactics(_session.MilitaryRules, state))
                {
                    var tactic = _session.MilitaryRules!.Tactics[tacticId];
                    string name = _session.View.Information.Tactics.SingleOrDefault(t => t.Id == tacticId)?.NameKey ?? "";
                    Number<SettlementEdit>("城防：" + _texts.Resolve(name), 0, 1, s => s.EnabledTacticIds.Contains(tacticId) ? 1u : 0u,
                        (s, v) => s with { EnabledTacticIds = v == 0 ? Toggle(s.EnabledTacticIds, tacticId, false) :
                            s.EnabledTacticIds.Where(other => (_session.MilitaryRules.Tactics[other].Effect == TacticEffect.FacilityAttack) !=
                                (tactic.Effect == TacticEffect.FacilityAttack)).Append(tacticId).Order().ToImmutableArray() }, true, tacticId: tacticId);
                }
            }
        }
        return rows;
    }

    private static ImmutableArray<int> Toggle(ImmutableArray<int> values, int id, bool enabled) =>
        enabled ? values.Contains(id) ? values : values.Add(id).Order().ToImmutableArray() : values.Remove(id);

    private static bool Equal(WorldEdit first, WorldEdit second) => (first, second) switch
    {
        (OfficerEdit a, OfficerEdit b) => a with { Proficiencies = b.Proficiencies, MasteredCategories = b.MasteredCategories, LearnedTactics = b.LearnedTactics } == b &&
            a.Proficiencies.SequenceEqual(b.Proficiencies) && a.MasteredCategories.Order().SequenceEqual(b.MasteredCategories.Order()) && a.LearnedTactics.SetEquals(b.LearnedTactics),
        (SettlementEdit a, SettlementEdit b) => a with { EnabledTacticIds = b.EnabledTacticIds } == b && a.EnabledTacticIds.Order().SequenceEqual(b.EnabledTacticIds.Order()),
        _ => first == second
    };
}
