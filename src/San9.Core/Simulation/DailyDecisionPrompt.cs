using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record DailyDecisionPrompt(Guid TaskId, int Step, string Kind, int RequesterForceId,
    int RespondingForceId, CompensationOffer? Offer, ImmutableArray<CompensationOffer> LegalOffers,
    long SpendingMoney, int Relation, int? TargetSettlementId, bool CanFulfill);

public sealed partial class SimulationEngine
{
    public DailyDecisionPrompt? GetDailyDecisionPrompt()
    {
        if (_world.IsExecuting) throw new InvalidOperationException("Simulation is executing.");
        if (Snapshot.Simulation.PendingDailyTask is not { } pending) return null;
        var task = Snapshot.Simulation.Tasks.Single(t => t.Id == pending.RemainingTaskIds[0]);
        var world = PreparationWorld();
        int responding = pending.RespondingForceId;
        int? target = task.AttackRequest?.TargetForceId ?? task.PrisonerReturn?.TargetForceId;
        var offers = ImmutableArray.CreateBuilder<CompensationOffer>();
        if (task.ResolutionStep == 0 && target is int other)
        {
            // Representative negotiable values, validated by the same public payment constraints.
            // This query does not inspect mission success rolls, acceptance thresholds or future results.
            foreach (var offer in new CompensationOffer[] { new(CompensationKind.Free, 0, null, null),
                new(CompensationKind.Money, 1000, null, null), new(CompensationKind.Money, 3000, null, null) })
                if (PrisonerCompensation.Payable(world, task.ForceId, other, offer, _rules.Treasures)) offers.Add(offer);
            if (task.PrisonerReturn is { } prisoner)
            {
                uint value = PrisonerCompensation.Value(world, task.ForceId, task.OfficerId, other, prisoner.PrisonerId);
                var ransom = new CompensationOffer(CompensationKind.Money, value, null, null);
                if (PrisonerCompensation.Payable(world, task.ForceId, other, ransom, _rules.Treasures)) offers.Add(ransom);
            }
            if (PrisonerCompensation.ExchangeCandidate(world, task.ForceId, other) is int exchange &&
                PrisonerReturnDiplomacy.CanRecover(world, exchange, _rules))
                offers.Add(new(CompensationKind.Prisoner, 0, exchange, null));
        }
        int account = world.Corps.Values.Single(c => c.OwnerId == responding && c.IsRoot).Id;
        var outlook = EconomicOutlook.Read(Snapshot, _rules.Economy.Salaries.ToDictionary(s => s.OfficeId, s => s.Amount), responding, account);
        return new(task.Id, task.ResolutionStep, task.Kind, task.ForceId, responding,
            task.AttackRequest?.Offer ?? task.PrisonerReturn?.Offer, offers.ToImmutable(),
            Math.Max(0, outlook.Money.Balance - outlook.Money.Expenditure),
            MilitaryDiplomacy.Relation(world, task.ForceId, target ?? responding), task.AttackRequest?.TargetSettlementId,
            task.AttackRequest is not { } request || AttackRequestDiplomacy.CanFulfill(world, task.ForceId,
                request.TargetForceId, request.TargetSettlementId, _rules));
    }

    public EconomicOutlook GetAccountOutlook(int forceId, int accountId) => EconomicOutlook.Read(Snapshot,
        _rules.Economy.Salaries.ToDictionary(s => s.OfficeId, s => s.Amount), forceId, accountId);
}
