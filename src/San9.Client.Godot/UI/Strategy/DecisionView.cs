using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Simulation;

namespace San9.Client.Godot;

/// <summary>Owns one prompt's navigation. Closing a child never silently answers the encounter.</summary>
public partial class DecisionView : FixedPage
{
    public DecisionDraft Draft { get; set; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public CommandPresentation Presentation { get; init; } = null!;
    public string ContentRoot { get; init; } = "";
    public Func<Settings.MessageSpeed> ReadMessageSpeed { get; init; } = () => Settings.MessageSpeed.Normal;
    public bool SoundEnabled { get; init; }
    public bool SkipIntroduction { get; init; }
    public Func<string,bool,IDisposable>? BeginMusic { get; init; }
    private IDisposable? _music;
    private DiplomacyScene? _meeting;
    public Func<GameCommand, string, bool> Submit { get; init; } = null!;
    public Action Finished { get; init; } = null!;
    public ISceneMedia Media { get; init; } = new SilentSceneMedia();
    public bool Pending { get; private set; }
    private Control? _child;
    private DecisionOption? _answer;
    private int? _selected;
    private uint _amount;
    private DiplomacyCounterofferDraft? _counteroffer;
    private long _sequence;
    private readonly CancellationTokenSource _lifetime = new();
    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        _sequence = Draft.Before.World.Simulation.NextSequence;
        if (Draft.Meeting is not null)
        {
            _meeting = new() { Definition = Presentation.Diplomacy ?? throw new InvalidDataException("Missing diplomacy presentation."),
                Service = Service, View = Draft.Before, Texts = Texts };
            AddChild(_meeting);
        }
        Narrate(SkipIntroduction ? [] : Draft.Introduction, ShowChoices);
    }
    private void Replace(Control? child)
    {
        if (_child is not null) { RemoveChild(_child); _child.QueueFree(); }
        _child = child; if (child is not null) AddChild(child);
    }
    private void Narrate(IReadOnlyList<CommandMessage> messages, Action next, bool retainMeeting = true)
    {
        if (messages.Count == 0) { next(); return; }
        Replace(new CommandNarration
        {
            Messages = messages,
            View = Draft.Before,
            Texts = Texts,
            Service = Service,
            Textures = Textures,
            Sounds = Presentation.Sounds,
            ContentRoot = ContentRoot,
            SoundEnabled = SoundEnabled, ReadMessageSpeed = ReadMessageSpeed,
            MeetingScene = _meeting,
            BeginMusic = BeginMusic,
            RetainMeeting = retainMeeting,
            MeetingClosed = () => { _music?.Dispose(); _music = null; },
            MessagePresented = message =>
            {
                if (message.Diplomacy is not null) StartDiplomacyMusic();
            },
            Closed = () => { Replace(null); next(); }
        });
    }
    private void ShowChoices()
    {
        if (_meeting is { Active: false } && Draft.Meeting is { } meeting)
            _meeting.Present(new(Draft.Prompt, null, null) { Diplomacy = meeting });
        StartDiplomacyMusic();
        Pending = false;
        if (Draft.Diplomacy is { } diplomacy)
        {
            if (diplomacy.Offer is null && Draft.Role is "attack-request" or "prisoner-return")
            {
                if (_counteroffer?.TaskId != diplomacy.TaskId) _counteroffer = new(Draft);
            }
            else _counteroffer = null;
            Replace(new DiplomacyDecisionView { Draft = Draft, Textures = Textures, Service = Service, Texts = Texts,
                Choose = Choose, Counteroffer = _counteroffer,
                ConfirmCounteroffer = () => SubmitAnswer(_counteroffer!.Option, _counteroffer.SelectedId, _counteroffer.Amount) });
            return;
        }
        if (Draft.Role == "tactic-study")
        {
            var dialog = new GameDialog { Textures = Textures }; dialog.SetRichMessage(Draft.Prompt.BbCode);
            dialog.Confirmed += () => Choose(Draft.Options.Single(o => o.Role == "choose"));
            dialog.Dismissed += () => Answer(Draft.Options.Single(o => o.Role == "decline"), null, 0);
            Replace(dialog); dialog.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); return;
        }
        if (Draft.Definition.Window is DecisionWindowKind.CountrySelection or DecisionWindowKind.PersonSelection)
        { Choose(Draft.Options.First(o => o.Input is DecisionInputKind.Person or DecisionInputKind.Country)); return; }
        if (Draft.Definition.Window == DecisionWindowKind.PersonDisposition && Draft.SubjectPerson is { } officer)
        {
            int person = officer.Id;
            var hint = new GameMessageBar { Textures = Textures, Text = Draft.Prompt.BbCode };
            var window = new CommandWindow { Texts = Texts,
                Textures = Textures,
                Title = Draft.Role switch { "office-request" => "仕官待遇", "search-recruitment" => "探索登用", _ => "俘虏处置" },
                Position = new(304, 320),
                Size = new(416, 128),
                Help = () => { },
                Closed = ShowChoices,
                Cancelled = () => Answer(Draft.Options[1], null, 0),
                Accepted = () => Answer(Draft.Options[0], null, 0),
                AcceptTitle = Draft.Options[0].Title,
                CancelTitle = Draft.Options[1].Title,
                ActionRightInset = 26,
                CanClose = false
            };
            var body = new DecisionCommandPage();
            Replace(body); body.AddChild(window); body.AddChild(hint);
            var target = window.ActionButton("对象武将", "edit", new(0, 0, 104, 40), () => { });
            target.MouseFilter = MouseFilterEnum.Ignore;
            window.AddChild(new OfficerCard { Service = Service, Textures = Textures, Position = new(100, 0), Officer = new(Texts.Resolve(officer.NameKey), $"information-person-{person}", officer.Abilities) });
            var properties = new OfficerPropertyList { Textures = Textures, Position = new(312, 0), LabelWidth = 48, LabelSkinPrefix = "deployment-mode" };
            properties.SetProperties([new("统率", officer.Abilities.Leadership.ToString(), 40), new("武力", officer.Abilities.War.ToString(), 40), new("智力", officer.Abilities.Intelligence.ToString(), 40), new("政治", officer.Abilities.Politics.ToString(), 40)]);
            window.AddChild(properties); return;
        }
        if (Draft.Definition.Window == DecisionWindowKind.Confirmation && Draft.Options.Count == 2)
        {
            var dialog = new GameDialog { Textures = Textures };
            dialog.SetRichMessage(Draft.Prompt.BbCode, Draft.Options[0].Title, Draft.Options[1].Title);
            dialog.Confirmed += () => Choose(Draft.Options[0]); dialog.Dismissed += () => Choose(Draft.Options[1]);
            Replace(dialog); dialog.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); return;
        }
        Replace(new VerticalChoiceDialog
        {
            Textures = Textures,
            Message = Draft.Prompt.BbCode,
            Options = Draft.Options.Select(o => (o.Title, (Action)(() => Choose(o)))).ToArray()
        });
    }
    private void Choose(DecisionOption option)
    {
        if (Pending) return;
        switch (option.Input)
        {
            case DecisionInputKind.Immediate: Answer(option, null, 0); return;
            case DecisionInputKind.Investment:
            case DecisionInputKind.Money:
                Replace(new NumberInputDialog
                {
                    Textures = Textures,
                    Subject = "资金",
                    State = new(option.Minimum, option.Maximum, Draft.AmountDefault(option)),
                    Confirmed = amount => Answer(option, null, amount),
                    Closed = option.Input == DecisionInputKind.Investment ? () => Choose(option) : ShowChoices
                }); return;
            default: Select(option); return;
        }
    }
    private void Select(DecisionOption option)
    {
        var selection = new CatalogSelection(option.Candidates,
            _counteroffer?.Option.Role == option.Role && _counteroffer.SelectedId is int selected ? [selected] : [], 1);
        bool person = option.Input is DecisionInputKind.Person or DecisionInputKind.Prisoner;
        bool treasure = option.Input == DecisionInputKind.Treasure;
        CatalogRow[] Read(int page)
        {
            if (person)
            {
                var table = OfficerCatalog.Read(Draft.Before, Texts, page, null, null);
                return table.Rows.Where(r => option.Candidates.Contains(r.Key.Id)).Select(r => new CatalogRow(r.Key.Id, r.Cells)).ToArray();
            }
            if (treasure)
                return TreasureCatalog.Read(Draft.Before, Texts).Rows.Where(r => option.Candidates.Contains(r.Key.Id))
                    .Select(r => new CatalogRow(r.Key.Id, r.Cells)).ToArray();
            return option.Candidates.Select(id => new CatalogRow(id, [new CatalogCell(option.Input==DecisionInputKind.Country ?
                Texts.Resolve(Draft.Before.Information.CountryTitles.Single(t=>t.Id==id).NameKey) : Texts.Resolve(Draft.Before.Information.Treasures.Single(t=>t.Id==id).NameKey))])).ToArray();
        }
        Replace(new CatalogView
        {
            Textures = Textures,
            Service = Service,
            Title = option.Title,
            HintText = option.Input == DecisionInputKind.Country ? "请选择国号。" : Draft.Prompt.BbCode,
            Pages = person ? OfficerCatalog.Pages : treasure ? TreasureCatalog.Pages : [new(option.Title, [new("name", option.Title, 12, CatalogAlignment.Left)])],
            ReadRows = Read,
            Selection = selection,
            Portrait = person ? id => $"information-person-{id}" : null,
            ConfirmSelection = ids =>
            {
                int selected = ids.Single();
                if (Draft.ConfirmSelection(selected) is not { } confirmation) { Answer(option, selected, 0); return; }
                var dialog = new GameDialog { Textures = Textures }; dialog.SetRichMessage(confirmation.BbCode);
                dialog.Confirmed += () => Answer(option, selected, 0); dialog.Dismissed += () => Select(option);
                Replace(dialog); dialog.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            },
            Closed = () =>
            {
                if (Draft.Definition.Window == DecisionWindowKind.CountrySelection) Select(option);
                else if (Draft.Role == "succession") Answer(Draft.Options.Single(o => o.Role == "decline"), null, 0);
                else if (Draft.ConfirmCancel() is { } confirmation)
                {
                    var dialog = new GameDialog { Textures = Textures }; dialog.SetRichMessage(confirmation.BbCode);
                    dialog.Confirmed += () => Answer(Draft.Options.Single(o => o.Role == "decline"), null, 0); dialog.Dismissed += () => Select(option);
                    Replace(dialog); dialog.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                }
                else ShowChoices();
            }
        });
    }
    private void Answer(DecisionOption option, int? selected, uint amount)
    {
        if (Pending) return;
        if (_counteroffer is not null && option.Role is "free" or "prisoner" or "money" or "treasure")
        {
            _counteroffer.Select(option.Role, selected, amount);
            Narrate(Draft.CounterofferAdvice(option, selected, amount), ShowChoices);
            return;
        }
        if (option.Role == "accept" && Draft.AcceptanceConfirmations.Count > 0)
        { ConfirmAcceptance(0, option, selected, amount); return; }
        SubmitAnswer(option, selected, amount);
    }
    private void ConfirmAcceptance(int index, DecisionOption option, int? selected, uint amount)
    {
        if (index == Draft.AcceptanceConfirmations.Count) { SubmitAnswer(option, selected, amount); return; }
        var dialog = new GameDialog { Textures = Textures }; dialog.SetRichMessage(Draft.AcceptanceConfirmations[index].BbCode);
        dialog.Confirmed += () => ConfirmAcceptance(index + 1, option, selected, amount); dialog.Dismissed += ShowChoices;
        Replace(dialog); dialog.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }
    private void SubmitAnswer(DecisionOption option, int? selected, uint amount)
    {
        var command = option.CreateCommand(Guid.NewGuid(), _sequence, selected, amount);
        if (!Submit(command, Draft.ControllerId)) return;
        _answer = option; _selected = selected; _amount = amount; Pending = true; Replace(null);
    }
    public void Complete(SessionResult result, DecisionDraft? continuation = null)
    {
        if (!Pending || _answer is null) return;
        var messages = Draft.Results(_answer.Role, result.Events, _selected, _amount);
        if (Draft.Role == "shanyang-gong" && _answer.Role == "accept")
            Narrate([Draft.MovieIntroduction()], () => PlayRestorationMovie(messages));
        else Narrate(messages, () =>
        {
            if (Draft.Meeting is not null && continuation?.Meeting == Draft.Meeting)
            {
                Draft = continuation; _sequence = Draft.Before.World.Simulation.NextSequence;
                _answer = null; _selected = null; _amount = 0;
                Replace(null); ShowChoices();
            }
            else FinishResult();
        }, retainMeeting: Draft.Meeting is not null && continuation?.Meeting == Draft.Meeting);
    }
    private async void PlayRestorationMovie(IReadOnlyList<CommandMessage> messages)
    {
        try
        {
            await Media.PlayMovieAsync(new("emperor-restoration"), _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }
        if (_lifetime.IsCancellationRequested) return;
        Narrate(messages, FinishResult);
    }
    private void FinishResult()
    {
        Replace(null);
        if (_meeting is { Active: true } meeting) meeting.Finish(Finish);
        else Finish();
        void Finish() { _music?.Dispose(); _music = null; Pending = false; Finished(); }
    }
    private void StartDiplomacyMusic()
    {
        if (Draft.Diplomacy is not null && _music is null)
            _music = BeginMusic?.Invoke("diplomacy-music", true);
    }
    public override void _GuiInput(InputEvent input) => AcceptEvent();
    public override void _ExitTree() { _music?.Dispose(); _music = null; _lifetime.Cancel(); _lifetime.Dispose(); }
}

internal partial class DecisionCommandPage : FixedPage
{
    protected override Color PageBackground => new(0, 0, 0, .35f);
}
