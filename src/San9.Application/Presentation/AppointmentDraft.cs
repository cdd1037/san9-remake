using System.Collections.Immutable;
using San9.Content.Interface;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

/// <summary>Revision-bound appointment choices. Editing is read-only; submission uses ordinary recorded commands.</summary>
public sealed class AppointmentDraft
{
    private readonly GameSession session;
    public OfficeAppointmentPlan? OfficePlan { get; }
    public StrategySnapshot View { get; }
    public int SourceId { get; }
    public string Kind { get; }
    public string Title => Kind switch { "strategist" => "军师", "rank" => "爵位", "reward" => "褒奖",
        "bestow" => "授与", "confiscate" => "没收", "execute" => "处斩", "exile" => "流放", _ => throw new InvalidOperationException() };
    public ImmutableArray<int> Candidates { get; }
    public ImmutableArray<int> TreasureIds { get; }
    public ImmutableArray<int> Members { get; private set; } = [];
    public int? TreasureId { get; private set; }
    public uint Money { get; }
    public int SelectionLimit => Kind == "reward" ? (int)Math.Min(Money / 100, (uint)Candidates.Length)
        : Kind == "exile" ? Candidates.Length : 1;
    public bool IsCurrent => session.Snapshot.Revision == View.World.Revision;
    public uint Cost => Kind == "reward" ? (uint)Members.Length * 100 : 0;
    public ImmutableArray<OfficeAssignment> Changes => OfficePlan?.Changes ?? [];
    public bool CanExecute => IsCurrent && (Kind switch {
        "rank" => !Changes.IsEmpty, "confiscate" => TreasureId is not null,
        "bestow" => Members.Length == 1 && TreasureId is not null, _ => !Members.IsEmpty });
    public static bool Supports(string kind) => kind is "strategist" or "rank" or "reward" or "bestow" or "confiscate" or "execute" or "exile";
    public static string? UnavailableReasonKey(StrategySnapshot view, int sourceId, string kind) => Supports(kind)
        ? new MapCommandAvailability(view).SettlementReasonKey(sourceId, kind) : CommandMenuSkin.ReasonKey("unavailable");

    public AppointmentDraft(GameSession session, int sourceId, string kind)
    {
        this.session = session; View = session.View; SourceId = sourceId; Kind = kind;
        if (UnavailableReasonKey(View, sourceId, kind) is string reason) throw new ArgumentException(reason, nameof(kind));
        var query = new MapCommandAvailability(View);
        Candidates = query.AppointmentPeople(kind).OrderBy(p => p.Id).Select(p => p.Id).ToImmutableArray();
        if (kind == "strategist")
        {
            int Intelligence(int id) => View.Officers.Single(p => p.Id == id).Abilities.Intelligence;
            Candidates = Candidates.OrderByDescending(Intelligence).ThenBy(id => id).ToImmutableArray();
            int? incumbent = View.World.Forces.Single(f => f.Id == View.PlayerForceId).AdviserId;
            Members = Candidates.Where(id => incumbent is null || Intelligence(id) >= Intelligence(incumbent.Value)).Take(1).ToImmutableArray();
        }
        TreasureIds = query.AppointmentTreasures(kind).OrderBy(t => t.Id).Select(t => t.Id).ToImmutableArray();
        if (kind is "bestow" or "confiscate")
        {
            TreasureIds = TreasureIds.OrderBy(id => View.Information.Treasures.Single(t => t.Id == id).Category)
                .ThenByDescending(id => View.Information.Treasures.Single(t => t.Id == id).AbilityBonus).ThenBy(id => id).ToImmutableArray();
            if (TreasureIds.Length == 1) TreasureId = TreasureIds[0];
        }
        OfficePlan = kind == "rank" ? new(View, Candidates, query.AppointmentOffices()) : null;
        Money = View.World.Corps.Single(c => c.OwnerId == View.PlayerForceId && c.IsRoot).Treasury.Money;
        if (kind == "execute")
        {
            Candidates = Candidates.OrderByDescending(id => {
                var person = View.World.Officers.Single(p => p.Id == id);
                return person.OwnerId is null ? -1 : Math.Min(100, person.Loyalty);
            }).ThenBy(id => id).ToImmutableArray();
        }
        if (kind == "exile")
        {
            int Priority(int id) => View.World.Officers.Single(p => p.Id == id).Status switch {
                OfficerStatus.Captive => 0, OfficerStatus.Serving => 1, OfficerStatus.Strategist => 2, OfficerStatus.Governor => 3, _ => 4 };
            long AbilityTotal(int id) { var a = View.Officers.Single(p => p.Id == id).Abilities; return (long)a.Leadership + a.War + a.Intelligence + a.Politics; }
            Candidates = Candidates.OrderBy(Priority).ThenByDescending(AbilityTotal).ThenBy(id => id).ToImmutableArray();
        }
        if (kind is "reward" or "bestow")
            Candidates = Candidates.OrderBy(id => View.World.Officers.Single(p => p.Id == id).Loyalty).ThenBy(id => id).ToImmutableArray();
        if (kind is "reward" or "exile" or "bestow" or "execute" && Candidates.Length == 1)
            Members = Candidates;
    }
    public bool SetMembers(IEnumerable<int> ids)
    {
        var selected = ids.ToImmutableArray();
        if (!IsCurrent || Kind is "rank" or "confiscate" || selected.Length > SelectionLimit ||
            selected.Distinct().Count() != selected.Length || selected.Except(Candidates).Any()) return false;
        Members = (Kind is "reward" or "exile" ? AppointmentOfficerOrder.Selected(View, selected) : selected.Order()).ToImmutableArray(); return true;
    }
    public bool SetTreasure(int id)
    {
        if (!IsCurrent || !TreasureIds.Contains(id)) return false;
        TreasureId = id; return true;
    }
    public bool AssignOffice(int officerId, int? officeId)
    {
        return IsCurrent && OfficePlan?.Assign(officerId, officeId) == true;
    }

    public AppointmentSubmission Submit()
    {
        if (!CanExecute) throw new InvalidOperationException("Invalid or stale appointment preparation.");
        List<DomainEvent> events = [];
        int completed = 0;
        // Reward and exile use separate existing transactions. Stop on a rejection and report actual committed results.
        foreach (int target in Kind is "reward" or "exile" ? Members : ImmutableArray.Create(Members.IsEmpty ? 0 : Members[0]))
        {
            Guid id = Guid.NewGuid(); int force = View.PlayerForceId; long sequence = session.Snapshot.Simulation.NextSequence;
            GameCommand command = Kind switch {
                "strategist" => new AppointAdviserCommand(id, force, sequence, target),
                "reward" => new RewardOfficerCommand(id, force, sequence, target),
                "bestow" => new GrantTreasureCommand(id, force, sequence, TreasureId!.Value, target),
                "confiscate" => new ConfiscateTreasureCommand(id, force, sequence, TreasureId!.Value),
                "execute" => new ExecutePrisonerCommand(id, force, sequence, target),
                "exile" => new ExileOfficerCommand(id, force, sequence, target),
                "rank" => new AssignOfficesCommand(id, force, sequence, Changes),
                _ => throw new InvalidOperationException()
            };
            var result = session.Execute(new("player"), command);
            if (!result.Submission.Accepted) return new(completed, result.Submission.Rejection, events.ToImmutableArray());
            completed++; events.AddRange(result.Events);
        }
        return new(completed, CommandRejection.None, events.ToImmutableArray());
    }
}

public sealed record AppointmentSubmission(int CompletedCommands, CommandRejection Rejection, ImmutableArray<DomainEvent> Events);
