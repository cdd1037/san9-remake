using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

/// <summary>One-time presentation of the confirmed world; never initializes or advances gameplay.</summary>
public static class NewGameIntroduction
{
    public static IReadOnlyList<CommandMessage> Produce(StrategySnapshot view, WorldMessageContext context,
        TextCatalog texts, ProgressPresentationDefinition content)
    {
        var world = view.World;
        if (view.PlayerForceId is not int id || world.Clock != world.Simulation.Started) return [];
        var force = world.Forces.Single(f => f.Id == id);
        var people = world.Officers.Where(p => !p.IsDormant && p.IsServing && p.OwnerId == id).ToArray();
        var ruler = people.FirstOrDefault(p => p.Id == force.LeaderId);
        if (ruler is null) return [];
        var actor = people.FirstOrDefault(p => p.Id == force.AdviserId) ?? people.Where(p => p.Id != ruler.Id)
            .OrderByDescending(p => p.BaseAbilities.Intelligence).ThenBy(p => p.Id).FirstOrDefault() ?? ruler;
        var values = new Dictionary<string, MessageBinding>
        {
            ["actor"] = ProgressMessageProducer.Person(actor.Id), ["ruler"] = ProgressMessageProducer.Person(ruler.Id),
            ["force"] = ProgressMessageProducer.Force(id), ["count"] = new(0)
        };
        List<CommandMessage> result = [];
        void Add(string role) => result.Add(ProgressMessageProducer.Produce(content.Introduction[role], context, texts, values,
            _ => throw new InvalidDataException("New-game introduction must not consume randomness.")));
        Add("opening");
        var alliance = world.Simulation.Alliance;
        if (alliance is not null && (alliance.MemberForceIds.Contains(id) || alliance.TargetForceId == id))
        {
            values["alliance-target"] = ProgressMessageProducer.Person(world.Forces.Single(f => f.Id == alliance.TargetForceId).LeaderId!.Value);
            values["alliance-leader"] = ProgressMessageProducer.Person(world.Forces.Single(f => f.Id == alliance.LeaderForceId).LeaderId!.Value);
            Add(alliance.TargetForceId == id ? "alliance-target" : "alliance-member");
        }
        else Relations("enemies", 0, 40);
        Relations("friends", 60, 100);
        values["force"] = ProgressMessageProducer.Force(id);
        Add("economy");
        var outlook = EconomicOutlook.Read(world, view.Information.Offices.ToDictionary(o => o.Id, o => o.Salary), id);
        if (!outlook.Money.Sufficient || !outlook.Food.Sufficient) Add("shortage");
        values["count"] = new(view.Settlements.Count(s => s.OwnerId == id && s.Kind == SettlementKind.City));
        Add("ambition");
        return result;

        void Relations(string role, int minimum, int maximum)
        {
            int Relation(int other) => world.Relations.SingleOrDefault(r => r.FirstId == Math.Min(id, other) && r.SecondId == Math.Max(id, other))?.Value ?? 0;
            var matches = world.Forces.Where(f => f.Id != id && !f.IsDormant && !f.IsEliminated)
                .Where(f => Relation(f.Id) >= minimum && Relation(f.Id) <= maximum).OrderBy(f => f.Id).ToArray();
            values["force"] = ProgressMessageProducer.Force(matches.FirstOrDefault()?.Id ?? id);
            values["second-force"] = ProgressMessageProducer.Force(matches.Skip(1).FirstOrDefault()?.Id ?? id);
            values["count"] = new(matches.Length);
            Add(role);
        }
    }
}
