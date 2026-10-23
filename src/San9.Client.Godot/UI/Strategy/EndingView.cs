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
    public Func<string, bool, MusicPlaybackState.Lease> BeginMusic { get; init; } = null!;
    public Func<EndingOperation, bool> Submit { get; init; } = null!;
    public Action Finished { get; init; } = null!;
    public Func<Action, Control> Chronicle { get; init; } = null!;
    public bool Pending { get; private set; }
    private Control? _child;
    private MusicPlaybackState.Lease? _music;
    private bool _opening;
    private TextureScope _sceneTextures = null!;
    private EndingPresentationDefinition Definition => Presentation.Ending!;
    private readonly CancellationTokenSource _lifetime = new();
    public override void _Ready()
    {
        Size = new(1024, 768); MouseFilter = MouseFilterEnum.Stop;
        _sceneTextures = Service.CreateScope(Service.LibraryAssets);
        _sceneTextures.Request(Definition.SceneAssets);
        _music = BeginMusic("unification-music", false);
        Narrate([Draft.Message("unification")], () =>
        {
            _music?.Dispose(); _music = null;
            Replace(Chronicle(BeginResolution));
        });
    }
    private void BeginResolution()
    {
        _opening = true; QueueRedraw();
        _music = BeginMusic("ending-music", true);
        SelectCountryTitle();
    }
    private void Prepare() => Narrate([Draft.PreparationMessage()], () =>
    { if (Draft.NeedsOfficeholders) SelectOffice(); else SelectPolicy(); });
    private void SelectCountryTitle()
    {
        var candidates = Draft.CountryTitleCandidates(Definition.ReplacedCountryTitleIds);
        if (candidates.Count == 0) { Prepare(); return; }
        Narrate([Draft.Message("country-title")], () => ShowCountryCatalog(candidates));
    }
    private void ShowCountryCatalog(IReadOnlyList<int> candidates) => Replace(new CatalogView
    {
        Textures = Textures, Service = Service, Title = "国号", HintText = "请选择国号。",
        Pages = [new("国号", [new("name", "国号", 12, CatalogAlignment.Left)])],
        Selection = new(candidates, [], 1),
        ReadRows = _ => candidates.Select(id => new CatalogRow(id, [new(Texts.Resolve(Draft.View.Information.CountryTitles.Single(t => t.Id == id).NameKey))])).ToArray(),
        ConfirmSelection = ids =>
        {
            int title = ids.Single();
            Confirm(Draft.PreviewCountryTitle(title, Definition.ReplacedCountryTitleIds).Message.BbCode, () =>
            {
                Draft.SelectCountryTitle(title, Definition.ReplacedCountryTitleIds);
                Narrate([Draft.Message("country-founded")], Prepare);
            }, () => ShowCountryCatalog(candidates));
        },
        Closed = () => ShowCountryCatalog(candidates)
    });
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
            Pages = EndingOfficeCatalog.Pages(Draft.CurrentOffice),
            Selection = new(candidates, [], 1),
            ReadRows = _ => EndingOfficeCatalog.Read(Draft, Texts),
            Portrait = id => $"information-person-{id}",
            ConfirmSelection = ids => { Draft.Select(ids.Single()); SelectOffice(); },
            Closed = () => { Draft.Back(); SelectOffice(); }
        }));
    }
    private void SelectPolicy() => Replace(new VerticalChoiceDialog
    {
        Textures = Textures,
        Message = Draft.Message("policy").Message.BbCode,
        Options = [("称霸大陆型", () => ChoosePolicy(EndingPolicy.ContinentalExpansion)), ("繁荣国内型", () => ChoosePolicy(EndingPolicy.DomesticProsperity))]
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
    public void Complete()
    {
        if (!Pending) return;
        Pending = false;
        var messages = Draft.Sequence();
        Narrate([messages[0].Message], () => Replace(new EndingSequenceView
        {
            Definition = Definition, Messages = messages.Skip(1).SkipLast(1).ToArray(), Textures = _sceneTextures, UiTextures = Textures,
            Expansion = Draft.Policy == EndingPolicy.ContinentalExpansion, ReadMessageSpeed = ReadMessageSpeed,
            Closed = () =>
            {
                void FinalResult()
                {
                    _opening = false; QueueRedraw();
                    Narrate([messages[^1].Message], PlayMovie);
                }
                if (Draft.NeedsOfficeholders && _music is { } music)
                    music.FadeOut(Definition.MusicFadeSeconds, () => { _music = null; FinalResult(); });
                else FinalResult();
            }
        }));
    }
    private void StopMusic() { _music?.Dispose(); _music = null; }
    public override void _Draw()
    {
        if (_opening && _sceneTextures.TryGetReady(Definition.Opening, this, out var texture)) DrawTexture(texture, Vector2.Zero);
    }
    private async void PlayMovie()
    {
        StopMusic();
        try { await Media.PlayMovieAsync(new("ending"), _lifetime.Token); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }
        if (!_lifetime.IsCancellationRequested) Finished();
    }
    public override void _GuiInput(InputEvent input) => AcceptEvent();
    public override void _ExitTree() { _lifetime.Cancel(); _lifetime.Dispose(); _music?.Dispose(); _music = null; _sceneTextures.Dispose(); }
}
