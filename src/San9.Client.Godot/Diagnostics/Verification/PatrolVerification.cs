using Godot;
using San9.Application.Presentation;
using static San9.Client.Godot.Diagnostics.VerificationSupport;

namespace San9.Client.Godot;

public partial class StrategyHud
{
    public async Task RunPatrolVerificationAsync()
    {
        string output = Path.GetFullPath(Path.Combine(ContentRoot,"../../verification/patrol"));
        async Task CaptureStage(string name) => await Capture(this,Path.Combine(output,name+".png"),Textures,World);
        async Task Click(Vector2 point)
        {
            GetViewport().PushInput(new InputEventMouseMotion { Position=point, GlobalPosition=point },true);
            GetViewport().PushInput(new InputEventMouseButton { Position=point, GlobalPosition=point, ButtonIndex=MouseButton.Left, Pressed=true },true);
            GetViewport().PushInput(new InputEventMouseButton { Position=point, GlobalPosition=point, ButtonIndex=MouseButton.Left, Pressed=false },true);
            await Frames(this,2);
        }
        await Frames(this,2);
        int city = Session.View.Settlements.First(s => PatrolDraft.UnavailableReasonKey(Session.View,s.Id) is null).Id;
        var before = Session.Snapshot;
        ShowCommandMenu(new(city,false),new(400,200));
        ActivateCommand("patrol");
        Check(_patrol is not null,"Patrol menu did not open preparation.");
        Check(!CanIssueOrders && !CanNavigateMap,"Patrol modal did not block competing map commands.");
        await CaptureStage("preparation");
        await Click(new(328,292));
        Check(_patrol!.GetChildren().OfType<CatalogView>().Any(),"Officer selector did not open.");
        await CaptureStage("officers");
        // Shared list bulk action and confirmation, through engine input.
        var selector = _patrol.GetChildren().OfType<CatalogView>().Single();
        var bulk = selector.GetChildren().OfType<CatalogBulkButton>().First();
        await Click(bulk.GlobalPosition+bulk.Size/2);
        await CaptureStage("selected-officers");
        var confirm = selector.GetChildren().OfType<InformationActionButton>().Single(b=>b.Title=="决定");
        await Click(confirm.GlobalPosition+confirm.Size/2);
        Check(_patrol.Draft.CanExecute,"Officer confirmation did not update preparation.");
        await CaptureStage("ready");
        int count = _patrol.Draft.Members.Length;
        uint money = _patrol.Draft.Money;
        await Click(new(574,584));
        Check(_patrol is null && _narration is not null,"Execute did not transition to narration.");
        Check(Session.Snapshot.Clock==before.Clock && Session.View.PlayerCorps!.Treasury.Money==money-count*50,"Patrol transaction mismatch.");
        long committed = Session.Snapshot.Revision;
        int index=0;
        while (_narration is not null)
        {
            Check(index<8,"Narration failed to terminate.");
            await CaptureStage($"message-{index++}");
            await Click(new(500,420));
            Check(Session.Snapshot.Revision==committed,"Advancing dialogue mutated the world.");
        }
        Check(PatrolDraft.UnavailableReasonKey(Session.View,city) is not null,"Repeated patrol remained available.");
        await Frames(this,2);
        Check(DisplayedMoney==Session.View.PlayerCorps!.Treasury.Money.ToString(),"HUD did not refresh patrol cost.");
        // A separate content probe covers a hidden NPC, its portrait and the citizen SE without rerolling gameplay.
        var presentation = CommandPresentation.Patrol!;
        var npc = Session.Messages(Texts).Produce(presentation.Messages["citizen"],new Dictionary<string,MessageBinding>
        {
            ["person.0"] = MessageBinding.Reference(San9.Content.Text.MessageEntityKind.Person,presentation.CitizenId),
            ["person.1"] = MessageBinding.Reference(San9.Content.Text.MessageEntityKind.Person,Session.View.Forces.Single(f=>f.Id==Session.View.PlayerForceId).LeaderId!.Value)
        },_=>0);
        _narration = new() { Messages = [new(npc,presentation.CitizenId,"patrol-citizen")], View = Session.View,
            Texts = Texts, Service = Textures, Textures = _textures, ContentRoot = ContentRoot,
            Sounds = CommandPresentation.Sounds, SoundEnabled = SoundEnabled, Closed = () => CloseNarration() };
        OverlayHost.AddChild(_narration);
        await CaptureStage("citizen-content-probe");
        await Click(new(500,420));
        Check(_narration is null && Session.Snapshot.Revision==committed,"NPC presentation altered the command result.");
        ShowCommandMenu(new(city,false),new(400,200));
        var facilities = _commandMenu.GetChildren().OfType<InformationMenuButton>().Single(b => b.Title == "设施");
        await Click(facilities.GlobalPosition+facilities.Size/2);
        var patrol = _commandMenu.GetChildren().OfType<InformationMenuButton>().Single(b => b.Title == "巡察");
        Check(patrol.Disabled,"Used patrol button remained enabled.");
        GetViewport().WarpMouse(patrol.GlobalPosition+patrol.Size/2);
        await Frames(this,4);
        Check(!_commandMenu.HintEnabled && _commandMenu.Hint==Texts.Resolve("ui.command-reason.already-used"),"Disabled patrol did not show its reason.");
        await CaptureStage("disabled-patrol");
        await Click(patrol.GlobalPosition+patrol.Size/2);
        Check(_patrol is null && Session.Snapshot.Revision==committed,"Disabled click executed a command.");
        _commandMenu.Close();
        GD.Print($"PATROL_SMOKE_OK city={city} people={count} messages={index} revision={Session.Snapshot.Revision}");
    }
}
