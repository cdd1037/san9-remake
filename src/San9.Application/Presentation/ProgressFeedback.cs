using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

/// <summary>Reports use captured occurrence data; displaying dialogue never creates a report.</summary>
internal static class ProgressFeedback
{
    internal static string PolicyRole(UnitPolicyRespondedEvent response) => response.ReturnOutcome == UnitPolicyReturnOutcome.NoDestination
        ? ProgressPresentationDefinition.ReturnUnavailableRole : PolicyRole(response.Response);

    internal static string PolicyRole(UnitPolicyResponse response) => response switch
    {
        UnitPolicyResponse.AvoidOrdered => "policy-avoid-ordered",
        UnitPolicyResponse.AvoidDespiteOrders => "policy-avoid-despite-orders",
        UnitPolicyResponse.AttackOrdered => "policy-attack-ordered",
        UnitPolicyResponse.AttackDespiteOrders => "policy-attack-despite-orders",
        UnitPolicyResponse.RetreatOrdered => "policy-retreat-ordered",
        UnitPolicyResponse.RetreatOutnumbered => "policy-retreat-outnumbered",
        UnitPolicyResponse.RetreatLowMorale => "policy-retreat-low-morale",
        UnitPolicyResponse.RetreatDespiteOrders => "policy-retreat-despite-orders",
        UnitPolicyResponse.PursueOrdered => "policy-pursue-ordered",
        UnitPolicyResponse.PursueDespiteOrders => "policy-pursue-despite-orders",
        UnitPolicyResponse.ResumeOrders => "policy-resume-orders",
        UnitPolicyResponse.ReturnHome => "policy-return-home",
        UnitPolicyResponse.PostActionNoTarget => "policy-post-action-no-target",
        UnitPolicyResponse.PostActionAttackOrdered => "policy-post-action-attack-ordered",
        UnitPolicyResponse.PostActionAvoidDespiteOrders => "policy-post-action-avoid-despite-orders",
        UnitPolicyResponse.PostActionReturnOrdered => "policy-post-action-return-ordered",
        UnitPolicyResponse.PostActionAttackDespiteOrders => "policy-post-action-attack-despite-orders",
        UnitPolicyResponse.ConstructionAborted => "policy-construction-aborted",
        UnitPolicyResponse.TransportAborted => "policy-transport-aborted",
        UnitPolicyResponse.ReturnTargetChanged => "policy-return-target-changed",
        UnitPolicyResponse.EnterCapturedPlace => "policy-enter-captured-place",
        UnitPolicyResponse.ReturnAfterAction => "policy-return-after-action",
        UnitPolicyResponse.AttackUnitAfterAction => "policy-attack-unit-after-action",
        UnitPolicyResponse.AttackPlaceAfterAction => "policy-attack-place-after-action",
        _ => throw new ArgumentOutOfRangeException(nameof(response))
    };

    internal static bool IsPostAction(UnitPolicyResponse response) => response is UnitPolicyResponse.PostActionNoTarget or
        UnitPolicyResponse.PostActionAttackOrdered or UnitPolicyResponse.PostActionAvoidDespiteOrders or
        UnitPolicyResponse.PostActionReturnOrdered or UnitPolicyResponse.PostActionAttackDespiteOrders;

    internal static bool IsPolicyDecision(UnitPolicyResponse response) => IsPostAction(response) || response is
        UnitPolicyResponse.AttackOrdered or UnitPolicyResponse.AttackDespiteOrders or UnitPolicyResponse.AvoidOrdered or
        UnitPolicyResponse.AvoidDespiteOrders or UnitPolicyResponse.RetreatOrdered or UnitPolicyResponse.RetreatOutnumbered or
        UnitPolicyResponse.RetreatLowMorale or UnitPolicyResponse.RetreatDespiteOrders or UnitPolicyResponse.PursueOrdered or
        UnitPolicyResponse.PursueDespiteOrders or UnitPolicyResponse.ResumeOrders or UnitPolicyResponse.ReturnHome;

    internal static Dictionary<string, MessageBinding> PolicyValues(UnitPolicyRespondedEvent response)
    {
        Dictionary<string, MessageBinding> values = [];
        if (response.Unit.CommanderId is int actor) values["actor"] = ProgressMessageProducer.Person(actor);
        if (response.OtherOfficerId is int other) values["other"] = ProgressMessageProducer.Person(other);
        if (response.HomeId is int home) values["place"] = ProgressMessageProducer.Place(home);
        if (response.Unit.ServicePlaceId is int origin) values["other-place"] = ProgressMessageProducer.Place(origin);
        if (response.PreviousPlaceId is int previous) values["previous-place"] = ProgressMessageProducer.Place(previous);
        return values;
    }

    internal static IReadOnlyList<CommandMessage> PolicyPrompts(StrategySnapshot before, WorldMessageContext context,
        TextCatalog texts, ProgressPresentationDefinition content, UnitPolicyRespondedEvent response, int index)
    {
        if (!response.ReportEligible || !IsPostAction(response.Response) || response.Unit.CommanderId is not int actor) return [];
        string role = PolicyRole(response);
        var occurrence = context.AtOccurrence(response.Clock, response.Unit);
        var random = new StableRandom(StableRandom.DeriveSeed((ulong)before.World.Revision, $"policy-prompt:{index}:{role}"));
        var message = ProgressMessageProducer.Produce(content.Feedback[role], occurrence, texts, PolicyValues(response),
            count => (int)(random.NextUInt64() % (ulong)count));
        return [message with { Speaker = occurrence.Speaker(actor), FocusLocation = MapCoordinates.Cell(response.Unit.Position!.Value) }];
    }

    private static Dictionary<string, MessageBinding> GrowthValues(OfficerAbilityGrownEvent growth) => new()
    {
        ["actor"] = ProgressMessageProducer.Person(growth.OfficerId),
        ["ability"] = new(growth.Ability switch { "leadership" => 0, "war" => 1, "intelligence" => 2, "politics" => 3,
            _ => throw new InvalidDataException("Unknown grown ability.") }),
        ["amount"] = new(growth.Current - growth.Previous), ["current"] = new(growth.Current)
    };

    internal static IReadOnlyList<CommandMessage> GrowthPrompts(StrategySnapshot before, WorldMessageContext context,
        TextCatalog texts, ProgressPresentationDefinition content, IReadOnlyList<DomainEvent> events)
    {
        List<CommandMessage> result = [];
        foreach (var growth in events.OfType<OfficerAbilityGrownEvent>())
        {
            if (!Player(before, growth.Occurrence.ForceId) || events.OfType<ImperialCeremonyResolvedEvent>().Any(e => e.RulerId == growth.OfficerId)) continue;
            var message = ProgressMessageProducer.Produce(content.Feedback["ability-grown"], context.AtOccurrence(growth.Clock), texts,
                GrowthValues(growth), _ => 0);
            result.Add(message);
        }
        return result;
    }

    private static bool Player(StrategySnapshot view, int? force) => force is int id &&
        view.World.Simulation.Controls.Any(c => c.ForceId == id && c.Kind == ControllerKind.Player);

    internal static IReadOnlyList<MapMessageRecord> Records(StrategySnapshot before, StrategySnapshot after,
        WorldMessageContext context, TextCatalog texts, ProgressPresentationDefinition content,
        IReadOnlyList<DomainEvent> events, int index)
    {
        List<MapMessageRecord> records = [];
        var values = new Dictionary<string, MessageBinding>();
        GameClock clock = before.World.Clock;
        MapCell? location = null;
        int? color = null;
        WorldMessageContext occurrence = context;
        void At(PersonOccurrence person) { color = person.ForceId; location = person.Position is { } position ? MapCoordinates.Cell(position)
            : person.PlaceId is int place ? before.PlaceCell(place) ?? after.PlaceCell(place) : null; }
        void Add(string role, MapReportImportance importance = MapReportImportance.Summary)
        {
            var random = new StableRandom(StableRandom.DeriveSeed((ulong)before.World.Revision, $"feedback:{index}:{role}"));
            var message = ProgressMessageProducer.Produce(content.Records[role], occurrence.AtOccurrence(clock), texts, values,
                n => (int)(random.NextUInt64() % (ulong)n));
            records.Add(new($"feedback:{before.World.Revision}:{index}:{role}", message.Message, location,
                MapRecordColors.Resolve(before, after, color), importance));
        }
        switch (events[index])
        {
            case SecretContactReportedEvent contact when Player(before, contact.RecipientForceId):
                clock = contact.Clock; At(contact.Occurrence); color = contact.RecipientForceId;
                values["actor"] = ProgressMessageProducer.Person(contact.OfficerId);
                values["force"] = ProgressMessageProducer.Force(contact.ContactForceId); Add("secret-contact"); break;
            case OfficerAbilityGrownEvent growth when before.World.Simulation.Boundary != SimulationBoundary.AwaitingDecision && Player(before, growth.Occurrence.ForceId):
                clock = growth.Clock; At(growth.Occurrence); values = GrowthValues(growth); Add("ability-grown"); break;
            case BattleConditionChangedEvent { Condition: UnitCondition.Normal, DaysRemaining: 0, Target: { } target } recovered
                when Player(before, target.ForceId) && recovered.PreviousCondition != UnitCondition.HighSpirits:
                clock = recovered.Clock; color = target.ForceId;
                location = target.Position is { } position ? MapCoordinates.Cell(position) : before.PlaceCell(target.Id) ?? after.PlaceCell(target.Id);
                if (target.IsSettlement) { values["place"] = ProgressMessageProducer.Place(target.Id); Add("settlement-recovered"); }
                else if (target.CommanderId is int commander) { values["actor"] = ProgressMessageProducer.Person(commander); Add("unit-recovered"); }
                break;
            case UnitPolicyRespondedEvent policy when policy.ReportEligible && policy.Unit.CommanderId is not null:
                clock = policy.Clock; color = policy.Unit.ForceId; location = MapCoordinates.Cell(policy.Unit.Position!.Value);
                values = PolicyValues(policy);
                Add(PolicyRole(policy)); break;
            case BurnResolvedEvent { Success: true } burn:
                clock = burn.Clock; var burning = burn.Occurrence;
                location = before.PlaceCell(burn.TargetSettlementId) ?? after.PlaceCell(burn.TargetSettlementId);
                values["place"] = ProgressMessageProducer.Place(burn.TargetSettlementId);
                color = burning.TargetForceId;
                if (!Player(before, burning.ForceId) && !Player(before, burning.TargetForceId)) Add("burn-other", MapReportImportance.Detailed);
                values["current"] = new(burning.Durability); values["amount"] = new(burn.DurabilityLoss);
                Add("burn-durability", MapReportImportance.Detailed); break;
            case RecruitmentResolvedEvent recruit:
                clock = recruit.Clock; var recruitment = recruit.Occurrence; At(recruitment.Target);
                values["actor"] = ProgressMessageProducer.Person(recruit.OfficerId);
                values["other"] = ProgressMessageProducer.Person(recruit.TargetOfficerId);
                values["force"] = ProgressMessageProducer.Force(recruitment.ForceId);
                if (recruitment.Target.ForceId is int oldOwner) values["enemy-force"] = ProgressMessageProducer.Force(oldOwner);
                // Conditional meeting text must see the target before recruitment, not its new status.
                occurrence = context.ForWorld(PersonnelPresentation.RecruitmentWorld(before.World, recruit));
                bool own = Player(before, recruitment.ForceId), victim = Player(before, recruitment.Target.ForceId);
                bool captive = recruitment.PreviousStatus == OfficerStatus.Captive;
                if (own)
                {
                    color = recruitment.ForceId;
                    Add(captive || recruitment.PreviousStatus == OfficerStatus.Free ? "recruit-meeting" : "recruit-secret-meeting");
                    if (recruit.Success && captive && recruitment.Target.ForceId is not null)
                    { Add("recruit-captive"); }
                    else Add(recruit.Success ? "recruit-success" : "recruit-failure");
                }
                if (!recruit.Success) break;
                if (!own && captive && victim) { color = recruitment.Target.ForceId; Add("recruit-captive-other", MapReportImportance.Detailed); }
                else if (!own && victim && recruitment.Target.ForceId != recruitment.ForceId) { color = recruitment.Target.ForceId; Add("recruit-lost"); }
                else if (!own) { color = null; Add(captive || recruitment.Target.ForceId is null ? "recruit-other" : "recruit-defected-other", MapReportImportance.Detailed); }
                if (recruitment.TransferredUnit || recruitment.TransferredPlace)
                {
                    if (recruitment.Target.PlaceId is int place) values["place"] = ProgressMessageProducer.Place(place);
                    if (own) { color = recruitment.ForceId; Add(recruitment.TransferredUnit ? "recruit-unit" : "recruit-place"); }
                    if (victim) { color = recruitment.Target.ForceId; Add(recruitment.TransferredUnit ? "recruit-unit-lost" : "recruit-place-lost"); }
                }
                break;
        }
        return records;
    }
}
