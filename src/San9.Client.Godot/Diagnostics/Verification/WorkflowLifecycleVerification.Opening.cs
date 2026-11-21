using Godot;
using San9.Application.Authoring;
using San9.Content.Authoring;
using San9.Content.Packages;

namespace San9.Client.Godot;

public partial class WorkflowLifecycleVerification
{
    private async Task CheckOpeningControls(string contentRoot)
    {
        var catalog = await ContentCatalogFiles.LoadAsync(contentRoot);
        var opening = await ContentCatalogFiles.LoadOpeningAsync(contentRoot, catalog);
        string textureRoot = PackageFiles.Resolve(contentRoot, catalog.Textures);
        var textures = await TexturePackageFiles.LoadAsync(textureRoot);
        var scene = new Control { Size = new(1024, 768), MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(scene); ModalStack.Attach(scene);
        var service = new TextureService(); service.Initialize(textures, textureRoot); scene.AddChild(service);
        var pending = new TaskCompletionSource<NewGameDraft>(TaskCreationOptions.RunContinuationsAsynchronously);
        int loads = 0;
        var page = new ForceSelectionView
        {
            Definition = opening.Selection, Entry = opening.Selection.Entries.Single(e => e.Id == "19"), Service = service,
            Help = opening.Menu.Help, Close = opening.Menu.Close, Closed = () => { }, QuitRequested = () => { }, Confirmed = () => { },
            LoadScenario = _ => { loads++; return pending.Task; }
        };
        try
        {
            scene.AddChild(page);
            var choose = page.GetNode<StartMenuButton>("ChooseForces");
            var create = page.GetNode<StartMenuButton>("CreateForces");
            var deploy = page.GetNode<StartMenuButton>("DeployOfficers");
            var cancel = page.GetNode<StartMenuButton>("CancelDeployment");
            var back = page.GetNode<StartMenuButton>("Back");
            Check(cancel.Disabled && create.Disabled && deploy.Disabled, "Unprepared opening actions flashed enabled on the first frame.");
            await WaitForModal(() => loads == 1 && service.ActiveReady, service);
            Check(cancel.Disabled && !pending.Task.IsCompleted, "Cancel deployment waited for the scenario reader to become disabled.");
            choose.GrabFocus();
            var point = back.GetGlobalRect().GetCenter();
            GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
            await Frame();
            Check(back.HasFocus() && !choose.HasFocus(), "Hover retained the old force-choice focus.");
            point = cancel.GetGlobalRect().GetCenter();
            GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
            await Frame();
            Check(back.HasFocus() && cancel.Disabled && !cancel.HasFocus(), "Disabled opening action stole keyboard focus.");

            string packageRoot = PackageFiles.Resolve(contentRoot, catalog.RequireScenario("19").Package);
            var source = await ScenarioContentFiles.LoadScenarioAsync(packageRoot);
            var display = await ScenarioContentFiles.LoadSelectionAsync(packageRoot);
            int id = source.OfficerCatalog.Keys.Max() + 1;
            var person = San9.Content.Scenarios.OfficerDefaults.Create(id) with { BirthYear = source.Year - 20 };
            source = source with { OfficerCatalog = source.OfficerCatalog.Values.Append(person).ToDictionary(p => p.Id) };
            pending.SetResult(new(source, display.Text, display.IllustrationRoles));
            await WaitForModal(() => !create.Disabled && !deploy.Disabled, service);
            Check(cancel.Disabled && page.Setup.Placements.IsEmpty, "Readiness enabled cancel deployment without any assignments.");
            Check(loads == 1, "Opening controls duplicated the scenario read.");
            GD.Print("OPENING_CONTROLS_OK availability=first-frame,pending,ready focus=hover-transfer,disabled-guard");
        }
        finally { scene.Free(); pending.TrySetCanceled(); }
    }
}
