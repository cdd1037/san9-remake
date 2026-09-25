using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

internal sealed record OfficerCommandCells(CatalogCell Order, CatalogCell Object, CatalogCell Target,
    CatalogCell Arrival, CatalogCell Rank, CatalogCell Tactic);

internal sealed class OfficerCommands(StrategySnapshot view, TextCatalog texts)
{
    private readonly Dictionary<int, OfficerView> people = view.Officers.ToDictionary(p => p.Id);
    private readonly Dictionary<int, ForceView> forces = view.Forces.ToDictionary(f => f.Id);
    private readonly Dictionary<int, UnitState> units = view.World.Units.ToDictionary(u => u.Id);
    private readonly Dictionary<int, SettlementView> places = view.Settlements.ToDictionary(s => s.Id);
    private readonly Dictionary<int, string> treasures = view.Information.Treasures.ToDictionary(t => t.Id, t => t.NameKey);
    private readonly Dictionary<int, PersistentTask> tasks = view.World.Simulation.Tasks
        .Where(t => t.Journey is not null).ToDictionary(t => t.OfficerId);
    private static readonly IReadOnlyDictionary<int, string> TacticNames = OfficerCatalog.Pages.Skip(4)
        .SelectMany(p => p.Columns).Where(c => c.Id.StartsWith("tactic.", StringComparison.Ordinal))
        .ToDictionary(c => int.Parse(c.Id.AsSpan(7)), c => c.Title);

    private string Person(int? id) => id is int key && people.TryGetValue(key, out var p) ? texts.Resolve(p.NameKey) : "";
    private string Force(int id) => forces.TryGetValue(id, out var f) ? Person(f.LeaderId) : "";
    private string Region(int id) => view.RegionNameKeys.TryGetValue(id, out string? key) ? texts.Resolve(key)
        : places.TryGetValue(id, out var p) ? texts.Resolve(p.NameKey) : "";
    private string Place(int id) => places.TryGetValue(id, out var p) ? p.ResolveName(texts) : Region(id);
    private string Unit(int id) => units.TryGetValue(id, out var u) && u.CommanderId is int leader ? Person(leader) + "队" : "";
    private string Surroundings(WorldPosition position) => view.RegionAt(position) is int region
        ? Region(region) + "周围" : $"({position.X},{position.Y})";
    private static CatalogCell Hidden(int width) => new(new string('＊', width), IsUnavailable: true);

    public OfficerCommandCells Read(OfficerState person)
    {
        var task = tasks.GetValueOrDefault(person.Id);
        var unit = person.UnitId is int unitId ? units[unitId] : null;
        BattleRank? rank = person.IsServing && unit is { FormationId: not null, IsEmbedded: false, IsPersonnel: false }
            ? view.FormationRank(unit, person.Id) : null;
        CatalogCell rankCell = rank is { } row
            ? new(row switch { BattleRank.Front => "前锋", BattleRank.Middle => "中锋", _ => "后卫" }, 3 - (int)row)
            : CatalogCell.Unavailable;
        // Original default expert setting exposes enemy equipped tactics. Rank is public.
        CatalogCell tactic = person.EquippedTacticId is not int tacticId ? new("")
            : rank is null ? CatalogCell.Unavailable
            : TacticNames.TryGetValue(tacticId, out string? name) ? new(name) : CatalogCell.Unavailable;
        var arrival = OfficerArrival.Read(view, person, task);
        if (person.OwnerId != view.PlayerForceId)
            return new(Hidden(2), Hidden(4), Hidden(5), arrival, rankCell, tactic);

        CatalogCell order = "";
        CatalogCell subject = CatalogCell.Unavailable;
        CatalogCell target = CatalogCell.Unavailable;
        if (unit is not null)
        {
            var effective = unit.EffectiveOrder;
            order = effective?.Kind switch
            {
                MilitaryOrderKind.Move => "进军", MilitaryOrderKind.Return => "撤退",
                MilitaryOrderKind.Transport => "输送",
                MilitaryOrderKind.AttackUnit or MilitaryOrderKind.AttackSettlement => "攻击",
                MilitaryOrderKind.Construct => effective.Construction is { } construction &&
                    view.FacilityNameSuffixKeys.TryGetValue(construction.FacilityTypeId, out string? key) ? texts.Resolve(key) : "建设",
                _ => ""
            };
            target = effective?.Kind switch
            {
                MilitaryOrderKind.AttackUnit => Unit(effective.UnitId!.Value),
                MilitaryOrderKind.Move => Surroundings(effective.Position!.Value),
                MilitaryOrderKind.Construct => Region(effective.Construction!.RegionId) + "周围",
                _ => effective?.SettlementId is int destination ? Place(destination) : CatalogCell.Unavailable
            };
        }
        else if (task?.Journey is { } journey)
        {
            order = task.Kind switch
            {
                "personnel-move" => "移动", "personnel-search" => "探索", "personnel-recruit" => "登庸",
                "personnel-return" => "撤回", "tactic-study" => "修行", "strategy-alienate" => "离间",
                "strategy-burn" => "烧夷", "strategy-seize" => "夺取", "strategy-gossip" => "流言",
                "diplomacy-gift" => "赠与", "diplomacy-attack-request" => "请求",
                "diplomacy-prisoner-return" => "交还", "diplomacy-surrender" => "劝降",
                "unit-strategy" => journey.UnitStrategy!.Strategy switch
                {
                    UnitStrategyKind.FalseReport => "伪报", UnitStrategyKind.Disorder => "扰乱",
                    UnitStrategyKind.Inspire => "激励", UnitStrategyKind.Reinforce => "救援",
                    _ => throw new ArgumentOutOfRangeException(nameof(task))
                },
                _ => throw new InvalidOperationException($"Unknown officer journey kind '{task.Kind}'.")
            };
            if (task.Kind == "personnel-search") subject = Region(journey.DestinationPlaceId);
            else if (task.Kind is "personnel-recruit" or "strategy-alienate") subject = Person(journey.TargetOfficerId);
            else if (journey.Gossip is { } gossip)
            {
                subject = Force(gossip.FirstTargetForceId);
                target = Force(gossip.SecondTargetForceId);
            }
            else if (task.Gift is { } gift)
            {
                subject = Force(gift.TargetForceId);
                target = gift.Offer.Kind switch
                {
                    GiftKind.Money => $"资金{gift.Offer.Amount}", GiftKind.Food => $"兵粮{gift.Offer.Amount}",
                    GiftKind.Treasure => texts.Resolve(treasures[gift.Offer.TreasureId!.Value]),
                    _ => throw new ArgumentOutOfRangeException(nameof(task))
                };
            }
            else if (task.AttackRequest is { } request)
            {
                subject = Force(request.TargetForceId);
                target = Place(request.TargetSettlementId);
            }
            else if (task.PrisonerReturn is { } prisoner)
            {
                subject = Force(prisoner.TargetForceId);
                target = Person(prisoner.PrisonerId);
            }
            else if (task.Surrender is { } surrender) subject = Force(surrender.TargetForceId);
            else if (journey.Burn is { } burn) target = Place(burn.TargetSettlementId);
            else if (journey.Seize is { } seize) target = Place(seize.TargetSettlementId);
            else target = journey.UnitStrategy is { } strategy ? Unit(strategy.TargetUnitId) : Place(journey.DestinationPlaceId);
        }
        else if (person.SettlementId is not null && person.Acted) order = "完";
        return new(order, subject, target, arrival, rankCell, tactic);
    }
}
