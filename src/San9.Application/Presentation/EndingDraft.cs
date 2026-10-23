using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Presentation;

/// <summary>Selection belongs to the scene until one complete ending operation is submitted.</summary>
public sealed class EndingDraft
{
    private readonly GameSession _session;
    private readonly TextCatalog _texts;
    private readonly DecisionPresentations _presentation;
    private WorldMessageContext _messages;
    private readonly int _winner;
    private readonly Dictionary<string, MessageBinding> _values;
    private readonly List<int> _selected = [];
    private static readonly string[] Offices = ["chancellor", "marshal", "protector", "successor"];
    public EndingSnapshot Snapshot { get; }
    public StrategySnapshot View { get; }
    public int SelectionStep => _selected.Count;
    public string CurrentOffice => Offices[SelectionStep];
    public bool NeedsOfficeholders => Snapshot.Officers.Length >= 5;
    public EndingPolicy Policy { get; set; }
    public IReadOnlyList<int> Candidates => Snapshot.Officers.Select(o => o.Id)
        .Where(id => id != Snapshot.RulerId && !_selected.Contains(id)).ToArray();
    public EndingDraft(GameSession session, TextCatalog texts, DecisionPresentations presentation)
    {
        _session = session; _texts = texts; _presentation = presentation; _messages = session.Messages(texts); View = session.View;
        Snapshot = session.GetEndingPrompt() ?? throw new InvalidOperationException("No ending selection is pending.");
        int winner = session.GetTerminalResult().WinningForceId!.Value;
        _winner = winner;
        _values = new()
        {
            ["force"] = MessageBinding.Reference(MessageEntityKind.Force, winner),
            ["ruler"] = MessageBinding.Reference(MessageEntityKind.Person, Snapshot.RulerId),
            ["year"] = new(View.World.Clock.Year),
            ["succession-year"] = new(View.World.Clock.Year + 15),
            ["fallen-year"] = new(View.World.Clock.Year + 18)
        };
        foreach (var (name, offset) in new[] { ("two", 2), ("three", 3), ("five", 5), ("six", 6), ("ten", 10) }) _values["year-" + name] = new(View.World.Clock.Year + offset);
    }
    public void Select(int person)
    {
        if (SelectionStep >= 4 || !Candidates.Contains(person)) throw new ArgumentOutOfRangeException(nameof(person));
        _values[CurrentOffice] = MessageBinding.Reference(MessageEntityKind.Person, person); _selected.Add(person);
    }
    public void Back()
    {
        if (SelectionStep == 0) return;
        _selected.RemoveAt(SelectionStep - 1); _values.Remove(CurrentOffice);
    }
    public EndingSelection Selection => new(Policy, NeedsOfficeholders && SelectionStep == 4 ?
        new(_selected[0], _selected[1], _selected[2], _selected[3]) : null);
    public EndingOperation CreateOperation()
    {
        _ = EndingResolution.Resolve(Snapshot, Selection.Officeholders, Policy);
        return new(Selection);
    }
    public CommandMessage Message(string role) => Message(role, _messages);
    private CommandMessage Message(string role, WorldMessageContext context)
    {
        var definition = _presentation.Shared["ending-" + role];
        var inputs = MessageGraph.Requirements(definition.Key, _texts.Messages).Bindings.ToDictionary(slot => slot, slot => _values[definition.Bindings[slot]]);
        return new(context.Produce(definition.Key, inputs, Random.Shared.Next),
            definition.Speaker is string speaker ? _values[speaker].Entity!.Id : null, definition.Sound)
        { IllustrationRole = definition.Illustration };
    }
    public IReadOnlyList<CommandMessage> AppointmentMessages() => new[] { "appoint-ruler", "appoint-chancellor", "appoint-marshal", "appoint-protector", "appoint-successor", "successor-reply" }.Select(Message).ToArray();
    public CommandMessage PreparationMessage() => Message(NeedsOfficeholders ? "prepare-offices" : "prepare-undersized");
    public IReadOnlyList<int> CountryTitleCandidates(IReadOnlyCollection<int> replacedTitles)
    {
        int? title = View.World.Forces.Single(f => f.Id == _winner).CountryTitleId;
        if (title is int current && !replacedTitles.Contains(current)) return [];
        var occupied = View.World.Forces.Where(f => f.Id != _winner && !f.IsDormant && !f.IsEliminated)
            .Select(f => f.CountryTitleId).OfType<int>().ToHashSet();
        return View.Information.CountryTitles.Where(t => t.Rule.ManualSelectionAllowed && !occupied.Contains(t.Id)).Select(t => t.Id).ToArray();
    }
    public CommandMessage PreviewCountryTitle(int title, IReadOnlyCollection<int> replacedTitles)
    {
        if (!CountryTitleCandidates(replacedTitles).Contains(title)) throw new ArgumentOutOfRangeException(nameof(title));
        return Message("confirm-country-title", _messages.PreviewCountryTitle(_winner, title));
    }
    public void SelectCountryTitle(int title, IReadOnlyCollection<int> replacedTitles)
    {
        if (!CountryTitleCandidates(replacedTitles).Contains(title)) throw new ArgumentOutOfRangeException(nameof(title));
        _messages = _messages.PreviewCountryTitle(_winner, title);
    }
    public IReadOnlyList<CommandMessage> Results() => ResultRoles().Select(Message).ToArray();
    public IReadOnlyList<(string Role, CommandMessage Message)> Sequence() => ResultRoles().Select(role => (role, Message(role))).ToArray();
    private IReadOnlyList<string> ResultRoles()
    {
        var result = _session.Ending ?? throw new InvalidOperationException("Ending has not been published.");
        List<string> messages = ["introduction"];
        messages.AddRange(Chapters(result));
        if (NeedsOfficeholders)
        {
            bool stable = result.StableSuccession ?? true;
            messages.Add("succession-year");
            bool weakChancellor = Snapshot.Officers.Single(o => o.Id == _selected[0]).Abilities.Politics < 80;
            bool initialFailure = result.DecisiveStage == 1 || Policy == EndingPolicy.DomesticProsperity && result.DecisiveStage == 3;
            messages.Add(stable ? "stable-succession" : weakChancellor && initialFailure ? "unstable-succession" : "successor-conflict");
        }
        string outcome = result.Outcome switch
        {
            EndingOutcome.GreatEmpire => "great-empire",
            EndingOutcome.CommercialPower => "commercial-power",
            EndingOutcome.CulturalPower => "cultural-power",
            EndingOutcome.CityState => "city-state",
            EndingOutcome.OrdinaryState => "ordinary-state",
            EndingOutcome.SouthernDynasty => "southern-dynasty",
            EndingOutcome.DividedState => "divided-state",
            EndingOutcome.FallenState => "fallen-state",
            _ => throw new InvalidDataException("Unknown ending outcome.")
        };
        messages.Add("result-" + outcome); return messages;
    }

    private IEnumerable<string> Chapters(EndingResult result)
    {
        bool expansion = Policy == EndingPolicy.ContinentalExpansion;
        yield return expansion ? "expansion-start" : "domestic-start";
        if (!NeedsOfficeholders) { yield return expansion ? "expansion-undersized" : "domestic-undersized"; yield break; }
        // The published domain result decides which stages were reached. These tests only select
        // wording explaining a failed stage; they cannot change the outcome or world state.
        if (result.DecisiveStage == 1)
        {
            yield return expansion ? Snapshot.SoldiersAndWounded < 300000 ? "expansion-soldier-shortage" : "expansion-leadership-shortage" :
                Snapshot.DomesticDurability < 1500 ? "domestic-durability-shortage" : "domestic-knowledge-shortage"; yield break;
        }
        yield return expansion ? "expansion-army-ready" : "domestic-building-ready";
        yield return expansion ? "expansion-west-start" : "domestic-government-start";
        if (result.DecisiveStage == 2)
        {
            yield return expansion ? Snapshot.ExpansionPublicOrder < 2000 ? "expansion-order-shortage" : "expansion-war-shortage" :
                Snapshot.Trust < 950 ? "domestic-trust-shortage" : "domestic-affinity-conflict"; yield break;
        }
        yield return expansion ? "expansion-west-ready" : "domestic-government-ready";
        yield return expansion ? "expansion-trade-start" : "domestic-defense-start";
        if (result.DecisiveStage == 3)
        {
            yield return expansion ? Snapshot.ExpansionCommerce < 2500 ? "expansion-commerce-shortage" : "expansion-naval-shortage" :
                Snapshot.Money < 300000 || Snapshot.Food < 750000 ? "domestic-resource-shortage" : "domestic-leadership-shortage"; yield break;
        }
        yield return expansion ? "expansion-trade-ready" : "domestic-defense-ready";
        yield return expansion ? "expansion-final-start" : "domestic-culture-start";
        yield return expansion ? result.Outcome == EndingOutcome.GreatEmpire ? "expansion-empire" : Snapshot.HasLivingBarbarianForce ? "expansion-barbarians" : "expansion-tactic-shortage" :
            result.Outcome == EndingOutcome.CulturalPower ? "domestic-culture-ready" : Snapshot.DomesticPublicOrder < 4000 ? "domestic-order-shortage" : "domestic-treasure-shortage";
    }
}
