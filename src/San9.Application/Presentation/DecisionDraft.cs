using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

public enum DecisionInputKind { Immediate, Person, Country, Investment, Money, Prisoner, Treasure }
public sealed record DecisionPerson(int Id, string NameKey, OfficerAbilities Abilities);
public sealed record DecisionOption(string Role, string Title, DecisionInputKind Input,
    int[] Candidates, uint Minimum, uint Maximum, Func<Guid, long, int?, uint, GameCommand> CreateCommand);
public sealed record DiplomaticDecisionData(Guid TaskId, int RequesterForceId, int TargetForceId, int? TargetSettlementId, int? PrisonerId,
    CompensationOffer? Offer, uint PaymentBalance, int Relation)
{
    public GiftOffer? Gift { get; init; }
}

/// <summary>One pending transaction and its retained presentation. Message variation never consumes simulation randomness.</summary>
public sealed class DecisionDraft
{
    public StrategySnapshot Before { get; }
    public int ForceId { get; }
    public string ControllerId { get; }
    public string Role { get; }
    public DecisionPresentationDefinition Definition { get; }
    public IReadOnlyList<DecisionOption> Options { get; }
    public IReadOnlyList<CommandMessage> Introduction { get; }
    public ProducedMessage Prompt { get; }
    public IReadOnlyList<ProducedMessage> AcceptanceConfirmations { get; }
    public int? PersonId { get; }
    public DecisionPerson? SubjectPerson { get; }
    public DiplomaticDecisionData? Diplomacy { get; }
    private readonly Dictionary<string, MessageBinding> _values;
    private readonly TextCatalog _texts;
    private readonly WorldMessageContext _context;
    private readonly DecisionPresentations _presentation;
    private readonly GameSession _session;
    private readonly ProgressPresentationDefinition? _progress;

    private DecisionDraft(GameSession session, TextCatalog texts, CommandPresentation presentation, string role, int force,
        Dictionary<string, MessageBinding> values, List<DecisionOption> options, int? person, DiplomaticDecisionData? diplomacy)
    {
        _progress = presentation.Progress;
        _session = session; Before = session.View; ForceId = force; Role = role; _texts = texts; _context = session.Messages(texts); _values = values; _presentation = presentation.Decisions;
        ControllerId = Before.World.Simulation.Controls.Single(c => c.ForceId == force && c.Kind == ControllerKind.Player).ControllerId;
        Definition = presentation.Decisions.Events[role]; Options = options;
        foreach (var (name, id) in presentation.Decisions.People) values[name] = Person(id);
        PersonId = person;
        SubjectPerson = person is int personId ? Before.DecisionPerson(personId) : null;
        Diplomacy = diplomacy;
        Prompt = Produce(Definition.Prompt).Message;
        var introduction = Definition.Introduction.ToList();
        if (diplomacy is not null)
        {
            if (!Before.World.Simulation.Controls.Any(c => c.Kind == ControllerKind.Player && c.ForceId == diplomacy.TargetForceId))
                introduction[0] = _presentation.Shared["diplomacy-foreign-arrival"];
            if (role == "attack-request" && !values.ContainsKey("enemy-force")) introduction[^1] = _presentation.Shared["diplomacy-unowned-target"];
            if (role is "attack-request" or "prisoner-return" && values.ContainsKey("adviser")) introduction.Add(_presentation.Shared["diplomacy-adviser"]);
        }
        Introduction = introduction.Select(Produce).ToArray();
        List<ProducedMessage> confirmations = [];
        if (session.GetAllianceParticipationPrompt() is { } alliance)
        {
            foreach (var conflict in alliance.PossibleRequestConflicts.Where(c => c.LiableForceId == ForceId))
            {
                bool againstTarget = conflict.Request.RequesterForceId == alliance.Proposal.TargetForceId;
                values["conflict-force"] = Force(conflict.Request.RequesterForceId);
                confirmations.Add(Produce(_presentation.Shared[againstTarget ? "alliance-responsibility" : "alliance-request-conflict"]).Message);
            }
            var own = alliance.PossibleOrderConflicts.Where(c => c.OwnerId == ForceId).ToArray();
            if (own.Any(c => c.ClearsContact)) confirmations.Add(Produce(_presentation.Shared["alliance-contact-conflict"]).Message);
            if (own.Any(c => c.ChangesOrder)) confirmations.Add(Produce(_presentation.Shared["alliance-order-conflict"]).Message);
        }
        AcceptanceConfirmations = confirmations;
    }

    private static MessageBinding Person(int id) => MessageBinding.Reference(MessageEntityKind.Person, id);
    private static MessageBinding Force(int id) => MessageBinding.Reference(MessageEntityKind.Force, id);
    private CommandMessage Produce(DecisionMessageDefinition definition) => Produce(definition, _context);
    private CommandMessage Produce(DecisionMessageDefinition definition, WorldMessageContext context)
    {
        var inputs = MessageGraph.Requirements(definition.Key, _texts.Messages).Bindings.ToDictionary(slot => slot,
            slot => _values.TryGetValue(definition.Bindings[slot], out var value) ? value :
                throw new InvalidDataException($"Missing decision input: {Role}/{definition.Key}/{slot}/{definition.Bindings[slot]}."));
        int? speaker = definition.Speaker is string speakerRole ? _values[speakerRole].Entity!.Id : null;
        string? illustration = definition.Illustration;
        if (illustration is string named && _values.TryGetValue(named, out var imageBinding) && imageBinding.Text is string imageRole) illustration = imageRole;
        int? illustrationPerson = illustration is "actor" or "other" ? _values[illustration].Entity!.Id : null;
        return new(context.Produce(definition.Key, inputs, Random.Shared.Next), speaker, definition.Sound)
        { IllustrationRole = illustrationPerson is null ? illustration : null, IllustrationPersonId = illustrationPerson };
    }

    public ProducedMessage? ConfirmSelection(int selected) => Definition.SelectionConfirmation is { } message ?
        Produce(message, Definition.Window == DecisionWindowKind.CountrySelection ? _context.PreviewCountryTitle(ForceId, selected) : _context).Message : null;
    public ProducedMessage? ConfirmCancel() => Definition.CancelConfirmation is { } message ? Produce(message).Message : null;
    public CommandMessage MovieIntroduction() => Produce(_presentation.Shared["shanyang-movie-introduction"]);

    public IReadOnlyList<CommandMessage> Results(string choice, IReadOnlyList<DomainEvent> events, int? selected, uint amount)
    {
        if (selected is int id && Role == "tactic-study") _values["actor"] = Person(id);
        if (amount != 0) _values["cost"] = new(amount);
        string branch = Role == "tactic-study" && choice == "choose" ? "accept" : choice;
        if (Role == "shanyang-gong" && choice == "accept" && !events.OfType<ShanyangGongEvent>().Any(e => e.ForceId == ForceId)) branch = "await-title";
        int actorId = _values["actor"].Entity!.Id;
        foreach (var result in events)
        {
            switch (result)
            {
                case SearchResolvedEvent e when e.OfficerId == actorId && Role == "search-recruitment" && choice == "accept": branch = e.Recruited ? "success" : "failure"; break;
                case MerchantInvestmentResolvedEvent e when e.OfficerId == actorId && Role == "merchant-investment":
                    branch = e.Choice == MerchantInvestmentChoice.Invest ? e.Succeeded ? "success" : "failure" : choice;
                    _values["cost"] = new(e.Investment); _values["amount"] = new(e.GrossReturn); break;
                case TigerSearchResolvedEvent e when e.OfficerId == actorId && Role == "tiger": branch = !e.Accepted ? "decline" : e.Result == EventResult.Succeeded ? "success" : "failure"; break;
                case BeastSearchResolvedEvent e when e.OfficerId == actorId && Role == "beast": branch = e.Result == EventResult.Declined ? "decline" : e.Result == EventResult.Succeeded ? "success" : "failure"; break;
                case MysticBookOfferResolvedEvent e when e.SearchingOfficerId == actorId && Role == "mystic-book-offer": if (!e.Accepted) _values["cost"] = _values["decline-cost"]; break;
                case CommunitySearchResolvedEvent e when e.OfficerId == actorId && e.EventId == Role:
                    _values["cost"] = new(e.Cost); _values["amount"] = new(e.MoneyReward); _values["soldiers"] = new(e.Soldiers); break;
                case SearchConflictResolvedEvent e when e.OfficerId == actorId && e.EventId == Role:
                    branch = e.Choice switch { SearchConflictChoice.Decline => "Decline", SearchConflictChoice.Allow => "Allow", SearchConflictChoice.Execute => "Execute", SearchConflictChoice.Pay => "Pay", SearchConflictChoice.Appease => "Appease", _ => e.Result == EventResult.Succeeded ? "success" : "failure" };
                    _values["cost"] = new(e.Cost); _values["amount"] = new(e.MoneyReward); _values["soldiers"] = new(e.Soldiers); break;
                case AttackRequestResolvedEvent e when e.TaskId == Diplomacy?.TaskId && Role == "attack-request":
                    branch = e.Outcome == AttackRequestOutcome.Accepted ? "success" : e.Outcome == AttackRequestOutcome.AlreadyAgreed ? "already-agreed" : "failure"; break;
                case PrisonerReturnResolvedEvent e when e.TaskId == Diplomacy?.TaskId && Role == "prisoner-return": branch = e.Accepted ? "success" : "failure"; break;
                case CompensationQuotedEvent e when e.TaskId == Diplomacy?.TaskId: branch = "quote"; break;
            }
        }
        var resultContext = _session.Messages(_texts);
        CommandMessage ResultMessage(DecisionMessageDefinition definition) => Produce(definition, resultContext);
        var messages = Definition.Results.TryGetValue(branch, out var sequence) ? sequence.Select(ResultMessage).ToList() : [];
        if (Role == "merchant-investment" && branch == "success" && events.OfType<MerchantInvestmentResolvedEvent>().FirstOrDefault(e => e.OfficerId == actorId) is { } investment)
        {
            var changes = events.OfType<ResourceChangedEvent>().Where(e => e.Account == investment.Account && e.Resource == investment.RewardResource).ToArray();
            int last = Array.FindLastIndex(changes, e => e.SourceId == actorId && e.Reason == "merchant-return");
            if (last >= 0 && investment.Account.Kind == ResourceOwnerKind.Corps)
            {
                var treasury = Before.World.Corps.Single(c => c.Id == investment.Account.Id).Treasury;
                long initial = investment.RewardResource == ResourceKind.Money ? treasury.Money : treasury.Food;
                long profit = changes[last].Actual + (investment.RewardResource == ResourceKind.Money ?
                    changes.Where(e => e.SourceId == actorId && e.Reason == "merchant-investment").Sum(e => e.Actual) : 0);
                _values["account"] = MessageBinding.Reference(MessageEntityKind.Corps, investment.Account.Id);
                _values["balance"] = new(initial + changes.Take(last + 1).Sum(e => e.Actual)); _values["profit"] = new(profit);
                messages.Add(ResultMessage(_presentation.Shared[investment.RewardResource == ResourceKind.Money ? "merchant-money-return" : "merchant-food-return"]));
            }
        }
        if (Role == "training-site" && choice == "Accept")
        {
            var growth = events.OfType<TacticProficiencyGrownEvent>().FirstOrDefault(e => e.OfficerId == _values["actor"].Entity!.Id);
            if (growth is not null) messages.Add(Produce(_presentation.Shared["training-growth-" + (growth.Current - growth.Previous > 18 ? 0 : growth.Current - growth.Previous > 8 ? 1 : 2)]));
        }
        if (Role == "alliance")
        {
            if (events.OfType<AllianceFormedEvent>().FirstOrDefault() is { } formed)
            {
                _values["leader-force"] = Force(formed.Alliance.LeaderForceId);
                _values["other"] = Person(Before.World.Forces.Single(f => f.Id == formed.Alliance.LeaderForceId).LeaderId!.Value);
                messages.AddRange(new[] { "alliance-formation-image", "alliance-leader-declaration", "alliance-leader-trust", "alliance-formed" }.Select(role => Produce(_presentation.Shared[role])));
            }
            else if (events.OfType<AllianceFormationFailedEvent>().Any()) messages.Add(Produce(_presentation.Shared["alliance-failed"]));
        }
        if (Diplomacy?.Offer is { Kind: CompensationKind.Prisoner, PrisonerId: int exchange } && branch == "success")
        {
            _values["exchange-person"] = Person(exchange); messages.Add(Produce(_presentation.Shared["diplomacy-exchanged-prisoner"]));
        }
        foreach (var learned in events.OfType<TacticLearnedEvent>().Where(e => _values["actor"].Entity?.Id == e.OfficerId))
        {
            _values["tactic"] = MessageBinding.Reference(MessageEntityKind.Tactic, learned.TacticId);
            if (_presentation.Shared.TryGetValue("learned-tactic", out var definition)) messages.Add(Produce(definition));
        }
        if (_progress is not null)
        {
            messages.AddRange(ProgressFeedback.GrowthPrompts(Before, _context, _texts, _progress, events));
            for (int index = 0; index < events.Count; index++)
                if (events[index] is OfficerInjuryWorsenedEvent)
                    messages.AddRange(OutcomeFeedback.Read(Before, Before, _context, _texts, _progress, events, index, executionMap: false).Messages);
        }
        return messages;
    }

    public static DecisionDraft? Read(GameSession session, TextCatalog texts, CommandPresentation presentation)
    {
        if (session.Snapshot.Simulation.Boundary != SimulationBoundary.AwaitingDecision) return null;
        string role = ""; int force = -1; int? person = null; DiplomaticDecisionData? diplomacy = null;
        var world = session.Snapshot;
        Dictionary<string, MessageBinding> values = [];
        List<DecisionOption> options = [];
        void Context(string kind, int owner, int actor, int? place = null, int? other = null, uint cost = 0)
        {
            role = kind; force = owner; values["force"] = Force(owner); values["actor"] = Person(actor);
            values["ruler"] = Person(world.Forces.Single(f => f.Id == owner).LeaderId!.Value);
            values["other"] = Person(other ?? actor); values["cost"] = new(cost); values["amount"] = new(0);
            if (place is int location) values["place"] = MessageBinding.Reference(world.Settlements.Any(s => s.Id == location) ? MessageEntityKind.Settlement : MessageEntityKind.Region, location);
        }
        void Option(string choice, Func<Guid, long, int?, uint, GameCommand> command, DecisionInputKind input = DecisionInputKind.Immediate,
            IEnumerable<int>? candidates = null, uint min = 0, uint max = 0, uint cost = 0)
        {
            string title = presentation.Decisions.Events[role].Options[choice].Replace("{cost}", cost.ToString(), StringComparison.Ordinal);
            options.Add(new(choice, title, input, candidates?.ToArray() ?? [], min, max, command));
        }
        void Binary(Func<Guid, long, bool, GameCommand> command)
        { Option("accept", (id, seq, _, _) => command(id, seq, true)); Option("decline", (id, seq, _, _) => command(id, seq, false)); }
        void Daily(Guid task, int step) => Binary((id, seq, accept) => new RespondDailyTaskCommand(id, force, seq, task, step, accept));
        void Successor(string kind, int owner, int ruler, IEnumerable<int> candidates, Func<Guid, long, int?, GameCommand> command)
        { Context(kind, owner, ruler); Option("choose", (id, seq, selected, _) => command(id, seq, selected), DecisionInputKind.Person, candidates); Option("decline", (id, seq, _, _) => command(id, seq, null)); }
        if (session.GetCountryTitlePrompt() is { } country)
        {
            Context("country-title", country.ForceId, world.Forces.Single(f => f.Id == country.ForceId).LeaderId!.Value);
            Option("choose", (id, seq, selected, _) => new ChooseCountryTitleCommand(id, force, seq, selected!.Value), DecisionInputKind.Country, country.CandidateIds);
        }
        else if (session.GetEmperorTransferPrompt() is { } emperor)
        { Context("emperor-transfer", emperor.ForceId, emperor.EmperorId, emperor.CityId, emperor.EmperorId); values["dynasty-years"] = new(400); Binary((id, seq, accept) => new RespondEmperorTransferCommand(id, force, seq, emperor.EmperorId, emperor.CityId, accept ? EmperorTransferChoice.Support : EmperorTransferChoice.Depose)); }
        else if (session.GetOfficeRequestPrompt() is { } office)
        { Context("office-request", office.ForceId, office.OfficerId, office.SettlementId); person = office.OfficerId; Binary((id, seq, accept) => new RespondOfficeRequestCommand(id, force, seq, office.OfficerId, accept ? EventChoice.Accept : EventChoice.Reject)); }
        else if (session.GetSearchRecruitmentPrompt() is { } recruit)
        { Context("search-recruitment", recruit.ForceId, recruit.SearchingOfficerId, recruit.PlaceId, recruit.DiscoveredOfficerId); person = recruit.DiscoveredOfficerId; Daily(recruit.TaskId, recruit.Step); }
        else if (session.GetShanyangGongPrompt() is { } shanyang)
        {
            Context(shanyang.ChoosingCountryTitle ? "shanyang-title" : "shanyang-gong", shanyang.ForceId, shanyang.SearchingOfficerId, shanyang.PlaceId, shanyang.EmperorId);
            if (shanyang.ChoosingCountryTitle) Option("choose", (id, seq, selected, _) => new RespondDailyTaskCommand(id, force, seq, shanyang.TaskId, shanyang.Step, true) { CountryTitleId = selected }, DecisionInputKind.Country, shanyang.CountryTitleIds);
            else Daily(shanyang.TaskId, shanyang.Step);
        }
        else if (session.GetMysticBookOfferPrompt() is { } offer)
        { Context("mystic-book-offer", offer.ForceId, offer.SearchingOfficerId, offer.PlaceId, offer.MysticOfficerId, offer.AcceptCost); values["decline-cost"] = new(offer.DeclineCost); values["treasure"] = MessageBinding.Reference(MessageEntityKind.Treasure, offer.TreasureId); Daily(offer.TaskId, offer.Step); }
        else if (session.GetMysticBookReturnPrompt() is { } book)
        { Context("mystic-book-return", book.ForceId, book.SearchingOfficerId, book.PlaceId, book.MysticOfficerId); values["treasure"] = MessageBinding.Reference(MessageEntityKind.Treasure, book.TreasureId); Daily(book.TaskId, book.Step); }
        else if (session.GetScholarInsultPrompt() is { } insult)
        { Context("scholar-insult", insult.ForceId, insult.SearchingOfficerId, insult.PlaceId, insult.ScholarOfficerId); Binary((id, seq, ignore) => new RespondDailyTaskCommand(id, force, seq, insult.TaskId, insult.Step, !ignore)); }
        else if (session.GetLongevityPrompt() is { } longevity)
        { Context("longevity", longevity.ForceId, longevity.SearchingOfficerId, longevity.PlaceId, cost: longevity.Cost); Daily(longevity.TaskId, longevity.Step); }
        else if (session.GetTreasureMonumentPrompt() is { } monument)
        { Context("treasure-monument", monument.ForceId, monument.SearchingOfficerId, monument.PlaceId, cost: monument.Cost); values["treasure"] = MessageBinding.Reference(MessageEntityKind.Treasure, monument.TreasureId); Daily(monument.TaskId, monument.Step); }
        else if (session.GetScholarReturnPrompt() is { } scholar)
        { Context("scholar-return", scholar.ForceId, scholar.SearchingOfficerId, scholar.PlaceId, scholar.ScholarOfficerId, 1000); Daily(scholar.TaskId, scholar.Step); }
        else if (session.GetMerchantInvestmentPrompt() is { } merchant)
        {
            Context("merchant-investment", merchant.ForceId, merchant.OfficerId, merchant.PlaceId);
            values["subject"] = new(0, presentation.Decisions.Subjects["merchant." + merchant.CategoryId]);
            values["trade-image"] = new(0, "decision-trade-" + merchant.CategoryId);
            foreach (var choice in new[] { ("invest", MerchantInvestmentChoice.Invest), ("decline", MerchantInvestmentChoice.Decline), ("execute", MerchantInvestmentChoice.Execute) })
                Option(choice.Item1, (id, seq, _, amount) => new RespondDailyTaskCommand(id, force, seq, merchant.TaskId, merchant.Step, choice.Item2 == MerchantInvestmentChoice.Invest) { MerchantInvestment = new(choice.Item2, amount) },
                    choice.Item2 == MerchantInvestmentChoice.Invest ? DecisionInputKind.Investment : DecisionInputKind.Immediate, min: merchant.MinimumInvestment, max: merchant.MaximumInvestment);
        }
        else if (session.GetCommunitySearchPrompt() is { } community)
        {
            Context(community.EventId, community.ForceId, community.OfficerId, community.PlaceId);
            foreach (var choice in community.Options) Option(choice.Choice.ToString(), (id, seq, _, _) => new RespondDailyTaskCommand(id, force, seq, community.TaskId, community.Step,
                choice.Choice is not (CommunitySearchChoice.Decline or CommunitySearchChoice.Execute))
            { CommunitySearch = choice.Choice }, cost: choice.Cost);
            values["cost"] = new(community.Options.FirstOrDefault(o => o.Cost > 0)?.Cost ?? 0);
            string subject = community.EventId switch
            {
                "disaster-relief" => presentation.Decisions.Subjects["disaster." + community.Season],
                "monument" or "homecoming-sacrifice" => presentation.Decisions.Subjects[community.EventId + "." + community.PlaceId],
                "training-site" => presentation.Decisions.Subjects["tactic-category." + community.TacticCategory],
                "bribe" => presentation.Decisions.Subjects["civic-person." + Random.Shared.Next(2)],
                _ => ""
            };
            values["subject"] = new(0, subject); values["soldiers"] = new(community.OfferedSoldiers);
            if (community.EventId == "training-site") values["training-image"] = new(0, community.TacticCategory == 4 ? "decision-training-water" : "decision-training-land");
            if (community.EventId == "bribe") values["amount"] = new(community.OfferedMoney);
            if (community.RequiredTreasureId is int treasure) values["treasure"] = MessageBinding.Reference(MessageEntityKind.Treasure, treasure);
        }
        else if (session.GetSearchConflictPrompt() is { } conflict)
        {
            Context(conflict.EventId, conflict.ForceId, conflict.OfficerId, conflict.PlaceId, conflict.OtherOfficerId ?? presentation.Decisions.People["citizen"]);
            foreach (var choice in conflict.Options) Option(choice.Choice.ToString(), (id, seq, _, _) => new RespondDailyTaskCommand(id, force, seq, conflict.TaskId, conflict.Step,
                choice.Choice is not (SearchConflictChoice.Decline or SearchConflictChoice.Execute))
            { SearchConflict = choice.Choice }, cost: choice.Cost);
            values["cost"] = new(conflict.Options.FirstOrDefault(o => o.Cost > 0)?.Cost ?? 0);
            string conflictSubject = conflict.EventId switch
            {
                "evildoer" => presentation.Decisions.Subjects["civic-person." + Random.Shared.Next(2)],
                "bandit-suppression" or "bandit-mediation" or "traveler-rescue" => presentation.Decisions.Subjects["bandits." + BanditProvince(conflict.PlaceId)],
                _ => ""
            };
            values["subject"] = new(0, conflictSubject);
            values["bandit-image"] = new(0, conflictSubject is "海贼" or "江贼" ? "decision-bandits-water" : "decision-bandits-land");
        }
        else if (session.GetTigerSearchPrompt() is { } tiger)
        { Context("tiger", tiger.ForceId, tiger.OfficerId, tiger.PlaceId); values["subject"] = new(0, "虎"); Daily(tiger.TaskId, tiger.Step); }
        else if (session.GetBeastSearchPrompt() is { } beast)
        { Context("beast", beast.ForceId, beast.OfficerId, beast.PlaceId); values["subject"] = new(0, "神兽"); Daily(beast.TaskId, beast.Step); }
        else if (session.GetTacticStudyPrompt() is { } study)
        {
            Context("tactic-study", study.ForceId, world.Forces.Single(f => f.Id == study.ForceId).LeaderId!.Value, study.CityId);
            values["tactic"] = MessageBinding.Reference(MessageEntityKind.Tactic, study.TacticId);
            Option("choose", (id, seq, selected, _) => new ChooseTacticStudentCommand(id, force, seq, study.CityId, selected), DecisionInputKind.Person, study.CandidateIds);
            Option("decline", (id, seq, _, _) => new ChooseTacticStudentCommand(id, force, seq, study.CityId, null));
        }
        else if (session.GetSnipingSuccessionPrompt() is { } sniping) Successor("succession", sniping.RespondingForceId, sniping.RulerId, sniping.CandidateIds, (id, seq, selected) => new ChooseSnipingSuccessorCommand(id, force, seq, sniping.RulerId, selected));
        else if (session.GetDuelSuccessionPrompt() is { } duel) Successor("succession", duel.RespondingForceId, duel.RulerId, duel.CandidateIds, (id, seq, selected) => new ChooseDuelSuccessorCommand(id, force, seq, duel.RulerId, selected));
        else if (session.GetCapturedRulerPrompt() is { } captive)
        { Context("captured-ruler", captive.CaptorId, world.Forces.Single(f => f.Id == captive.CaptorId).LeaderId!.Value, other: captive.RulerId); person = captive.RulerId; Binary((id, seq, accept) => new ChooseCapturedRulerDispositionCommand(id, force, seq, captive.RulerId, accept ? CapturedRulerAction.Release : CapturedRulerAction.Execute)); }
        else if (session.GetExecutionSuccessionPrompt() is { } execution) Successor("succession", execution.RespondingForceId, execution.RulerId, execution.CandidateIds, (id, seq, selected) => new ChooseExecutionSuccessorCommand(id, force, seq, execution.RulerId, selected));
        else if (session.GetAllianceParticipationPrompt() is { } alliance)
        {
            int owner = alliance.Proposal.RespondingForceId!.Value;
            Context("alliance", owner, world.Forces.Single(f => f.Id == owner).LeaderId!.Value);
            values["target-force"] = Force(alliance.Proposal.TargetForceId);
            Binary((id, seq, accept) => new AllianceParticipationCommand(id, force, seq, alliance.Proposal.TargetForceId, accept));
        }
        else if (session.GetAttackRequestPrompt() is { } attack)
            Diplomacy("attack-request", attack.TaskId, attack.Step, attack.RespondingForceId, attack.RequesterForceId, attack.TargetForceId, attack.Offer, attack.TargetSettlementId, null);
        else if (session.GetPrisonerReturnPrompt() is { } prisoner)
            Diplomacy("prisoner-return", prisoner.TaskId, prisoner.Step, prisoner.RespondingForceId, prisoner.RequesterForceId, prisoner.TargetForceId, prisoner.Offer, null, prisoner.PrisonerId);
        else if (world.Simulation.PendingDailyTask is { } daily &&
            world.Simulation.Tasks.Single(t => t.Id == daily.RemainingTaskIds[0]) is { } task &&
            (task.Gift is not null || task.Surrender is not null))
        {
            int target = task.Gift?.TargetForceId ?? task.Surrender!.TargetForceId;
            Diplomacy(task.Gift is null ? "surrender" : "gift", task.Id, task.ResolutionStep,
                daily.RespondingForceId, task.ForceId, target, null, null, null, task.Gift?.Offer);
        }
        else throw new InvalidDataException("The pending decision has no presentation handler.");
        return new(session, texts, presentation, role, force, values, options, person, diplomacy);

        int BanditProvince(int place)
        {
            int city = session.View.RegionCityIds.GetValueOrDefault(place, place);
            return session.View.CityProvinceIds.TryGetValue(city, out var province) ? province :
                throw new InvalidDataException("Missing encounter province.");
        }

        void Diplomacy(string kind, Guid task, int step, int responding, int requester, int target, CompensationOffer? offer, int? place, int? prisoner, GiftOffer? gift = null)
        {
            Context(kind, responding, world.Simulation.Tasks.Single(t => t.Id == task).OfficerId, place, prisoner);
            values["target-force"] = Force(target); values["requester-force"] = Force(requester);
            values["force"] = Force(target); values["host"] = Person(world.Forces.Single(f => f.Id == target).LeaderId!.Value);
            values["requester-ruler"] = Person(world.Forces.Single(f => f.Id == requester).LeaderId!.Value);
            if (world.Forces.Single(f => f.Id == target).AdviserId is int adviser) values["adviser"] = Person(adviser);
            uint balance = world.Corps.Single(c => c.OwnerId == requester && c.IsRoot).Treasury.Money;
            int relation = world.Relations.SingleOrDefault(r => r.FirstId == Math.Min(requester, target) && r.SecondId == Math.Max(requester, target))?.Value ?? 0;
            diplomacy = new(task, requester, target, place, prisoner, offer, balance, relation) { Gift = gift };
            if (kind is "gift" or "surrender") { Daily(task, step); return; }
            if (place is int targetPlace && world.Settlements.Single(s => s.Id == targetPlace).OwnerId is int enemy) values["enemy-force"] = Force(enemy);
            if (offer is not null) { values["cost"] = new(offer.Money); Daily(task, step); return; }
            Option("free", (id, seq, _, _) => Reply(id, seq, new(CompensationKind.Free, 0, null, null)));
            Option("decline", (id, seq, _, _) => Reply(id, seq, new(CompensationKind.Refuse, 0, null, null)));
            if (balance > 0) Option("money", (id, seq, _, amount) => Reply(id, seq, new(CompensationKind.Money, amount, null, null)), DecisionInputKind.Money, min: 1, max: Math.Min(balance, 1000000));
            var prisoners = world.Officers.Where(p => p.Status == OfficerStatus.Captive && p.OwnerId == target &&
                (p.SettlementId is int city ? world.Settlements.Single(s => s.Id == city).OwnerId : p.UnitId is int unit ? world.Units.Single(u => u.Id == unit).OwnerId : null) == requester).Select(p => p.Id).ToArray();
            if (prisoners.Length > 0) Option("prisoner", (id, seq, selected, _) => Reply(id, seq, new(CompensationKind.Prisoner, 0, selected, null)), DecisionInputKind.Prisoner, prisoners);
            int ruler = world.Forces.Single(f => f.Id == requester).LeaderId!.Value;
            var treasures = world.Treasures.Where(t => t.Discovered && t.HolderId == ruler).Select(t => t.Id).ToArray();
            if (treasures.Length > 0) Option("treasure", (id, seq, selected, _) => Reply(id, seq, new(CompensationKind.Treasure, 0, null, selected)), DecisionInputKind.Treasure, treasures);
            GameCommand Reply(Guid id, long seq, CompensationOffer value) => new RespondDailyTaskCommand(id, force, seq, task, step, true) { Offer = value };
        }
    }
}
