using Godot;
using San9.Application;
using San9.Client.Godot.Persistence;
using San9.Client.Godot.Settings;
using San9.Content.Packages;

namespace San9.Client.Godot;

public partial class WorkflowLifecycleVerification
{
    private static IEnumerable<T> Descendants<T>(Node parent) where T : Node
    {
        foreach (var child in parent.GetChildren())
        {
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private async Task WaitForModal(Func<bool> ready, TextureService service)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!ready())
        {
            if (service.ActiveError is { } error) throw error;
            if (watch.Elapsed.TotalSeconds > 30) throw new TimeoutException("Modal content verification timed out.");
            await Frame();
        }
    }

    private async Task CheckContentModals(string packageRoot)
    {
        var package = await ClientContentFiles.LoadAsync(Path.GetFullPath(packageRoot));
        var scene = new Node(); AddChild(scene);
        var service = new TextureService(); service.Initialize(package, package.TextureRoot); scene.AddChild(service);
        using var textures = service.CreateScope();
        textures.RequestPrefixes("environment-", "save-load-", "dialog-", "message-", "decision-choice-");
        var host = StrategyOverlayHost.Create(); scene.AddChild(host);
        try
        {
            var settings = new EnvironmentSettingsView
            {
                Textures = textures, Initial = UserSettings.Default, Closed = () => { },
                Apply = _ => throw new IOException("injected settings write failure")
            };
            var settingsPage = OverlayPage.Create(settings, Colors.Transparent); host.AddChild(settingsPage);
            await WaitForModal(() => service.ActiveReady, service);
            await Frame();
            var confirm = settings.GetChildren().OfType<SaveLoadButton>().Single(button => button.Title == "执行");
            await Click(confirm.GetGlobalRect().GetCenter());
            Check(settings.HasNotice, "Actual settings page did not show the injected save failure.");
            var notice = settings.GetChildren().OfType<GameDialog>().Single();
            var options = settings.GetChildren().OfType<EnvironmentOptionButton>().ToArray();
            var selected = options.Select(option => option.Selected).ToArray();
            for (int i = 0; i < options.Length + 3; i++)
            {
                await KeyPress(Key.Tab);
                Check(GetViewport().GuiGetFocusOwner() is { } focus && notice.IsAncestorOf(focus),
                    "Actual settings failure notice leaked keyboard focus.");
            }
            await KeyPress(Key.Escape);
            Check(!settings.HasNotice && options.Select(option => option.Selected).SequenceEqual(selected),
                "Dismissing settings failure changed the underlying draft.");
            settingsPage.Free();

            var session = GameSession.Start(package.Scenario, 1);
            session.MarkSaved(session.NextOperationSequence);
            string saves = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"),
                "../../local/generated/verification/modal-content", Guid.NewGuid().ToString("N")));
            var store = new SlotSaveStore(saves);
            var summary = SaveSlotSummaryFactory.Create(session, package.Scenario, package.Text, package.Skin.Roles, "modal verification");
            await store.WriteAsync(1, SlotSaveStore.Capture(session, summary));
            int loaded = 0;
            var system = new SystemMenuPanel
            {
                Service = service, Session = session, Store = store, Texts = package.Text,
                LoadGame = document => { Check(document.Header.Summary.Note == summary.Note, "Load selected a different save."); loaded++; return Task.CompletedTask; }
            };
            host.AddChild(system);
            var title = new ReturnToTitleView
            {
                Definition = package.Presentation.Ending!, Textures = textures, Service = service,
                LoadGame = () => system.Open(SaveLoadMode.Load)
            };
            host.AddChild(title);
            await WaitForModal(() => service.ActiveReady, service);
            title._Process(title.Definition.LogoSeconds + 1);
            await Click(title.GlobalPosition + new Vector2(50, 50));
            var loadOption = Descendants<InformationMenuButton>(title).Single(button => button.Title == "载入游戏");
            await Click(loadOption.GetGlobalRect().GetCenter());
            await WaitForModal(() => system.Browser.IsOpen && service.ActiveReady, service);
            await Frame();
            var row = Descendants<SaveLoadButton>(system).First(button => button.Skin == "load-row");
            Check(!row.Disabled, "Actual save row was not available for loading.");
            await Click(row.GetGlobalRect().GetCenter());
            await WaitForModal(() => loaded == 1, service);
            GD.Print("MODAL_CONTENT_OK settings=failure,tab,escape terminal=menu,load-list,mouse-row,save-document");
        }
        finally { scene.Free(); }
    }
}
