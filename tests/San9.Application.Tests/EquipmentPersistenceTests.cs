using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Immutable;
using San9.Application.Persistence;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Content.Scenarios;

namespace San9.Application.Tests;

public sealed class EquipmentPersistenceTests
{
    [Fact]
    public void EventRewardMigrationPreservesOwnershipAndRejectsIdentityConflicts()
    {
        var basis = G01ScenarioTests.Fixture();
        var definition = new EquipmentDefinition("gift", EquipmentSlot.Treasure, EquipmentRarity.Rare, default, default) { Name = "玉佩" };
        var hidden = new EquipmentInstance(20, definition.Id, null, null, false) { IsLost = true, EventGiftKey = "gift.1" };
        var gift = new EquipmentGiftSearchEventDefinition(basis.Settlements[0].Id, hidden.Id, definition.Id, new("gift.1"))
            { Id = "gift-event", Consumption = EventConsumption.OnSuccess };
        var content = basis with { Information = basis.Information with { Equipment = [definition] }, InitialEquipment = [hidden], SearchEvents = [gift] };
        var original = GameSession.Start(basis, 42).Capture();
        var rules = new EquipmentRulebook([definition]);
        var migrated = EquipmentMigration.Initialize(original, content, rules);
        Assert.Contains(hidden, migrated.World.Equipment);
        Assert.Contains("20:gift", migrated.InitializedSearchEquipment!.Value);
        var claimed = hidden with { OfficerId = 1, IsLost = false, EventGiftKey = null };
        var saved = migrated with { World = migrated.World with { Equipment = [claimed] } };
        Assert.Equal(claimed, Assert.Single(EquipmentMigration.Initialize(saved, content, rules).World.Equipment));
        var previousSearchItem = hidden with { EventGiftKey = null, SearchPlaceId = basis.Settlements[0].Id };
        var priorSave = original with { InitializedSearchEquipment = ["20:gift"], World = original.World with { Equipment = [previousSearchItem] } };
        Assert.Equal(previousSearchItem, Assert.Single(EquipmentMigration.Initialize(priorSave, content, rules).World.Equipment));
        var removed = migrated with { World = migrated.World with { Equipment = [] } };
        Assert.Empty(EquipmentMigration.Initialize(removed, content, rules).World.Equipment);
        var conflict = original with { World = original.World with { Equipment = [new EquipmentInstance(20, "other", null, 1, false)] } };
        Assert.Throws<InvalidDataException>(() => EquipmentMigration.Initialize(conflict, content, rules));
    }

    [Fact]
    public void OldSaveAddsSearchItemsOnceWithoutOverwritingExistingInstanceIds()
    {
        var basis = G01ScenarioTests.Fixture();
        var featured = new EquipmentDefinition("featured", EquipmentSlot.MainHand, EquipmentRarity.Precious,
            new(2, 0, 0, 0), default) { Name = "玄纹长戟" };
        var other = new EquipmentDefinition("other", EquipmentSlot.Helmet, EquipmentRarity.Precious,
            new(0, 0, 0, 1), default) { Name = "旧盔" };
        var hidden = new EquipmentInstance(20, featured.Id, null, null, false)
            { IsLost = true, SearchPlaceId = basis.Settlements[0].Id };
        var scenario = basis with { Information = basis.Information with { Equipment = [featured, other] }, InitialEquipment = [hidden] };
        var original = GameSession.Start(scenario, 42).Capture();
        var held = new EquipmentInstance(20, other.Id, null, 1, false);
        var old = original with { InitializedSearchEquipment = null, World = original.World with { Equipment = [held] } };

        var migrated = GameSession.Restore(scenario, old).Capture();
        Assert.Contains("20:featured", migrated.InitializedSearchEquipment!.Value);
        Assert.Contains(held, migrated.World.Equipment);
        var added = Assert.Single(migrated.World.Equipment, item => item.DefinitionId == featured.Id);
        Assert.Equal(21, added.Id);
        Assert.Equal(hidden.SearchPlaceId, added.SearchPlaceId);
        var savedBeforeContentUpgrade = old with { InitializedSearchEquipment = [] };
        Assert.Contains(GameSession.Restore(scenario, savedBeforeContentUpgrade).Snapshot.Equipment,
            item => item.DefinitionId == featured.Id);

        var removed = migrated with { World = migrated.World with { Equipment = [held] } };
        Assert.DoesNotContain(GameSession.Restore(scenario, removed).Snapshot.Equipment, item => item.DefinitionId == featured.Id);
        var withoutPlacement = scenario with { InitialEquipment = [] };
        var savedWithoutPlacement = GameSession.Restore(withoutPlacement, removed).Capture();
        Assert.Contains("20:featured", savedWithoutPlacement.InitializedSearchEquipment!.Value);
        Assert.DoesNotContain(GameSession.Restore(scenario, savedWithoutPlacement).Snapshot.Equipment, item => item.DefinitionId == featured.Id);
    }

    [Fact]
    public void ExchangeHistoryRoundTripsAndLegacyItemWithoutHistoryLoads()
    {
        var item = new EquipmentInstance(8, "sword", null, 1, false, 2, 33)
        {
            LastExperienceExchange = "42:u1:u3",
            ExperienceExchanges = ImmutableSortedSet.Create(StringComparer.Ordinal, "42:u1:u2", "42:u1:u3")
        };
        var json = JsonNode.Parse(JsonSerializer.Serialize(item, StrictJson.Options))!.AsObject();
        var restored = StrictJson.Deserialize<EquipmentInstance>(Encoding.UTF8.GetBytes(json.ToJsonString()));
        Assert.True(item.ExperienceExchanges.SetEquals(restored.ExperienceExchanges!));
        json.Remove("experienceExchanges");
        var legacy = StrictJson.Deserialize<EquipmentInstance>(Encoding.UTF8.GetBytes(json.ToJsonString()));
        Assert.Null(legacy.ExperienceExchanges);
        Assert.Equal(item.LastExperienceExchange, legacy.LastExperienceExchange);
    }

    [Fact]
    public void MigratedNamedItemIsDeferredForNonServingHolder()
    {
        var person = new OfficerState(2, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Free };
        var named = new EquipmentInstance(5, "legacy-treasure:5", 2, null, true) { LegacyTreasureId = 5 };
        var deferred = Assert.Single(EquipmentMigration.UnequipNonServing([named], [person]));
        Assert.False(deferred.Equipped);
        Assert.True(deferred.EquipOnService);
        Assert.Equal(5, deferred.LegacyTreasureId);
    }
    [Fact]
    public void OrdinaryEquipmentConvertsOnceAndDeferredPossessionConvertsOnService()
    {
        var scenario = G01ScenarioTests.Fixture();
        var named = new EquipmentDefinition("named", EquipmentSlot.MainHand, EquipmentRarity.Rare, new(5, 0, 0, 0), default);
        scenario = scenario with { Information = scenario.Information with { Equipment = EquipmentPreset.Basic().Add(named) } };
        var session = GameSession.Start(scenario, 42);
        var original = session.Capture();
        Assert.DoesNotContain(original.World.Equipment, item => item.DefinitionId.StartsWith("issue-", StringComparison.Ordinal));
        var old = original with { InitialEquipmentIssued = true, ArmamentMigrationInitialized = null, World = original.World with
        {
            Officers = original.World.Officers.Select(p => p.Id == 2 ? p with { OwnerId = null, Status = OfficerStatus.Free } : p).ToArray(),
            Forces = original.World.Forces.Select(f => f.Id == 1 ? f with { EquipmentProduction = new("basic-spear", 1, 15) } : f).ToArray(),
            Equipment = [new EquipmentInstance(10, "issue-cloth-cap", 1, null, true),
                new EquipmentInstance(11, "issue-short-sword", 2, null, false),
                new EquipmentInstance(12, "named", 1, null, true, 2, 50)]
        } };
        var migrated = GameSession.Restore(scenario, old).Capture();
        Assert.True(migrated.ArmamentMigrationInitialized);
        Assert.Equal(360, migrated.World.Forces.Single(f => f.Id == 1).ArmamentMigrationCredit);
        Assert.Null(migrated.World.Forces.Single(f => f.Id == 1).EquipmentProduction);
        Assert.Equal(200, migrated.World.Equipment.Single(e => e.Id == 11).PendingArmamentCredit);
        Assert.Equal(old.World.Equipment.Single(e => e.Id == 12), migrated.World.Equipment.Single(e => e.Id == 12));
        var saved = GameSession.Restore(scenario, migrated);
        var restored = SaveService.Decode(SaveService.Encode(saved), scenario).Capture();
        Assert.Equal(360, restored.World.Forces.Single(f => f.Id == 1).ArmamentMigrationCredit);
        var mutation = WorldState.Restore(restored.World).BeginMutation(equipment: new EquipmentRulebook(EquipmentPreset.Basic().Add(named)));
        mutation.Replace(mutation.Officers[2] with { OwnerId = 1, Status = OfficerStatus.Serving });
        Assert.Equal(560, mutation.Forces[1].ArmamentMigrationCredit);
        Assert.DoesNotContain(mutation.AllEquipment.Values, item => item.Id == 11);
        mutation.Replace(mutation.Officers[2]);
        Assert.Equal(560, mutation.Forces[1].ArmamentMigrationCredit);
    }
    [Fact]
    public void LegacyEquipmentDefinitionsDefaultGradeAndRequirements()
    {
        var definition = new EquipmentDefinition("spear", EquipmentSlot.MainHand, EquipmentRarity.Ordinary, default, default);
        var json = JsonNode.Parse(JsonSerializer.Serialize(definition, StrictJson.Options))!.AsObject();
        json.Remove("grade");
        json.Remove("minimumAbilities");
        var restored = StrictJson.Deserialize<EquipmentDefinition>(Encoding.UTF8.GetBytes(json.ToJsonString()));
        Assert.Equal(1, restored.Grade);
        Assert.Equal(default, restored.MinimumAbilities);
    }

    [Fact]
    public void WorldSnapshotAcceptsLegacyMissingEquipmentAndRoundTripsInstances()
    {
        var world = WorldState.Create(new(190, 1, 1), 1, [new ForceState(1, 0)], [], [],
            corps: [new CorpsState(1, 1, null, new(0, 0)) { IsRoot = true }]);
        var source = new SimulationEngine(world).Snapshot;
        var json = JsonNode.Parse(JsonSerializer.Serialize(source, StrictJson.Options))!.AsObject();
        json.Remove("equipment");
        json.Remove("equipmentInitialized");
        var legacy = StrictJson.Deserialize<WorldSnapshot>(Encoding.UTF8.GetBytes(json.ToJsonString()));
        Assert.Empty(legacy.Equipment);
        Assert.False(legacy.EquipmentInitialized);
        var current = source with { Equipment = [new EquipmentInstance(8, "sword", null, 1, false, 2, 33)] };
        var restored = StrictJson.Deserialize<WorldSnapshot>(JsonSerializer.SerializeToUtf8Bytes(current, StrictJson.Options));
        Assert.Equal(current.Equipment, restored.Equipment);
        Assert.Single(WorldState.Restore(restored).BeginMutation().CapturePresentation().Equipment);
    }

    [Fact]
    public void LegacyInformationWithoutEquipmentRetainsAnEmptyCatalog()
    {
        var json = JsonNode.Parse(JsonSerializer.Serialize(InformationDefinitions.Empty, StrictJson.Options))!.AsObject();
        json.Remove("equipment");
        var restored = StrictJson.Deserialize<InformationDefinitions>(Encoding.UTF8.GetBytes(json.ToJsonString()));
        Assert.Empty(restored.Equipment);
        Assert.False(restored.Equipment.IsDefault);
    }

    [Fact]
    public void EquipmentCommandAndEventRoundTripPolymorphically()
    {
        GameCommand command = new ConfigureEquipmentCommand(Guid.NewGuid(), 1, 3, 2, EquipmentSlot.MainHand, 5);
        DomainEvent changed = new EquipmentConfiguredEvent(new(190, 1, 1), 1, 2, EquipmentSlot.MainHand, 5);
        Assert.Equal(command, StrictJson.Deserialize<GameCommand>(JsonSerializer.SerializeToUtf8Bytes(command, StrictJson.Options)));
        Assert.Equal(changed, StrictJson.Deserialize<DomainEvent>(JsonSerializer.SerializeToUtf8Bytes(changed, StrictJson.Options)));
        GameCommand production = new StartEquipmentProductionCommand(Guid.NewGuid(), 1, 4, 2, "spear");
        GameCommand transfer = new TransferEquipmentCommand(Guid.NewGuid(), 1, 5, 7, 2);
        DomainEvent completed = new EquipmentProductionCompletedEvent(new(190, 1, 1), 1, 2, "spear", 7);
        GameCommand purchase = new PurchaseEquipmentCommand(Guid.NewGuid(), 1, 6, 2, "spear");
        DomainEvent purchased = new EquipmentPurchasedEvent(new(190, 1, 1), 1, 2, "spear", 8, 400);
        Assert.Equal(production, StrictJson.Deserialize<GameCommand>(JsonSerializer.SerializeToUtf8Bytes(production, StrictJson.Options)));
        Assert.Equal(transfer, StrictJson.Deserialize<GameCommand>(JsonSerializer.SerializeToUtf8Bytes(transfer, StrictJson.Options)));
        Assert.Equal(completed, StrictJson.Deserialize<DomainEvent>(JsonSerializer.SerializeToUtf8Bytes(completed, StrictJson.Options)));
        Assert.Equal(purchase, StrictJson.Deserialize<GameCommand>(JsonSerializer.SerializeToUtf8Bytes(purchase, StrictJson.Options)));
        Assert.Equal(purchased, StrictJson.Deserialize<DomainEvent>(JsonSerializer.SerializeToUtf8Bytes(purchased, StrictJson.Options)));
    }
}
