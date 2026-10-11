using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.AI;

public sealed record AutomaticResponseFailure(string Code, string Kind, string Identity, string ControllerId,
    CommandRejection Rejection = CommandRejection.None);

internal sealed class AutomaticResponseException(AutomaticResponseFailure failure) : Exception(failure.Code)
{
    internal AutomaticResponseFailure Failure { get; } = failure;
}

/// <summary>Mandatory responses have their own bounded evaluation allowance, independent of turn work.</summary>
internal static class AutomaticResponseRunner
{
    internal static void Run(SimulationEngine engine, List<DomainEvent> events, List<WorldSnapshot>? frames = null)
    {
        HashSet<string> answered = [];
        while (engine.GetPendingResponse() is { Owner.Kind: ControllerKind.Automatic } response)
        {
            if (!answered.Add(response.Identity)) Fail(response, "response-no-progress");
            CommandExecutionResult result;
            try
            {
                var command = Choose(engine, response);
                result = engine.Execute(new(response.Owner.ControllerId), command, frames is not null);
            }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException)
            {
                throw new AutomaticResponseException(new("response-rule-fault", response.Kind, response.Identity,
                    response.Owner.ControllerId));
            }
            if (!result.Submission.Accepted) Fail(response, "response-rejected", result.Submission.Rejection);
            events.AddRange(result.Events);
            if (frames is not null)
                foreach (var frame in result.PresentationFrames)
                {
                    if (frames.Count > 0 && frames[^1].Clock == frame.Clock) frames[^1] = frame;
                    else frames.Add(frame);
                }
        }
    }

    internal static GameCommand Choose(SimulationEngine engine, ResponseRequest response, int evaluationBudget = 2)
    {
        var snapshot = engine.Snapshot;
        var id = new Guid(Convert.FromHexString(CanonicalJson.Hash(new { snapshot.Simulation.RunSeed,
            response.Identity, response.Owner.ControllerId, snapshot.Simulation.NextSequence })).AsSpan(0, 16));
        if (response.Kind == "captured-ruler" && engine.GetCapturedRulerPrompt() is { } captive)
        {
            // Release is the explicit low-budget default. Optional comparison samples only the analysis stream.
            var action = CapturedRulerAction.Release;
            if (evaluationBudget >= 2)
            {
                var random = AiPlanningContext.Create(AiObservation.FromSnapshot(response.Owner.ForceId, snapshot),
                    $"response:{response.Kind}:{captive.RulerId}", new(2, 0)).CreateAnalysisRandom();
                if (captive.IsAllianceTarget || random.NextUInt64() % 100 < (ulong)ExecutionChance(captive))
                    action = CapturedRulerAction.Execute;
            }
            if (!captive.LegalActions.Contains(action)) Fail(response, "response-no-legal-option");
            return new ChooseCapturedRulerDispositionCommand(id, response.Owner.ForceId,
                snapshot.Simulation.NextSequence, captive.RulerId, action);
        }
        if (response.Kind == "execution-succession" && engine.GetExecutionSuccessionPrompt() is { } succession)
        {
            if (succession.CandidateIds.IsEmpty) Fail(response, "response-no-legal-option");
            // The rule-defined automatic succession order is an explicit legal option, even with zero search.
            return new ChooseExecutionSuccessorCommand(id, response.Owner.ForceId,
                snapshot.Simulation.NextSequence, succession.RulerId, null);
        }
        if (response.Kind == "sniping-succession" && engine.GetSnipingSuccessionPrompt() is { } sniping)
            return new ChooseSnipingSuccessorCommand(id, response.Owner.ForceId, snapshot.Simulation.NextSequence, sniping.RulerId, null);
        if (response.Kind == "duel-succession" && engine.GetDuelSuccessionPrompt() is { } duel)
            return new ChooseDuelSuccessorCommand(id, response.Owner.ForceId, snapshot.Simulation.NextSequence, duel.RulerId, null);
        if (engine.GetOfficeRequestPrompt() is { } office)
            return new RespondOfficeRequestCommand(id, response.Owner.ForceId, snapshot.Simulation.NextSequence, office.OfficerId, EventChoice.Accept);
        if (engine.GetEmperorTransferPrompt() is { } emperor)
            return new RespondEmperorTransferCommand(id, response.Owner.ForceId, snapshot.Simulation.NextSequence,
                emperor.EmperorId, emperor.CityId, EmperorTransferChoice.Support);
        if (engine.GetCountryTitlePrompt() is { } title)
            return new ChooseCountryTitleCommand(id, response.Owner.ForceId, snapshot.Simulation.NextSequence, title.CandidateIds[0]);
        if (engine.GetAllianceParticipationPrompt() is { } alliance)
        {
            int target = alliance.Proposal.TargetForceId;
            int relation = snapshot.Relations.SingleOrDefault(r => r.FirstId == Math.Min(target, response.Owner.ForceId) &&
                r.SecondId == Math.Max(target, response.Owner.ForceId))?.Value ?? 0;
            return new AllianceParticipationCommand(id, response.Owner.ForceId, snapshot.Simulation.NextSequence, target,
                relation < 60 && alliance.Proposal.EligibleMemberForceIds.Contains(response.Owner.ForceId));
        }
        if (engine.GetTacticStudyPrompt() is { } study)
            return new ChooseTacticStudentCommand(id, response.Owner.ForceId, snapshot.Simulation.NextSequence, study.CityId,
                study.CandidateIds.IsEmpty ? null : study.CandidateIds[0]);
        if (response.Kind == "daily-task" && engine.GetDailyDecisionPrompt() is { } daily)
            return Daily(engine, id, response, snapshot.Simulation.NextSequence, daily);
        Fail(response, "response-unsupported");
        throw new InvalidOperationException("Unreachable response dispatch.");
    }

    private static RespondDailyTaskCommand Daily(SimulationEngine engine, Guid id, ResponseRequest request, long sequence, DailyDecisionPrompt prompt)
    {
        int force = request.Owner.ForceId;
        var response = new RespondDailyTaskCommand(id, force, sequence, prompt.TaskId, prompt.Step, false);
        if (prompt.Kind is "diplomacy-attack-request" or "diplomacy-prisoner-return")
        {
            if (prompt.Step == 0)
            {
                bool cooperative = prompt.Kind == "diplomacy-prisoner-return" || prompt.CanFulfill && prompt.TargetSettlementId is int target &&
                    engine.Snapshot.Settlements.FirstOrDefault(s => s.Id == target)?.OwnerId is int owner &&
                    MilitaryDiplomacy.AutomaticEnemy(engine.Snapshot, force, owner);
                var offer = prompt.LegalOffers.FirstOrDefault(o => o.Kind == CompensationKind.Prisoner) ?? (prompt.Relation >= 80 ? prompt.LegalOffers.FirstOrDefault(o => o.Kind == CompensationKind.Free)
                    : prompt.LegalOffers.Where(o => o.Kind == CompensationKind.Money).OrderBy(o => o.Money).FirstOrDefault()
                        ?? prompt.LegalOffers.FirstOrDefault());
                return response with { Accept = cooperative && offer is not null, Offer = cooperative ? offer : null };
            }
            bool accept = prompt.Offer is { Kind: CompensationKind.Free or CompensationKind.Prisoner } ||
                prompt.Offer is { Kind: CompensationKind.Money } payment && payment.Money <= prompt.SpendingMoney / 4 ||
                prompt.Offer is { Kind: CompensationKind.Treasure, TreasureId: int treasure } &&
                    engine.GetTreasureDefinition(treasure) is { LoyaltyBonus: <= 10 };
            return response with { Accept = accept };
        }
        if (engine.GetCommunitySearchPrompt() is { } community)
        {
            var choice = community.Options.Where(o => o.Choice is not (CommunitySearchChoice.Execute or CommunitySearchChoice.DemandMoney))
                .Where(o => o.Cost <= Spending(community.Account, o.Resource))
                .OrderBy(o => o.Choice == CommunitySearchChoice.Decline).ThenBy(o => o.Cost).First();
            return response with { CommunitySearch = choice.Choice,
                Accept = choice.Choice is not (CommunitySearchChoice.Decline or CommunitySearchChoice.Execute) };
        }
        if (engine.GetSearchConflictPrompt() is { } conflict)
        {
            var choice = conflict.Options.Where(o => o.Choice != SearchConflictChoice.Execute)
                .Where(o => o.Cost <= Spending(conflict.Account, ResourceKind.Money))
                .OrderByDescending(o => o.SuccessChance ?? (o.Choice == SearchConflictChoice.Decline ? 0 : 100))
                .ThenBy(o => o.Cost).First();
            return response with { SearchConflict = choice.Choice,
                Accept = choice.Choice is not (SearchConflictChoice.Decline or SearchConflictChoice.Execute) };
        }
        if (engine.GetMerchantInvestmentPrompt() is { } merchant)
        {
            bool invest = merchant.SuccessChance >= 60 && merchant.MaximumInvestment >= merchant.MinimumInvestment &&
                Spending(merchant.Account, ResourceKind.Money) >= merchant.MinimumInvestment;
            return response with { Accept = invest, MerchantInvestment = new(invest ? MerchantInvestmentChoice.Invest : MerchantInvestmentChoice.Decline,
                invest ? merchant.MinimumInvestment : 0) };
        }
        if (engine.GetShanyangGongPrompt() is { } shanyang)
            return response with { Accept = true, CountryTitleId = shanyang.ChoosingCountryTitle ? shanyang.CountryTitleIds[0] : null };
        if (engine.GetTigerSearchPrompt() is { } tiger) return response with { Accept = tiger.BattleChance >= 60 };
        if (engine.GetBeastSearchPrompt() is { } beast) return response with { Accept = beast.BattleChance >= 60 };
        if (engine.GetScholarInsultPrompt() is not null) return response;
        if (engine.GetMysticBookReturnPrompt() is { } book) return response with { Accept = book.DeclineRewardTacticId is null };
        if (engine.GetMysticBookOfferPrompt() is { } mystic)
            return response with { Accept = Spending(mystic.Account, ResourceKind.Money) >= mystic.AcceptCost };
        if (engine.GetLongevityPrompt() is { } longevity)
            return response with { Accept = Spending(longevity.Account, ResourceKind.Money) >= longevity.Cost };
        if (engine.GetTreasureMonumentPrompt() is { } monument)
            return response with { Accept = Spending(monument.Account, ResourceKind.Money) >= monument.Cost };
        if (engine.GetSearchRecruitmentPrompt() is not null || engine.GetScholarReturnPrompt() is not null)
            return response with { Accept = true };
        // Gift and surrender retain the public automatic outcome rule; these prompts only arise for a player force.
        Fail(request, "response-unsupported");
        throw new InvalidOperationException("Unreachable response dispatch.");

        long Spending(ResourceAccount account, ResourceKind resource)
        {
            var outlook = engine.GetAccountOutlook(force, account.Id);
            var forecast = resource == ResourceKind.Money ? outlook.Money : outlook.Food;
            return Math.Max(0, forecast.Balance - forecast.Expenditure);
        }
    }

    internal static int ExecutionChance(CapturedRulerPrompt captive)
    {
        var a = captive.RulerAbilities;
        int best = Math.Max(Math.Max(a.War, a.Intelligence), Math.Max(a.Politics, a.Leadership));
        return Math.Max(0, 70 - captive.CaptorBaseTrust / 10 - best / 2 - captive.RelationValue);
    }

    private static void Fail(ResponseRequest response, string code, CommandRejection rejection = CommandRejection.None) =>
        throw new AutomaticResponseException(new(code, response.Kind, response.Identity, response.Owner.ControllerId, rejection));
}
