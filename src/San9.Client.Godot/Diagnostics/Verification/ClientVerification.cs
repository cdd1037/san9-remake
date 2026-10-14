using Godot;
using San9.Application.Presentation;

namespace San9.Client.Godot.Diagnostics;

public partial class ClientVerification : Node
{
    public MapWorld World { get; init; } = null!;
    public StrategySelection Selection { get; init; } = null!;
    public StrategyHud Hud { get; init; } = null!;
    public ClientOptions Options { get; init; } = null!;
    public override async void _Ready()
    {
        try
        {
            await VerificationSupport.WaitForView(this, World, requirePresentedView: false);
            if (Options.PatrolSmoke) await Hud.RunPatrolVerificationAsync();
            if (Options.InformationSmoke) await RunInformationChecks();
            if (Options.MapSmoke) await RunMapChecks();
            if (Options.UiSmoke) await Hud.RunSmokeAsync();
            if (Options.RuntimeSmoke) await RunRuntimeChecks();
            await WaitForView();
            if (Options.Screenshot is {} path)
            {
                if (Options.UiSmoke) await CaptureUiVariants(path);
                await Capture(path);
            }
            if (Options.Quit) GetTree().Quit();
        }
        catch (Exception e) { GD.PushError($"CLIENT_VERIFICATION_FAILED {e}"); GetTree().Quit(1); }
    }
    private async Task RunRuntimeChecks()
    {
        var session = World.Session;
        var before = session.Snapshot;
        var officer = session.View.Officers.Where(o => o.SettlementId == 15 && o.OwnerId == 5 && o.IsAvailable)
            .OrderByDescending(o => o.Abilities.Politics).First();
        if (Options.Screenshot is { } path) await Capture(Path.Combine(Path.GetDirectoryName(path)!, "runtime-before.png"));
        var result = session.Develop(new(5, 15, [officer.Id], San9.Core.Domain.DomesticActivity.Commerce));
        if (!result.Accepted || session.Snapshot.Corps.Single(f => f.IsRoot && f.OwnerId == 5).Treasury.Money != before.Corps.Single(f => f.IsRoot && f.OwnerId == 5).Treasury.Money-50 ||
            session.Snapshot.Settlements.Single(s => s.Id == 15).Commerce <= 300 || session.Snapshot.Clock != before.Clock)
            throw new InvalidOperationException("Runtime development transaction failed.");
        await VerificationSupport.Frames(this, 2);
        if (World.StateRevision != session.Snapshot.Revision || Hud.DisplayedMoney != session.Snapshot.Corps.Single(f => f.IsRoot && f.OwnerId == 5).Treasury.Money.ToString())
            throw new InvalidOperationException("Runtime map or HUD did not consume the committed snapshot.");
        var after = session.Snapshot;
        if (session.Develop(new(5, 15, [officer.Id], San9.Core.Domain.DomesticActivity.Commerce)).Accepted || session.Snapshot.Revision != after.Revision)
            throw new InvalidOperationException("Rejected runtime command changed the world.");
        GD.Print($"RUNTIME_SMOKE_OK development={result.Increment} cost={result.Cost} revision={World.StateRevision} old_snapshot_money={before.Corps.Single(f => f.IsRoot && f.OwnerId == 5).Treasury.Money}");
    }
    private Task WaitForView() => VerificationSupport.WaitForView(this, World);

    private Task Capture(string path) => VerificationSupport.Capture(this, path, World.Textures, World);
}
