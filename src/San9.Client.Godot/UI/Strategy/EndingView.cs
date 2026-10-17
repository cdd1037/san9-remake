using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Client.Godot;

public partial class EndingView : FixedPage
{
    protected override Color PageBackground => Colors.Black;
    public EndingDraft Draft { get; init; } = null!;
    public TextureScope Textures { get; init; } = null!;
    public TextureService Service { get; init; } = null!;
    public TextCatalog Texts { get; init; } = null!;
    public CommandPresentation Presentation { get; init; } = null!;
    public string ContentRoot { get; init; } = "";
    public Func<Settings.MessageSpeed> ReadMessageSpeed { get; init; } = () => Settings.MessageSpeed.Normal;
    public bool SoundEnabled { get; init; }
    public ISceneMedia Media { get; init; } = null!;
    public Func<EndingOperation, bool> Submit { get; init; } = null!;
    public Action Finished { get; init; } = null!;
    public bool Pending { get; private set; }
    private Control? _child;
    private readonly CancellationTokenSource _lifetime = new();
    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        Media.RequestBackgroundMusic(new(BackgroundMusicAction.Stop));
        Media.RequestBackgroundMusic(new(BackgroundMusicAction.Play, "unification"));
        Narrate([Draft.Message("unification")], () =>
        {
            Media.RequestBackgroundMusic(new(BackgroundMusicAction.Play, "ending"));
            if (Draft.NeedsOfficeholders) SelectOffice(); else Publish();
        });
    }
    private void Replace(Control? child)
    {
        if (_child is not null) { RemoveChild(_child); _child.QueueFree(); }
        _child = child; if (child is not null) AddChild(child);
    }
    private void Narrate(IReadOnlyList<CommandMessage> messages, Action next) => Replace(new CommandNarration
    {
        Messages = messages,
        View = Draft.View,
        Texts = Texts,
        Service = Service,
        Textures = Textures,
        Sounds = Presentation.Sounds,
        ContentRoot = ContentRoot,
        SoundEnabled = SoundEnabled, ReadMessageSpeed = ReadMessageSpeed,
        IllustrationFadeSeconds = 1,
        Closed = next
    });
    private void SelectOffice()
    {
        if (Draft.SelectionStep == 4)
        {
            Confirm(Draft.Message("confirm-offices").Message.BbCode,
                () => Narrate(Draft.AppointmentMessages(), SelectPolicy), () => { Draft.Back(); SelectOffice(); }); return;
        }
        string title = Draft.CurrentOffice switch { "chancellor" => "相国", "marshal" => "大元帅", "protector" => "西域都护", _ => "后继者" };
        var candidates = Draft.Candidates.ToArray();
        Narrate([Draft.Message("select-" + Draft.CurrentOffice)], () => Replace(new CatalogView
        {
            Textures = Textures,
            Service = Service,
            Title = title,
            Pages = OfficerCatalog.Pages,
            Selection = new(candidates, [], 1),
            ReadRows = page => OfficerCatalog.Read(Draft.View, Texts, page, null, null).Rows.Where(r => candidates.Contains(r.Key.Id)).Select(r => new CatalogRow(r.Key.Id, r.Cells)).ToArray(),
            Portrait = id => $"information-person-{id}",
            ConfirmSelection = ids => { Draft.Select(ids.Single()); SelectOffice(); },
            Closed = () => { Draft.Back(); SelectOffice(); }
        }));
    }
    private void SelectPolicy() => Replace(new VerticalChoiceDialog
    {
        Textures = Textures,
        Message = Draft.Message("policy").Message.BbCode,
        Options = [("大陆制霸", () => ChoosePolicy(EndingPolicy.ContinentalExpansion)), ("国内繁荣", () => ChoosePolicy(EndingPolicy.DomesticProsperity))]
    });
    private void ChoosePolicy(EndingPolicy policy)
    {
        Draft.Policy = policy;
        Confirm(Draft.Message(policy == EndingPolicy.ContinentalExpansion ? "confirm-expansion" : "confirm-domestic").Message.BbCode, Publish, SelectPolicy);
    }
    private void Confirm(string message, Action yes, Action no)
    {
        var dialog = new GameDialog { Textures = Textures }; dialog.SetRichMessage(message); dialog.Confirmed += yes; dialog.Dismissed += no;
        Replace(dialog); dialog.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }
    private void Publish()
    {
        if (Pending || !Submit(Draft.CreateOperation())) return;
        Pending = true; Replace(null);
    }
    public void Complete() { if (Pending) { Pending = false; Narrate(Draft.Results(), PlayMovie); } }
    private async void PlayMovie()
    {
        try { await Media.PlayMovieAsync(new("ending"), _lifetime.Token); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }
        if (!_lifetime.IsCancellationRequested) Finished();
    }
    public override void _GuiInput(InputEvent input) => AcceptEvent();
    public override void _ExitTree() { _lifetime.Cancel(); _lifetime.Dispose(); }
}
