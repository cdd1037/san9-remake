using Godot;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Packages;
using San9.Core.Domain;

namespace San9.Client.Godot;

/// <summary>Exercises the real officer equipment controls using a prepared content package.</summary>
public partial class OfficerEquipmentVerification : Node
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
            string root = System.Environment.GetEnvironmentVariable("SAN9_EQUIPMENT_CONTENT")
                ?? throw new InvalidOperationException("Set SAN9_EQUIPMENT_CONTENT to a scenario package.");
            var package = await ClientContentFiles.LoadAsync(root);
            int selectedForce = package.Scenario.PlayerForceId ?? package.Scenario.Forces.First(f => !f.IsDormant && !f.IsBarbarian && f.LeaderId is not null).Id;
            var initial = GameSession.Start(package.Scenario, 42, playerForceIds: [selectedForce]);
            int player = initial.View.PlayerForceId ?? throw new InvalidOperationException("No player force.");
            var officer = initial.View.Officers.First(o => o.OwnerId == player && o.SettlementId is not null &&
                initial.Snapshot.Officers.Single(p => p.Id == o.Id).IsServing);
            var saved = initial.Capture();
            long itemId = saved.World.Equipment.Select(e => e.Id).DefaultIfEmpty(-1).Max() + 1;
            var session = GameSession.Restore(package.Scenario, saved with { World = saved.World with
            { Equipment = [.. saved.World.Equipment, new EquipmentInstance(itemId, "basic-spear", null, player, false)] } });
            var service = new TextureService(); service.Initialize(package, package.TextureRoot); AddChild(service);
            using var textures = service.CreateScope();
            textures.RequestPrefixes("detail-", "message-", "catalog-", "information-", "scroll-", "roster-", "dialog-");
            var state = new OfficerDetailState(); state.Open([officer.Id], officer.Id);
            var page = new OfficerDetailView { State = state, Session = session, Textures = textures, Texts = package.Text, Service = service,
                Read = id => OfficerDetails.Read(session.View, package.Text, id),
                ReadName = id => package.Text.Resolve(session.View.Officers.Single(o => o.Id == id).NameKey),
                CurrentChanged = _ => { }, Closed = () => { } };
            AddChild(page);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            page.GetNode<DetailActionButton>("EquipmentOpen").EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var equipment = Descendants<OfficerEquipmentView>(page).Single();
            var labels = Descendants<Label>(equipment).Select(label => label.Text).ToHashSet();
            foreach (string slot in new[] { "主武器", "副武器", "头盔", "铠甲", "坐骑", "宝物", "兵符", "暗器", "书籍" })
                if (!labels.Contains(slot)) throw new InvalidOperationException("Missing slot: " + slot);
            var slots = OfficerEquipment.Read(session, package.Text, officer.Id).Slots;
            int slotIndex = Array.FindIndex(slots, s => s.Slot == EquipmentSlot.MainHand);
            Descendants<CatalogView>(equipment).Single().Activated!(slotIndex);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var items = OfficerEquipment.Read(session, package.Text, officer.Id).Slots[slotIndex].Items;
            int itemIndex = Array.FindIndex(items, item => item.Id == itemId);
            if (itemIndex < 0 || !items[itemIndex].CanEquip) throw new InvalidOperationException("Inventory equipment cannot be selected.");
            Descendants<CatalogView>(equipment).Single().Activated!(itemIndex);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Descendants<CatalogView>(equipment).Single().Activated!(0);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Descendants<StrategyButton>(equipment).Single(button => button.Title == "是").EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var equipped = session.Snapshot.Equipment.Single(e => e.Id == itemId);
            if (equipped.OfficerId != officer.Id || !equipped.Equipped) throw new InvalidOperationException("Equipment command did not apply.");
            GD.Print($"OFFICER_EQUIPMENT_OK officer={officer.Id} slots=9");
            page.Free(); service.Free(); GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
