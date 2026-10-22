using San9.Core.Simulation;

namespace San9.Application.Presentation;

/// <summary>Local terms survive picker cancellation; only explicit confirmation creates a submitted response.</summary>
public sealed class DiplomacyCounterofferDraft
{
    private readonly DecisionDraft _decision;
    public Guid TaskId { get; }
    public DecisionOption Option { get; private set; }
    public int? SelectedId => Offer.PrisonerId ?? Offer.TreasureId;
    public uint Amount => Offer.Money;
    public CompensationOffer Offer { get; private set; }
    public bool HasCondition => Offer.Kind != CompensationKind.Free;

    public DiplomacyCounterofferDraft(DecisionDraft decision)
    {
        if (decision.Role is not ("attack-request" or "prisoner-return") || decision.Diplomacy is not { Offer: null } data)
            throw new ArgumentException("The decision is not awaiting a counteroffer.", nameof(decision));
        _decision = decision; TaskId = data.TaskId;
        Option = decision.Options.Single(o => o.Role == "free");
        Offer = new(CompensationKind.Free, 0, null, null);
    }

    public void Select(string role, int? selectedId = null, uint amount = 0)
    {
        var option = _decision.Options.SingleOrDefault(o => o.Role == role);
        if (option is null || role is not ("free" or "prisoner" or "money" or "treasure") ||
            (option.Input is DecisionInputKind.Prisoner or DecisionInputKind.Treasure
                ? selectedId is not int id || !option.Candidates.Contains(id) || amount != 0
                : selectedId is not null || (option.Input == DecisionInputKind.Money
                    ? amount < option.Minimum || amount > option.Maximum : amount != 0)))
            throw new ArgumentException("Invalid counteroffer selection.", nameof(role));
        var response = (RespondDailyTaskCommand)option.CreateCommand(Guid.Empty, _decision.Before.World.Simulation.NextSequence, selectedId, amount);
        Option = option; Offer = response.Offer!;
    }
}
