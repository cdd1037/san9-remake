using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

/// <summary>A deterministic first economic policy. All candidates use the public command validator.</summary>
public static class DomesticPlanner
{
    public static GameCommand Choose(GameSession session, ControlSeat seat, Guid commandId, int turn, bool scripted = false)
        => Choose(session.Snapshot, session.Validate, seat, commandId, turn, scripted);

    internal static GameCommand Choose(WorldSnapshot snapshot, Func<CommandContext, GameCommand, CommandSubmissionResult> validate,
        ControlSeat seat, Guid commandId, int turn, bool scripted = false, int? corpsId = null)
    {
        snapshot = AiPlanningWorld.Read(snapshot);
        var busy = snapshot.Simulation.Tasks.Select(t => t.OfficerId).ToHashSet();
        var available = snapshot.Officers.Where(p => p.IsServing && !p.Acted && p.OwnerId == seat.ForceId &&
            p.SettlementId is not null && !busy.Contains(p.Id)).ToLookup(p => p.SettlementId!.Value);
        foreach (var city in snapshot.Settlements.Where(s => s.OwnerId == seat.ForceId && (corpsId is null || s.CorpsId == corpsId)).OrderBy(s => s.Id))
        {
            var people = available[city.Id].OrderBy(p => p.Id).Take(5).ToArray();
            if (people.Length == 0) continue;
            var corps = city.CorpsId is int id ? snapshot.Corps.Single(c => c.Id == id) : snapshot.Corps.Single(c => c.OwnerId == seat.ForceId && c.IsRoot);
            DomesticActivity[] priorities = scripted ? Enumerable.Range(0, 6).Select(i => (DomesticActivity)((turn + i) % 6)).ToArray() :
                [DomesticActivity.Cultivation, DomesticActivity.Commerce, DomesticActivity.Patrol, DomesticActivity.Recruit, DomesticActivity.Train, DomesticActivity.Repair];
            if (!scripted)
            {
                // Maintenance needs take precedence; otherwise rotate investments to avoid starving a project.
                var urgent = city.Soldiers < 3000 ? DomesticActivity.Recruit : city.Morale < 65 ? DomesticActivity.Train :
                    city.PublicOrder < 500 ? DomesticActivity.Patrol : city.Durability < city.MaxDurability / 2 ? DomesticActivity.Repair : priorities[turn % priorities.Length];
                priorities = new[] { urgent }.Concat(priorities).Distinct().ToArray();
            }
            foreach (var activity in priorities)
            {
                // Keep a small cash reserve and ten days of recruit rations before discretionary spending.
                uint money = corps.Treasury.Money;
                uint food = corps.Treasury.Food;
                if (activity == DomesticActivity.Recruit && (city.Soldiers >= 10000 || food < (city.Soldiers + 5000L) / 50)) continue;
                for (int count = people.Length; count > 0; count--)
                {
                    if (activity != DomesticActivity.Train && money < 50 * count + 100) continue;
                    var command = new DevelopCommand(commandId, seat.ForceId, snapshot.Simulation.NextSequence,
                        new(seat.ForceId, city.Id, people.Take(count).Select(p => p.Id).ToImmutableArray(), activity));
                    if (validate(new(seat.ControllerId), command).Accepted) return command;
                }
            }
        }
        return new WaitCommand(commandId, seat.ForceId, snapshot.Simulation.NextSequence);
    }
}
