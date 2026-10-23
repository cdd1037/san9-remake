using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

public sealed record ProducedMessage(string Key, GameClock Clock, string BbCode);

/// <summary>A read-only message projection over one committed world, independent of UI selection and simulation RNG.</summary>
public sealed class WorldMessageContext
{
    private readonly ScenarioInitialState _definition;
    private readonly StrategySnapshotSource _source;
    private readonly WorldSnapshot _world;
    private readonly TextCatalog _texts;
    private readonly Dictionary<int, OfficerState> _people;
    private readonly Dictionary<int, ForceState> _forces;
    private readonly Dictionary<int, SettlementState> _places;
    private readonly Dictionary<int, UnitState> _units;
    private readonly Dictionary<int, OfficerInitialState> _personDefinitions;
    private readonly OfficerAbilityRules _abilities;
    private readonly Lazy<StrategySnapshot> _view;
    private readonly IReadOnlyDictionary<int, BattleObjectContext> _capturedUnits;

    public WorldMessageContext(ScenarioInitialState definition, WorldSnapshot world, TextCatalog texts,
        IEnumerable<BattleObjectContext>? capturedObjects = null)
        : this(definition, new StrategySnapshotSource(definition, new ScenarioRules(definition)), world, texts, capturedObjects) { }

    internal WorldMessageContext(ScenarioInitialState definition, StrategySnapshotSource source, WorldSnapshot world,
        TextCatalog texts, IEnumerable<BattleObjectContext>? capturedObjects = null)
    {
        _source = source;
        _definition = definition; _world = world; _texts = texts;
        _people = world.Officers.ToDictionary(p => p.Id);
        _forces = world.Forces.ToDictionary(f => f.Id);
        _places = world.Settlements.ToDictionary(s => s.Id);
        _units = world.Units.ToDictionary(u => u.Id);
        _personDefinitions = definition.Officers.ToDictionary(p => p.State.Id);
        _abilities = source.Abilities;
        _view = new(() => source.Project(world));
        _capturedUnits = (capturedObjects ?? []).Where(o => !o.IsSettlement).ToDictionary(o => o.Id);
    }

    // An occurrence changes the displayed date and captured battle objects, not world records or rules.
    private WorldMessageContext(WorldMessageContext source, GameClock clock, BattleObjectContext[] objects)
    {
        _source = source._source;
        _definition = source._definition; _world = source._world with { Clock = clock }; _texts = source._texts;
        _people = source._people; _forces = source._forces; _places = source._places; _units = source._units;
        _personDefinitions = source._personDefinitions; _abilities = source._abilities;
        _view = new(() => _source.Project(_world));
        _capturedUnits = objects.Where(o => !o.IsSettlement).ToDictionary(o => o.Id);
    }

    public WorldMessageContext AtOccurrence(GameClock clock, params BattleObjectContext[] objects) =>
        new(this, clock, objects);
    public WorldMessageContext ForWorld(WorldSnapshot world) => new(_definition, _source, world, _texts);
    public CommandSpeaker Speaker(int id) => new(PersonName(id),_people[id].OwnerId is int owner
        ? _texts.Resolve(_view.Value.Forces.Single(f=>f.Id==owner).NameKey) : null);

    public ProducedMessage Produce(string key, IReadOnlyDictionary<string, MessageBinding> values,
        Func<int, int> randomIndex)
    {
        foreach (var value in values.Values)
            if (value.Entity is { } entity) Validate(entity);
        var context = new MessageContext(values, Query, randomIndex);
        return new(key, _world.Clock, new MessageEvaluator(_texts.Messages).Evaluate(key, context));
    }

    public WorldMessageContext PreviewCountryTitle(int forceId,int titleId)
    {
        if (!_definition.Information.CountryTitles.Any(t=>t.Id==titleId) || !_forces.ContainsKey(forceId))
            throw new ArgumentException("Unknown country title preview.");
        return new(_definition,_source,_world with { Forces=_world.Forces.Select(f=>f.Id==forceId ? f with { CountryTitleId=titleId } : f).ToArray() },_texts);
    }

    public MessageBinding Query(string name, IReadOnlyList<MessageBinding> args)
    {
        int Id(int index, MessageEntityKind kind)
        {
            if (index >= args.Count || args[index].Entity is not { } entity || entity.Kind != kind)
                throw new InvalidDataException($"{name} requires a {kind} reference at argument {index}.");
            Validate(entity);
            return entity.Id;
        }
        OfficerState Person(int index = 0) => _people[Id(index, MessageEntityKind.Person)];
        ForceState Force(int index = 0) => args[index].Entity?.Kind == MessageEntityKind.Corps
            ? _forces[_world.Corps.Single(c => c.Id == Id(index, MessageEntityKind.Corps)).OwnerId]
            : _forces[Id(index, MessageEntityKind.Force)];
        MessageBinding Text(string text) => new(0, text);
        MessageBinding Flag(bool value) => new(value ? 1 : 0);
        if (name == "scenario.has-message-tag")
        {
            Count(1);
            if (args[0].Text is not { Length: > 0 } tag || args[0].Entity is not null)
                throw new InvalidDataException("Message tag requires a named tag.");
            return Flag(_texts.MessageTags.Contains(tag, StringComparer.Ordinal));
        }
        if (name is "year" or "month" or "day" or "season")
        {
            Count(0);
            return new(name switch { "year" => _world.Clock.Year, "month" => _world.Clock.Month,
                "day" => _world.Clock.Day, _ => (_world.Clock.Month - 1) / 3 });
        }
        if (name == "event.occurred")
        {
            Count(1);
            if (args[0].Text is not { Length: > 0 } key || args[0].Entity is not null)
                throw new InvalidDataException("Event history requires a named event key.");
            return Flag(_world.Simulation.ConsumedEventKeys.Any(k => k.Value == key));
        }
        if (name is "parent-child" or "same-parent" or "same-bloodline" or "senior-kin" or "junior-kin" or
            "affinity-distance" or "same-root-force" or "person.same-portrait")
        {
            Count(2);
            var a = Person(); var b = Person(1);
            bool siblings = a.ParentId is not null && a.ParentId == b.ParentId;
            return name switch
            {
                "parent-child" => Flag(a.ParentId == b.Id || b.ParentId == a.Id),
                "same-parent" => Flag(siblings),
                "same-bloodline" => Flag(a.BloodlineId is not null && a.BloodlineId == b.BloodlineId),
                "senior-kin" => Flag(b.ParentId == a.Id || siblings && a.BirthYear < b.BirthYear),
                "junior-kin" => Flag(a.ParentId == b.Id || siblings && a.BirthYear > b.BirthYear),
                "affinity-distance" => new(PersonnelRulebook.AffinityDistance(a, b)),
                "same-root-force" => Flag(a.OwnerId is not null && a.OwnerId == b.OwnerId),
                _ => Flag(Profile(a.Id).PortraitKey is { } portrait && portrait == Profile(b.Id).PortraitKey)
            };
        }
        if (name == "force-relation")
        {
            Count(2); var first = Force(); var second = Force(1);
            if (first.Id == second.Id) return new(100);
            var relation = _world.Relations.SingleOrDefault(r => r.FirstId == Math.Min(first.Id, second.Id) && r.SecondId == Math.Max(first.Id, second.Id));
            return new(relation?.Value ?? 0);
        }
        if (name is "has-treasure" or "has-treasure-category")
        {
            Count(2); var person = Person();
            int category = checked((int)args[1].Number);
            int item = name == "has-treasure" ? Id(1, MessageEntityKind.Treasure) : -1;
            if (name == "has-treasure-category" && (args[1].Entity is not null || args[1].Text is not null))
                throw new InvalidDataException("Treasure category must be a number.");
            return Flag(_world.Treasures.Any(t => t.HolderId == person.Id && t.Discovery == TreasureDiscovery.Discovered &&
                (name == "has-treasure" ? t.Id == item : _definition.Information.Treasures.Single(d => d.Id == t.Id).Category == category)));
        }
        Count(1);
        if (name.StartsWith("person.", StringComparison.Ordinal))
        {
            var p = Person();
            return name switch
            {
                "person.name" => Text(PersonName(p.Id)),
                "person.surname" => Text(ResolveOptional(Profile(p.Id).SurnameKey)),
                "person.given-name" => Text(ResolveOptional(Profile(p.Id).GivenNameKey)),
                "person.courtesy-name" => Text(ResolveOptional(_personDefinitions[p.Id].CourtesyNameKey)),
                "person.office-name" => Text(p.OfficeId is int office ? _texts.Resolve(_definition.Information.Offices.Single(o => o.Id == office).NameKey) : ""),
                "person.office-command-capacity" => new(p.OfficeId is int office ? _definition.Information.Offices.Single(o => o.Id == office).CommandCapacity : 0),
                "person.location-name" => Text(p.SettlementId is int place ? PlaceName(place) : p.UnitId is int unit ? UnitLocation(_units[unit]) :
                    p.RegionId is int region ? _texts.Resolve(_definition.RegionNameKeys[region]) : ""),
                "person.is-female" => Flag(p.IsFemale),
                "person.is-male" => Flag(!p.IsFemale),
                "person.is-ruler" => Flag(p.Status == OfficerStatus.Ruler),
                "person.is-strategist" => Flag(p.Status == OfficerStatus.Strategist),
                "person.is-captive" => Flag(p.Status == OfficerStatus.Captive),
                "person.is-free" => Flag(p.Status == OfficerStatus.Free),
                "person.personality" => new(p.Personality), "person.righteousness" => new(p.Righteousness),
                "person.ambition" => new(p.Ambition), "person.injury" => new(p.Injury),
                "person.loyalty" => new(Math.Min(100, p.Loyalty)),
                "person.war" => new(_abilities.Evaluate(p, _world.Treasures).War),
                "person.intelligence" => new(_abilities.Evaluate(p, _world.Treasures).Intelligence),
                "person.leadership" => new(_abilities.Evaluate(p, _world.Treasures).Leadership),
                "person.politics" => new(_abilities.Evaluate(p, _world.Treasures).Politics),
                "person.is-stationed" => Flag(p.SettlementId is not null || p.UnitId is int id && _units[id].IsEmbedded),
                "person.is-on-military-duty" => Flag(p.UnitId is int militaryUnit && _units[militaryUnit].EffectiveOrderClass == UnitOrderClass.Military),
                _ => throw Unsupported(name)
            };
        }
        if (name.StartsWith("force.", StringComparison.Ordinal))
        {
            var f = Force();
            return name switch
            {
                "force.name" => Text(_texts.Resolve(_view.Value.Forces.Single(v => v.Id == f.Id).NameKey)),
                "force.leader-name" => Text(f.LeaderId is int leader ? PersonName(leader) : ""),
                "force.direct-leader-name" => Text(args[0].Entity?.Kind == MessageEntityKind.Corps &&
                    _world.Corps.Single(c => c.Id == args[0].Entity!.Id).LeaderId is int corpsLeader ? PersonName(corpsLeader) :
                    f.LeaderId is int ruler ? PersonName(ruler) : ""),
                "force.country-name" => Text(f.CountryTitleId is int title ? _texts.Resolve(_definition.Information.CountryTitles.Single(t => t.Id == title).NameKey) :
                    f.LeaderId is int ruler ? PersonName(ruler) : ""),
                "force.rank-name" => Text(f.RankId is int rank ? _texts.Resolve(_definition.Information.Ranks.Single(r => r.Id == rank).NameKey) : ""),
                "force.rank-precedence" => new(f.RankId is int rank ? _definition.Information.Ranks.Single(r => r.Id == rank).Precedence : 0),
                "force.is-alliance-leader" => Flag(_world.Simulation.Alliance?.LeaderForceId == f.Id),
                "force.corps-count" => new(_world.Corps.Count(c => c.OwnerId == f.Id)),
                _ => throw Unsupported(name)
            };
        }
        return name switch
        {
            "settlement.name" => Text(args[0].Entity?.Kind==MessageEntityKind.Region ? _texts.Resolve(_definition.RegionNameKeys[Id(0,MessageEntityKind.Region)]) : PlaceName(Id(0, MessageEntityKind.Settlement))),
            "settlement.name-with-city" or "settlement.base-name-with-city" => Text(PlaceNameWithCity(args[0], name == "settlement.base-name-with-city")),
            "settlement.base-name" => Text(args[0].Entity?.Kind==MessageEntityKind.Region ? _texts.Resolve(_definition.RegionNameKeys[Id(0,MessageEntityKind.Region)]) : BasePlaceName(Id(0, MessageEntityKind.Settlement))),
            "settlement.owner-name" => Text(_places[Id(0, MessageEntityKind.Settlement)].OwnerId is int owner ?
                _texts.Resolve(_view.Value.Forces.Single(f => f.Id == owner).NameKey) : ""),
            "settlement.type-name" => Text(SettlementType(Id(0, MessageEntityKind.Settlement))),
            "treasure.name" => Text(_texts.Resolve(_definition.Information.Treasures.Single(t => t.Id == Id(0, MessageEntityKind.Treasure)).NameKey)),
            "treasure.category" => new(_definition.Information.Treasures.Single(t => t.Id == Id(0, MessageEntityKind.Treasure)).Category),
            "tactic.name" => Text(_texts.Resolve(_definition.Information.Tactics.Single(t => t.Id == Id(0, MessageEntityKind.Tactic)).NameKey)),
            "tactic.category-name" => Text(TacticCategory(Id(0, MessageEntityKind.Tactic))),
            "military-target.location-name" => Text(args[0].Entity?.Kind == MessageEntityKind.Settlement ?
                PlaceName(Id(0, MessageEntityKind.Settlement)) : TargetLocation(Id(0, MessageEntityKind.Unit))),
            "military-target.is-garrison" => Flag(args[0].Entity?.Kind == MessageEntityKind.Settlement
                ? _places.ContainsKey(Id(0, MessageEntityKind.Settlement)) : _capturedUnits.TryGetValue(Id(0, MessageEntityKind.Unit), out var captured)
                    ? captured.IsGarrison : _units[Id(0, MessageEntityKind.Unit)].IsEmbedded),
            "military-target.commander-name" => Text((_capturedUnits.TryGetValue(Id(0, MessageEntityKind.Unit), out var target)
                ? target.CommanderId : _units[Id(0, MessageEntityKind.Unit)].CommanderId) is int commander ? PersonName(commander) :
                throw new InvalidDataException("A field unit display name requires a commander.")),
            _ => throw Unsupported(name)
        };

        void Count(int expected)
        { if (args.Count != expected) throw new InvalidDataException($"{name} requires {expected} arguments."); }
    }

    private string PersonName(int id) => _texts.Resolve(_personDefinitions[id].NameKey);
    private string ResolveOptional(string? key) => key is null ? "" : _texts.Resolve(key);
    private MessagePersonProfile Profile(int id) => _texts.MessagePeople.TryGetValue(id, out var profile) ? profile :
        throw new InvalidDataException($"Missing message person profile: {id}.");
    private string PlaceName(int id) => _view.Value.Settlements.Single(s => s.Id == id).ResolveName(_texts);
    private string PlaceNameWithCity(MessageBinding value, bool baseName)
    {
        if (value.Entity is not { Kind: MessageEntityKind.Settlement or MessageEntityKind.Region } entity)
            throw new InvalidDataException("Qualified place name requires a settlement or region reference.");
        Validate(entity);
        string place = entity.Kind == MessageEntityKind.Region ? _texts.Resolve(_definition.RegionNameKeys[entity.Id]) :
            baseName ? BasePlaceName(entity.Id) : PlaceName(entity.Id);
        if (entity.Kind == MessageEntityKind.Settlement && _places[entity.Id].Kind == SettlementKind.City) return place;
        if (!_definition.RegionCityIds.TryGetValue(entity.Id, out int city))
            throw new InvalidDataException($"Missing city for place {entity.Id}.");
        return place + "〈" + PlaceName(city) + "〉";
    }
    private string BasePlaceName(int id) => _texts.Resolve(_definition.RegionNameKeys.TryGetValue(id, out string? key)
        ? key : _view.Value.Settlements.Single(s => s.Id == id).NameKey);
    private string SettlementType(int id) => _places[id].Kind == SettlementKind.City ? "都市" :
        _view.Value.Settlements.Single(s => s.Id == id).FacilityTypeId is int type
            ? ResolveOptional(_definition.FacilityNameSuffixKeys.GetValueOrDefault(type))
            : throw new InvalidDataException($"Settlement {id} has no facility type.");
    private string UnitLocation(UnitState unit) => unit.ServicePlaceId is int place ? PlaceName(place) :
        throw new InvalidDataException($"Unit {unit.Id} has no service location.");
    private string TargetLocation(int id) => _capturedUnits.TryGetValue(id, out var target)
        ? target.ServicePlaceId is int place ? PlaceName(place) : throw new InvalidDataException($"Captured unit {id} has no service location.")
        : UnitLocation(_units[id]);
    private string TacticCategory(int id)
    {
        string[] names =
            ["步兵", "骑兵", "弓骑", "弩兵", "水军", "攻城", "智略", "谋略", "策略"];
        int? category = _definition.Military?.Tactics.Single(t => t.Id == id).Category;
        return category is int index && (uint)index < names.Length ? names[index] :
            throw new InvalidDataException($"Tactic {id} has no named category.");
    }
    private static InvalidDataException Unsupported(string name) => new($"Unsupported world message query: {name}.");

    private void Validate(MessageEntity entity)
    {
        bool exists = entity.Kind switch
        {
            MessageEntityKind.Person => _people.ContainsKey(entity.Id), MessageEntityKind.Force => _forces.ContainsKey(entity.Id),
            MessageEntityKind.Settlement => _places.ContainsKey(entity.Id), MessageEntityKind.Unit => _units.ContainsKey(entity.Id) || _capturedUnits.ContainsKey(entity.Id),
            MessageEntityKind.Treasure => _world.Treasures.Any(t => t.Id == entity.Id),
            MessageEntityKind.Tactic => _definition.Information.Tactics.Any(t => t.Id == entity.Id),
            MessageEntityKind.Region => _definition.RegionNameKeys.ContainsKey(entity.Id),
            MessageEntityKind.Corps => _world.Corps.Any(c => c.Id == entity.Id), _ => false
        };
        if (!exists) throw new InvalidDataException($"Missing message entity: {entity.Kind} {entity.Id}.");
    }
}
