using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed class WorldState
{
    private readonly Dictionary<int, ForceState> _forces = [];
    private readonly Dictionary<int, SettlementState> _settlements = [];
    private readonly Dictionary<int, OfficerState> _officers = [];
    private readonly Dictionary<int, UnitState> _units = [];
    private ForceRelation[] _relations = [];
    private CorpsState[] _corps = [];
    private TreasureState[] _treasures = [];
    public long Revision { get; private set; }
    private WorldSnapshot? _snapshot;
    private WorldState(GameClock clock, ulong randomState)
    {
        Clock = clock;
        RandomState = randomState;
    }

    public GameClock Clock { get; private set; }

    public ulong RandomState { get; private set; }

    public static WorldState Create(GameClock clock, ulong seed) => new(clock, seed);

    public static WorldState Create(GameClock clock, ulong seed, IEnumerable<ForceState> forces,
        IEnumerable<SettlementState> settlements, IEnumerable<OfficerState> officers, IEnumerable<UnitState>? units = null, IEnumerable<CorpsState>? corps = null, IEnumerable<TreasureState>? treasures = null, IEnumerable<ForceRelation>? relations = null)
    {
        var world = new WorldState(clock, seed);
        foreach (var force in forces)
        {
            if (force.Id < 0 || force.Trust < 0) throw new ArgumentException("Invalid force.");
            world._forces.Add(force.Id, force);
        }
        foreach (var settlement in settlements)
        {
            if (settlement.Id < 0 || !Enum.IsDefined(settlement.Kind) || settlement.OwnerId is int owner && !world._forces.ContainsKey(owner) ||
                settlement.MaxDurability <= 0 || settlement.Durability < 0 || settlement.Durability > settlement.MaxDurability ||
                settlement.Morale is < 0 or > 100 || settlement.Commerce < 0 || settlement.Commerce > settlement.MaxCommerce ||
                settlement.Cultivation < 0 || settlement.Cultivation > settlement.MaxCultivation || settlement.PublicOrder is < 0 or > 1000)
                throw new ArgumentException("Invalid settlement.");
            world._settlements.Add(settlement.Id, settlement);
        }
        foreach (var officer in officers)
        {
            if (!officer.IsValid() || officer.SettlementId is int location && !world._settlements.ContainsKey(location) ||
                officer.OwnerId is int owner && !world._forces.ContainsKey(owner) ||
                officer.IsServing && world._settlements[officer.SettlementId!.Value].OwnerId != officer.OwnerId) throw new ArgumentException("Invalid officer.");
            world._officers.Add(officer.Id, officer);
        }
        if (world._forces.Values.Any(f => f.LeaderId is int leader &&
                (!world._officers.TryGetValue(leader, out var officer) || officer.OwnerId != f.Id)) ||
            world._settlements.Values.Any(s => s.CorpsId < 0 || s.CorpsId is not null && s.OwnerId is null) ||
            world._settlements.Values.Where(s => s.CorpsId is not null).GroupBy(s => s.CorpsId)
                .Any(g => g.Select(s => s.OwnerId).Distinct().Count() != 1))
            throw new ArgumentException("Invalid leader or corps relationship.");
        foreach (var unit in units ?? [])
        {
            if (!unit.IsValid || !world._forces.ContainsKey(unit.OwnerId)) throw new ArgumentException("Invalid unit.");
            world._units.Add(unit.Id, unit);
        }
        world._relations = (relations ?? []).ToArray();
        world._corps = (corps ?? []).ToArray();
        world._treasures = (treasures ?? []).ToArray();
        if (world._corps.Select(c => c.Id).Distinct().Count() != world._corps.Length ||
            world._corps.Any(c => c.Id < 0 || !world._forces.ContainsKey(c.OwnerId) ||
                !world._officers.TryGetValue(c.LeaderId, out var leader) || !leader.IsServing || leader.OwnerId != c.OwnerId) ||
            world._treasures.Select(t => t.Id).Distinct().Count() != world._treasures.Length ||
            world._treasures.Any(t => t.Id < 0 || t.HolderId is int holder && !world._officers.ContainsKey(holder)) ||
            world._relations.Select(r => (r.FirstId, r.SecondId)).Distinct().Count() != world._relations.Length ||
            world._relations.Any(r => r.FirstId >= r.SecondId || r.Value is < 0 or > 100 || !world._forces.ContainsKey(r.FirstId) || !world._forces.ContainsKey(r.SecondId)))
            throw new ArgumentException("Invalid information state relationships.");
        return world;
    }

    public DevelopmentOutcome Develop(DevelopmentCommand command)
    {
        if (command.Activity is not (DomesticActivity.Commerce or DomesticActivity.Cultivation))
            return new(DevelopmentRejection.UnsupportedActivity);
        if (!_forces.TryGetValue(command.ForceId, out var force) || !_settlements.TryGetValue(command.SettlementId, out var city) ||
            !_officers.TryGetValue(command.OfficerId, out var officer)) return new(DevelopmentRejection.UnknownObject);
        if (city.OwnerId != force.Id || officer.OwnerId != force.Id) return new(DevelopmentRejection.WrongOwner);
        if (!officer.IsServing || officer.Acted || officer.SettlementId != city.Id) return new(DevelopmentRejection.OfficerUnavailable);
        bool commerce = command.Activity == DomesticActivity.Commerce;
        if (commerce ? city.CommerceUsed : city.CultivationUsed) return new(DevelopmentRejection.AlreadyUsed);
        int current = commerce ? city.Commerce : city.Cultivation, maximum = commerce ? city.MaxCommerce : city.MaxCultivation;
        if (current >= maximum) return new(DevelopmentRejection.AtMaximum);
        if (force.Money < 50) return new(DevelopmentRejection.InsufficientFunds);
        var result = DomesticDevelopment.CalculateResult(command.Activity, current, maximum, (int)Math.Min(force.Money, int.MaxValue),
            [officer.Abilities], false);
        _forces[force.Id] = force with { Money = force.Money - (uint)result.Cost };
        _settlements[city.Id] = commerce ? city with { Commerce = city.Commerce + result.Increment, CommerceUsed = true }
            : city with { Cultivation = city.Cultivation + result.Increment, CultivationUsed = true };
        _officers[officer.Id] = officer with { Acted = true };
        Revision++;
        return new(DevelopmentRejection.None, result.Increment, result.Cost);
    }

    internal void CommitXun(GameClock clock, ulong randomState)
    {
        Clock = clock;
        RandomState = randomState;
        foreach (var city in _settlements.Values.ToArray()) _settlements[city.Id] = city with { CommerceUsed = false, CultivationUsed = false };
        foreach (var officer in _officers.Values.ToArray()) _officers[officer.Id] = officer with { Acted = false };
        Revision++;
    }

    internal WorldSnapshot CreateSnapshot()
    {
        if (_snapshot is not null && _snapshot.Revision == Revision) return _snapshot;
        string canonical = string.Create(
            CultureInfo.InvariantCulture,
            $"{Clock.Year}|{Clock.Month}|{(int)Clock.Xun}|{RandomState}");
        var forces = Array.AsReadOnly(_forces.Values.OrderBy(f => f.Id).ToArray());
        var settlements = Array.AsReadOnly(_settlements.Values.OrderBy(s => s.Id).ToArray());
        var officers = Array.AsReadOnly(_officers.Values.OrderBy(o => o.Id).ToArray());
        var corps = Array.AsReadOnly(_corps);
        var relations = Array.AsReadOnly(_relations);
        var treasures = Array.AsReadOnly(_treasures);
        var units = Array.AsReadOnly(_units.Values.OrderBy(u => u.Id).ToArray());
        canonical += JsonSerializer.Serialize(new { forces, settlements, officers, units, corps, treasures, relations });
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return _snapshot = new WorldSnapshot(Clock, RandomState, hash) { Forces = forces, Settlements = settlements, Officers = officers, Units = units, Corps = corps, Treasures = treasures, Relations = relations, Revision = Revision };
    }
}

public sealed record WorldSnapshot(
    GameClock Clock,
    ulong RandomState,
    string StateHash)
{
    public IReadOnlyList<ForceState> Forces { get; init; } = [];
    public IReadOnlyList<SettlementState> Settlements { get; init; } = [];
    public IReadOnlyList<OfficerState> Officers { get; init; } = [];
    public IReadOnlyList<UnitState> Units { get; init; } = [];
    public IReadOnlyList<ForceRelation> Relations { get; init; } = [];
    public IReadOnlyList<CorpsState> Corps { get; init; } = [];
    public IReadOnlyList<TreasureState> Treasures { get; init; } = [];
    public long Revision { get; init; }
}
