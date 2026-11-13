using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Packages;

namespace San9.Client.Godot;

/// <summary>Checks the real officer formation detail against a prepared local content package.</summary>
public partial class OfficerFormationVerification : Node
{
    public override void _Ready() => Callable.From(Run).CallDeferred();

    private static IEnumerable<T> Descendants<T>(Node node) where T : Node
    {
        foreach (var child in node.GetChildren())
        {
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private async void Run()
    {
        try
        {
            string root = System.Environment.GetEnvironmentVariable("SAN9_OFFICER_FORMATION_CONTENT")
                ?? throw new InvalidOperationException("Set SAN9_OFFICER_FORMATION_CONTENT to a scenario package.");
            var package = await ClientContentFiles.LoadAsync(root);
            var session = GameSession.Start(package.Scenario, 42, settings: new() { MixedTroopsEnabled = true });
            if (!session.ResearchProjects.Any(project => project.Id == "crossbow-3"))
                throw new InvalidOperationException("Legacy troop preset did not upgrade to three levels.");
            var visible = session.View.Officers.Select(o => o.Id).ToHashSet();
            var officer = session.Snapshot.Officers.First(o => visible.Contains(o.Id) && o.FormationProficiencies is { Count: > 0 });
            var expected = OfficerDetails.Read(session.View, package.Text, officer.Id);
            if (expected.Formations.Length == 0 || !expected.Formations.Any(f => f.Proficiency is not null))
                throw new InvalidOperationException("No formation data in the selected officer.");
            var service = new TextureService(); service.Initialize(package, package.TextureRoot); AddChild(service);
            using var textures = service.CreateScope();
            textures.RequestPrefixes("detail-", "message-", "catalog-", "scroll-", "roster-");
            var state = new OfficerDetailState(); state.Open([officer.Id], officer.Id);
            var page = new OfficerDetailView
            {
                State = state, Textures = textures, Texts = package.Text, Service = service,
                Read = id => OfficerDetails.Read(session.View, package.Text, id),
                ReadName = id => package.Text.Resolve(session.View.Officers.Single(o => o.Id == id).NameKey),
                CurrentChanged = _ => { }, Closed = () => { }
            };
            AddChild(page);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            page.GetNode<DetailActionButton>("FormationTab").EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (state.Page != OfficerDetailPage.Formations) throw new InvalidOperationException("Formation tab did not open.");
            var labels = Descendants<Label>(page).Select(l => l.Text).ToArray();
            foreach (var formation in expected.Formations)
            {
                if (!labels.Contains(formation.Name) || !labels.Contains(formation.Proficiency?.ToString() ?? "—"))
                    throw new InvalidOperationException("Missing formation detail: " + formation.Name);
            }
            GD.Print($"OFFICER_FORMATION_OK formations={expected.Formations.Length}");
            page.Free(); service.Free(); GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
