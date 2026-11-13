using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Nodes;
using San9.Application.AI;
using San9.Application.Persistence;
using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Application.Tests;

public sealed partial class OriginalAiActionsTests
{
    [Fact]
    public void AiCanEquipFromArmamentReserveWhenInventoryIsEmpty()
    {
        var basis = Fixture();
        var content = basis with { Military = basis.Military! with
        {
            TroopTypes = [new(100, "枪兵", 1, ImmutableDictionary<int, int>.Empty)],
            ResearchProjects = [new("basic", 100, 30, [])],
            Armaments = [new("spear", "铁矛", ArmamentSlot.Weapon, [100], 8, 0, 100, null)]
        } };
        var definition = new ScenarioSessionDefinition(content);
        var seat = new ControlSeat("ai:2", 2, ControllerKind.Automatic);
        var initial = definition.CreateWorld(42, settings: new NewGameSettings { MixedTroopsEnabled = true }).CreateSnapshot();
        initial = initial with
        {
            Simulation = initial.Simulation with { Boundary = SimulationBoundary.Stable, Controls = [seat], ActiveControl = 0 },
            Forces = initial.Forces.Select(force => force.Id == 2 ? force with
                { ArmamentReserve = ImmutableDictionary<string, ulong>.Empty.Add("spear", 500) } : force).ToArray(),
            Units = initial.Units.Select(unit => unit.Id == 20 ? unit with { Position = new(37, 7) } : unit).ToArray()
        };
        var engine = GameSession.EngineFactory(content)(WorldState.Restore(initial));
        List<DomainEvent> events = [];

        OriginalAiArmament.Manage(engine, definition.MilitaryRules, seat, events);

        var configured = Assert.Single(events.OfType<UnitArmamentConfiguredEvent>());
        Assert.Equal(500u, configured.IssuedSets);
        Assert.Equal(500u, engine.Snapshot.Units.Single(unit => unit.Id == 20).Detachments[9].Weapon!.RemainingSets);
        Assert.Null(engine.Snapshot.Forces.Single(force => force.Id == 2).ArmamentReserve);
    }

    [Fact]
    public void AiCanResupplyUnitAfterTenIneligibleUnits()
    {
        var basis = Fixture();
        var content = basis with { Military = basis.Military! with
        {
            TroopTypes = [new(100, "枪兵", 1, ImmutableDictionary<int, int>.Empty)],
            ResearchProjects = [new("basic", 100, 30, [])],
            Armaments = [new("spear", "铁矛", ArmamentSlot.Weapon, [100], 8, 0, 100, null)]
        } };
        var definition = new ScenarioSessionDefinition(content);
        var seat = new ControlSeat("ai:2", 2, ControllerKind.Automatic);
        var initial = definition.CreateWorld(42, settings: new NewGameSettings { MixedTroopsEnabled = true }).CreateSnapshot();
        var target = initial.Units.Single(unit => unit.Id == 20) with
        {
            Position = new(37, 7),
            Detachments = initial.Units.Single(unit => unit.Id == 20).Detachments.SetItem(9,
                initial.Units.Single(unit => unit.Id == 20).Detachments[9] with { Weapon = new("spear", 1000, 5000, 0) })
        };
        var idle = Enumerable.Range(100, 10).Select(id => target with
        {
            Id = id, ActiveEngagement = true, CommanderId = id + 1000,
            MemberPositions = ImmutableDictionary<int, FormationPosition>.Empty.Add(id + 1000, target.MemberPositions[9]),
            Detachments = ImmutableDictionary<int, DetachmentState>.Empty.Add(id + 1000, target.Detachments[9])
        }).ToArray();
        target = target with { Id = 200 };
        initial = initial with
        {
            Simulation = initial.Simulation with { Boundary = SimulationBoundary.Stable, Controls = [seat], ActiveControl = 0, NextUnitId = 201 },
            Forces = initial.Forces.Select(force => force.Id == 2 ? force with
                { ArmamentInventory = ImmutableDictionary<string, uint>.Empty.Add("spear", 500) } : force).ToArray(),
            Units = initial.Units.Where(unit => unit.Id != 20).Concat(idle).Append(target).ToArray(),
            Officers = initial.Officers.Select(officer => officer.Id == 9 ? officer with { UnitId = 200 } : officer)
                .Concat(Enumerable.Range(100, 10).Select(id => initial.Officers.Single(officer => officer.Id == 9) with
                    { Id = id + 1000, UnitId = id })).ToArray()
        };
        var engine = GameSession.EngineFactory(content)(WorldState.Restore(initial));
        List<DomainEvent> events = [];

        OriginalAiArmament.Manage(engine, definition.MilitaryRules, seat, events);

        Assert.Equal(200, Assert.Single(events.OfType<UnitArmamentsResuppliedEvent>()).UnitId);
        Assert.Equal(1500u, engine.Snapshot.Units.Single(unit => unit.Id == 200).Detachments[9].Weapon!.RemainingSets);
    }

    [Fact]
    public void AuthoredHiddenEquipmentSurvivesNewGameAndSaveAndRejectsInvalidPlaces()
    {
        var basis = Fixture();
        var definition = new EquipmentDefinition("featured-spear", EquipmentSlot.MainHand, EquipmentRarity.Precious,
            new(2, 0, 0, 0), new(1, 0, 0, 0)) { Name = "玄纹长戟" };
        var hidden = new EquipmentInstance(10000, definition.Id, null, null, false)
            { IsLost = true, SearchPlaceId = basis.Settlements[0].Id };
        var content = basis with { Information = basis.Information with { Equipment = [definition] }, InitialEquipment = [hidden] };
        var session = GameSession.Start(content, 42);
        Assert.Equal(hidden, Assert.Single(session.Snapshot.Equipment, item => item.Id == hidden.Id));
        var restored = SaveService.Decode(SaveService.Encode(session), content);
        Assert.Equal(hidden, Assert.Single(restored.Snapshot.Equipment, item => item.Id == hidden.Id));
        Assert.Throws<InvalidDataException>(() => (content with
            { InitialEquipment = [hidden with { SearchPlaceId = int.MaxValue }] }).Validate());
        Assert.Throws<InvalidDataException>(() => (content with { InitialEquipment = [hidden, hidden] }).Validate());
    }

    [Fact]
    public void AiResuppliesEligibleFieldUnitUsingTheSameInventoryCommand()
    {
        var basis = Fixture();
        var content = basis with { Military = basis.Military! with
        {
            TroopTypes = [new(100, "枪兵", 1, ImmutableDictionary<int, int>.Empty)],
            ResearchProjects = [new("basic", 100, 30, [])],
            Armaments = [new("spear", "铁矛", ArmamentSlot.Weapon, [100], 8, 0, 100, null)]
        } };
        var definition = new ScenarioSessionDefinition(content);
        var seat = new ControlSeat("ai:2", 2, ControllerKind.Automatic);
        var initial = definition.CreateWorld(42, settings: new NewGameSettings { MixedTroopsEnabled = true }).CreateSnapshot();
        initial = initial with
        {
            Simulation = initial.Simulation with { Boundary = SimulationBoundary.Stable, Controls = [seat], ActiveControl = 0 },
            Forces = initial.Forces.Select(force => force.Id == 2 ? force with
                { ArmamentInventory = ImmutableDictionary<string, uint>.Empty.Add("spear", 500) } : force).ToArray(),
            Units = initial.Units.Select(unit => unit.Id == 20 ? unit with
            {
                Position = new(37, 7),
                Detachments = unit.Detachments.SetItem(9, unit.Detachments[9] with { Weapon = new("spear", 1000, 5000, 0) })
            } : unit).ToArray()
        };
        var engine = GameSession.EngineFactory(content)(WorldState.Restore(initial));
        List<DomainEvent> events = [];

        OriginalAiArmament.Manage(engine, definition.MilitaryRules, seat, events);

        Assert.Equal(500u, Assert.Single(Assert.Single(events.OfType<UnitArmamentsResuppliedEvent>()).Lines).Sets);
        Assert.Equal(1500u, engine.Snapshot.Units.Single(unit => unit.Id == 20).Detachments[9].Weapon!.RemainingSets);
    }

    [Fact]
    public void AiIssuesAvailableArmamentsToAnUnarmedFieldDetachment()
    {
        var basis = Fixture();
        var content = basis with { Military = basis.Military! with
        {
            TroopTypes = [new(100, "枪兵", 1, ImmutableDictionary<int, int>.Empty)],
            ResearchProjects = [new("basic", 100, 30, [])],
            Armaments = [new("spear", "铁矛", ArmamentSlot.Weapon, [100], 8, 0, 100, null),
                new("advanced", "铁戟", ArmamentSlot.Weapon, [100], 20, 0, 100, "basic"),
                new("armor", "皮甲", ArmamentSlot.Armor, [], 0, 8, 100, null)]
        } };
        var definition = new ScenarioSessionDefinition(content);
        var seat = new ControlSeat("ai:2", 2, ControllerKind.Automatic);
        var initial = definition.CreateWorld(42, settings: new NewGameSettings { MixedTroopsEnabled = true }).CreateSnapshot();
        initial = initial with
        {
            Simulation = initial.Simulation with { Boundary = SimulationBoundary.Stable, Controls = [seat], ActiveControl = 0 },
            Forces = initial.Forces.Select(force => force.Id == 2 ? force with
                { ArmamentInventory = ImmutableDictionary<string, uint>.Empty.Add("spear", 500).Add("advanced", 900).Add("armor", 400) } : force).ToArray(),
            Units = initial.Units.Select(unit => unit.Id == 20 ? unit with { Position = new(37, 7) } : unit).ToArray()
        };
        var engine = GameSession.EngineFactory(content)(WorldState.Restore(initial));
        List<DomainEvent> events = [];

        OriginalAiArmament.Manage(engine, definition.MilitaryRules, seat, events);

        var detachment = engine.Snapshot.Units.Single(unit => unit.Id == 20).Detachments[9];
        Assert.Equal("spear", detachment.Weapon!.DefinitionId);
        Assert.Equal(500u, detachment.Weapon.RemainingSets);
        Assert.Equal("armor", detachment.Armor!.DefinitionId);
        Assert.Equal(400u, detachment.Armor.RemainingSets);
        Assert.Equal(2, events.OfType<UnitArmamentConfiguredEvent>().Count());
        Assert.Equal(900u, engine.Snapshot.Forces.Single(force => force.Id == 2).ArmamentInventory!["advanced"]);
    }

    [Fact]
    public void AiBuysCompatibleWeaponsWhenOtherWeaponStockCannotEquipTheUnit()
    {
        var basis = Fixture();
        var content = basis with { Military = basis.Military! with
        {
            TroopTypes = [new(100, "弓兵", 1, ImmutableDictionary<int, int>.Empty), new(101, "枪兵", 1, ImmutableDictionary<int, int>.Empty)],
            ResearchProjects = [new("basic", 100, 30, [])],
            Armaments = [new("spear", "铁矛", ArmamentSlot.Weapon, [101], 8, 0, 100, null),
                new("bow", "弓", ArmamentSlot.Weapon, [100], 8, 0, 100, null)]
        } };
        var definition = new ScenarioSessionDefinition(content);
        var seat = new ControlSeat("ai:2", 2, ControllerKind.Automatic);
        var initial = definition.CreateWorld(42, settings: new NewGameSettings { MixedTroopsEnabled = true }).CreateSnapshot();
        initial = initial with
        {
            Simulation = initial.Simulation with { Boundary = SimulationBoundary.Stable, Controls = [seat], ActiveControl = 0 },
            Forces = initial.Forces.Select(force => force.Id == 2 ? force with
                { ArmamentInventory = ImmutableDictionary<string, uint>.Empty.Add("spear", 5000) } : force).ToArray(),
            Units = initial.Units.Select(unit => unit.Id == 20 ? unit with { Position = new(37, 7) } : unit).ToArray()
        };
        var engine = GameSession.EngineFactory(content)(WorldState.Restore(initial));
        List<DomainEvent> events = [];

        OriginalAiArmament.Manage(engine, definition.MilitaryRules, seat, events);

        Assert.Equal("bow", Assert.Single(events.OfType<ArmamentPurchasedEvent>()).DefinitionId);
        Assert.Equal(1000u, engine.Snapshot.Forces.Single(force => force.Id == 2).ArmamentInventory!["bow"]);
        Assert.Equal(5000u, engine.Snapshot.Forces.Single(force => force.Id == 2).ArmamentInventory!["spear"]);
    }

    [Fact]
    public void AiManufacturesArmamentsWithTheSameModuleAndResourceRules()
    {
        var basis = Fixture();
        var content = basis with { Military = basis.Military! with
        {
            TroopTypes = [new(100, "枪兵", 1, ImmutableDictionary<int, int>.Empty)],
            ResearchProjects = [new("basic", 100, 30, [])],
            Armaments = [new("armor", "皮甲", ArmamentSlot.Armor, [], 0, 8, 100, null)
                { ProductionCostPerThousand = 50, ProductionDays = 30 }]
        } };
        var definition = new ScenarioSessionDefinition(content);
        var seat = new ControlSeat("ai:2", 2, ControllerKind.Automatic);
        var initial = definition.CreateWorld(42, settings: new NewGameSettings { MixedTroopsEnabled = true }).CreateSnapshot();
        initial = initial with { Simulation = initial.Simulation with { Boundary = SimulationBoundary.Stable, Controls = [seat], ActiveControl = 0 } };
        var engine = GameSession.EngineFactory(content)(WorldState.Restore(initial));
        var before = engine.Snapshot.Corps.Single(c => c.OwnerId == 2 && c.IsRoot).Treasury.Money;
        List<DomainEvent> events = [];
        OriginalAiArmament.Manage(engine, definition.MilitaryRules, seat, events);
        Assert.Equal("armor", engine.Snapshot.Forces.Single(force => force.Id == 2).ArmamentProduction?.DefinitionId);
        Assert.Equal(before - 50, engine.Snapshot.Corps.Single(c => c.OwnerId == 2 && c.IsRoot).Treasury.Money);
        Assert.Single(events.OfType<ArmamentProductionStartedEvent>());
    }

    [Fact]
    public void MixedTroopAndOtherModuleChoicesSurviveSaveIndependently()
    {
        var basis = Fixture();
        var content = basis with { Military = basis.Military! with
        {
            TroopTypes = [new(100, "枪兵", 1, ImmutableDictionary<int, int>.Empty)],
            ResearchProjects = [new("basic", 100, 30, [])]
        } };
        for (var flags = 0; flags < 16; flags++)
        {
            var settings = new NewGameSettings
            {
                MixedTroopsEnabled = true,
                NaturalDeathsEnabled = (flags & 1) != 0,
                EquipmentDevelopmentEnabled = (flags & 2) != 0,
                FormationLearningEnabled = (flags & 4) != 0,
                PersonalPeerageEnabled = (flags & 8) != 0
            };
            var session = GameSession.Start(content, 42, settings: settings);
            var restored = SaveService.Decode(SaveService.Encode(session), content);
            Assert.Equal(settings, restored.Snapshot.Simulation.NewGame);
            Assert.Equal(settings.FormationLearningEnabled, restored.Snapshot.Simulation.NewGame.FormationTrainingEnabled);
        }
    }

    [Fact]
    public void LegacyTreasureOnlyCatalogAlsoReceivesBasicEquipmentRecipes()
    {
        var basis = Fixture();
        var treasure = new TreasureDefinition(99, "旧宝物", 1, 2, 0, null);
        var state = new TreasureState(99, 1, TreasureDiscovery.Discovered, null);
        var legacy = LegacyEquipmentMigration.Convert([treasure], [state]);
        var content = basis with { Information = basis.Information with
        {
            Treasures = [treasure], TreasureStates = [state], Equipment = legacy.Definitions
        } };
        var rules = new ScenarioSessionDefinition(content).Rules;
        Assert.Contains("basic-spear", rules.Equipment.Items.Keys);
        Assert.Contains(rules.Equipment.Items.Values, item => item.LegacyTreasureId == 99);
        Assert.Contains(EquipmentPreset.ResearchId, rules.Military!.ResearchProjects.Keys);
    }

    [Fact]
    public void UnknownLegacyTreasureCategoryRemainsPlayableAsTreasureSlot()
    {
        var basis = Fixture();
        var treasure = new TreasureDefinition(99, "legacy-item", 19, 2, 0, null);
        var state = new TreasureState(99, 1, TreasureDiscovery.Discovered, null);
        var content = basis with { Information = basis.Information with { Treasures = [treasure], TreasureStates = [state] } };
        var session = GameSession.Start(content, 42);
        var item = Assert.Single(session.Snapshot.Equipment, item => item.LegacyTreasureId == 99);
        Assert.Equal(EquipmentSlot.Treasure, new ScenarioSessionDefinition(content).Rules.Equipment.Items[item.DefinitionId].Slot);
        var texts = new San9.Content.Text.TextCatalog(San9.Content.Text.TextCatalog.CurrentSchemaVersion,
            new Dictionary<string, string> { ["legacy-item"] = "旧宝物", ["person.1"] = "君主" });
        Assert.Equal("宝物", Assert.Single(TreasureCatalog.Read(session.View, texts).Rows).Cells[1].Text);
    }

    [Fact]
    public void EquipmentWithUnknownTacticReferenceIsRejected()
    {
        var basis = Fixture();
        var item = new EquipmentDefinition("invalid-tactic", EquipmentSlot.MainHand, EquipmentRarity.Ordinary,
            default, default) { TacticId = 999999 };
        var content = basis with { Information = basis.Information with { Equipment = [item] } };
        Assert.Throws<InvalidDataException>(() => content.Validate());
    }

    [Fact]
    public void MigratedTreasureWithUnknownTacticReferenceIsRejected()
    {
        var basis = Fixture();
        var treasure = new TreasureDefinition(99, "legacy-item", 1, 2, 0, 999999);
        var state = new TreasureState(99, 1, TreasureDiscovery.Discovered, null);
        var content = basis with { Information = basis.Information with { Treasures = [treasure], TreasureStates = [state] } };
        Assert.Throws<InvalidDataException>(() => content.Validate());
    }

    [Fact]
    public void PlayerCannotResearchOrManufactureObsoleteOfficerEquipment()
    {
        var basis = Fixture();
        var content = basis with
        {
            Units = [],
            Officers = basis.Officers.Select(person => person.State.UnitId is null ? person : person with
            {
                State = person.State with { UnitId = null, SettlementId = person.State.OwnerId }
            }).ToArray()
        };
        var session = GameSession.Start(content, 42);
        Assert.False(session.Execute(new("player"), new StartResearchCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, 1, EquipmentPreset.ResearchId)).Submission.Accepted);
        Assert.DoesNotContain(EquipmentPreset.ResearchId, session.Snapshot.Forces.Single(f => f.Id == 1).CompletedResearch);
        Assert.Equal(CommandRejection.InvalidTask, session.Execute(new("player"), new StartEquipmentProductionCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, 1, "basic-spear")).Submission.Rejection);
        var restored = SaveService.Decode(SaveService.Encode(session), content);
        Assert.DoesNotContain(restored.Snapshot.Equipment, item => item.DefinitionId == "basic-spear");
        StateAssert.Equal(session.Capture(), restored.Capture());
    }

    [Fact]
    public void PlayerCannotPurchaseOrdinaryOfficerEquipment()
    {
        var content = Fixture();
        var session = GameSession.Start(content, 42);
        long before = session.Snapshot.Corps.Single(c => c.OwnerId == 1 && c.IsRoot).Treasury.Money;
        var id = Guid.NewGuid();
        var buy = new PurchaseEquipmentCommand(id, 1, session.Snapshot.Simulation.NextSequence, 1, "basic-spear");
        var result = session.Execute(new("player"), buy);
        Assert.Equal(CommandRejection.InvalidTask, result.Submission.Rejection);
        Assert.Equal(before, session.Snapshot.Corps.Single(c => c.OwnerId == 1 && c.IsRoot).Treasury.Money);
        Assert.DoesNotContain(session.Snapshot.Equipment, item => item.DefinitionId == "basic-spear");
        Assert.False(session.Execute(new("player"), new PurchaseEquipmentCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, 1, "not-for-sale")).Submission.Accepted);
        var loaded = SaveService.Decode(SaveService.Encode(session), content);
        Assert.DoesNotContain(loaded.Snapshot.Equipment, item => item.DefinitionId == "basic-spear");
    }

    [Fact]
    public void PurchaseRespectsResearchAndFundsWithoutChangingInventory()
    {
        var basis = Fixture();
        var item = new EquipmentDefinition("advanced", EquipmentSlot.MainHand, EquipmentRarity.Ordinary, default, default)
        { Name = "精制长枪", PurchaseCost = 100, PurchaseResearchProjectId = EquipmentPreset.ResearchId };
        var content = basis with { Information = basis.Information with { Equipment = EquipmentPreset.Basic().Add(item) } };
        var original = GameSession.Start(content, 42);
        PurchaseEquipmentCommand Command(GameSession session) => new(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, 1, "advanced");
        Assert.False(original.Execute(new("player"), Command(original)).Submission.Accepted);
        Assert.DoesNotContain(original.Snapshot.Equipment, equipment => equipment.DefinitionId == "advanced");
        var save = original.Capture();
        save = save with { World = save.World with
        {
            Forces = save.World.Forces.Select(force => force.Id == 1 ? force with { CompletedResearch = [EquipmentPreset.ResearchId] } : force).ToArray(),
            Corps = save.World.Corps.Select(corps => corps.OwnerId == 1 && corps.IsRoot ? corps with
                { Treasury = corps.Treasury with { Money = 0 } } : corps).ToArray()
        } };
        var poor = GameSession.Restore(content, save);
        Assert.Equal(CommandRejection.InvalidTask, poor.Execute(new("player"), Command(poor)).Submission.Rejection);
        Assert.DoesNotContain(poor.Snapshot.Equipment, equipment => equipment.DefinitionId == "advanced");
    }

    [Fact]
    public void AiPrefersLegalEquipmentWithACombatEffect()
    {
        var basis = Fixture();
        var plain = new EquipmentDefinition("plain-feature", EquipmentSlot.MainHand, EquipmentRarity.Precious, default, default);
        var useful = plain with { Id = "combat-feature", CombatEffects = new(AttackPercent: 5) };
        var content = basis with { Information = basis.Information with { Equipment = [plain, useful] } };
        var definition = new ScenarioSessionDefinition(content);
        var seat = new ControlSeat("ai:2", 2, ControllerKind.Automatic);
        var initial = definition.CreateWorld(42).CreateSnapshot();
        initial = initial with { Equipment = [.. initial.Equipment, new EquipmentInstance(500, plain.Id, null, 2, false),
            new EquipmentInstance(501, useful.Id, null, 2, false)],
            Simulation = initial.Simulation with { Boundary = SimulationBoundary.Stable, Controls = [seat], ActiveControl = 0 } };
        var engine = GameSession.EngineFactory(content)(WorldState.Restore(initial));
        List<DomainEvent> events = [];

        OriginalAiEquipment.Manage(engine, definition.Rules.Equipment, seat, events);

        Assert.True(engine.Snapshot.Equipment.Single(item => item.Id == 501).Equipped);
        Assert.Equal(2, engine.Snapshot.Equipment.Single(item => item.Id == 501).OfficerId);
    }

    [Fact]
    public void AiEquipsItsRulerAndTransfersSpareFeaturedEquipmentToAnotherOfficer()
    {
        var basis = Fixture();
        var strong = new EquipmentDefinition("strong-feature", EquipmentSlot.MainHand, EquipmentRarity.Precious,
            new(8, 0, 0, 0), default) { Name = "强剑" };
        var spare = strong with { Id = "spare-feature", Name = "长剑", BaseBonuses = new(4, 0, 0, 0) };
        var content = basis with { Information = basis.Information with { Equipment = [strong, spare] } };
        var definition = new ScenarioSessionDefinition(content);
        var seat = new ControlSeat("ai:2", 2, ControllerKind.Automatic);
        var initial = definition.CreateWorld(42).CreateSnapshot();
        initial = initial with { Equipment = [.. initial.Equipment, new EquipmentInstance(500, strong.Id, 2, null, false),
            new EquipmentInstance(501, spare.Id, 2, null, false)],
            Simulation = initial.Simulation with { Boundary = SimulationBoundary.Stable, Controls = [seat], ActiveControl = 0 } };
        var engine = GameSession.EngineFactory(content)(WorldState.Restore(initial));
        List<DomainEvent> events = [];

        OriginalAiEquipment.Manage(engine, definition.Rules.Equipment, seat, events);

        Assert.True(engine.Snapshot.Equipment.Single(item => item.Id == 500).Equipped);
        var transferred = engine.Snapshot.Equipment.Single(item => item.Id == 501);
        Assert.Equal(4, transferred.OfficerId);
        Assert.True(transferred.Equipped);
        Assert.Contains(events, e => e is EquipmentTransferredEvent { EquipmentId: 501, PreviousOfficerId: 2, OfficerId: 4 });
    }

    [Fact]
    public void AiDoesNotTransferFeaturedEquipmentWhenNoOfficerMeetsItsRequirement()
    {
        var basis = Fixture();
        var item = new EquipmentDefinition("demanding-feature", EquipmentSlot.MainHand, EquipmentRarity.Precious,
            new(4, 0, 0, 0), default) { MinimumAbilities = new(100, 0, 0, 0) };
        var content = basis with { Information = basis.Information with { Equipment = [item] } };
        var definition = new ScenarioSessionDefinition(content);
        var seat = new ControlSeat("ai:2", 2, ControllerKind.Automatic);
        var initial = definition.CreateWorld(42).CreateSnapshot();
        initial = initial with { Equipment = [.. initial.Equipment, new EquipmentInstance(501, item.Id, 2, null, false)],
            Simulation = initial.Simulation with { Boundary = SimulationBoundary.Stable, Controls = [seat], ActiveControl = 0 } };
        var engine = GameSession.EngineFactory(content)(WorldState.Restore(initial));
        List<DomainEvent> events = [];

        OriginalAiEquipment.Manage(engine, definition.Rules.Equipment, seat, events);

        var held = engine.Snapshot.Equipment.Single(value => value.Id == 501);
        Assert.Equal(2, held.OfficerId);
        Assert.False(held.Equipped);
        Assert.DoesNotContain(events, e => e is EquipmentTransferredEvent { EquipmentId: 501 });
    }

    [Fact]
    public void AiDoesNotBuyOrdinaryOfficerEquipment()
    {
        var source = Fixture();
        var content = source with { Information = source.Information with { Equipment = EquipmentPreset.Basic() } };
        var definition = new ScenarioSessionDefinition(content);
        var seat = new ControlSeat("ai:2", 2, ControllerKind.Automatic);
        var initial = definition.CreateWorld(42).CreateSnapshot();
        initial = initial with { Equipment = initial.Equipment.Where(item => item.OfficerId != 2).ToArray(),
            Simulation = initial.Simulation with { Boundary = SimulationBoundary.Stable, Controls = [seat], ActiveControl = 0 } };
        var engine = GameSession.EngineFactory(content)(WorldState.Restore(initial));
        List<DomainEvent> events = [];
        OriginalAiEquipment.Manage(engine, definition.Rules.Equipment, seat, events);
        Assert.Empty(engine.Snapshot.Forces.Single(force => force.Id == 2).CompletedResearch);
        Assert.Empty(events.OfType<EquipmentPurchasedEvent>());
    }

    [Fact]
    public void AiResearchesEquipsAndProducesPersonalEquipmentWithoutMixedTroops()
    {
        var content = Fixture();
        var definition = new ScenarioSessionDefinition(content);
        var seat = new ControlSeat("ai:2", 2, ControllerKind.Automatic);
        var initial = definition.CreateWorld(42).CreateSnapshot();
        initial = initial with { Simulation = initial.Simulation with { Boundary = SimulationBoundary.Stable, Controls = [seat], ActiveControl = 0 } };
        var researching = GameSession.EngineFactory(content)(WorldState.Restore(initial));
        List<DomainEvent> researchEvents = [];
        OriginalAiResearch.TryStart(researching, definition.MilitaryRules, seat, researchEvents, definition.Rules.Equipment);
        Assert.Empty(researchEvents.OfType<ResearchStartedEvent>());
        var ready = initial with
        {
            Forces = initial.Forces.Select(force => force.Id == 2 ? force with { CompletedResearch = [EquipmentPreset.ResearchId] } : force).ToArray(),
            Equipment = [.. initial.Equipment, new EquipmentInstance(500, "basic-spear", null, 2, false)]
        };
        var first = GameSession.EngineFactory(content)(WorldState.Restore(ready));
        var second = GameSession.EngineFactory(content)(WorldState.Restore(ready));
        List<DomainEvent> firstEvents = [], secondEvents = [];
        OriginalAiEquipment.Manage(first, definition.Rules.Equipment, seat, firstEvents);
        OriginalAiEquipment.Manage(second, definition.Rules.Equipment, seat, secondEvents);
        Assert.False(first.Snapshot.Equipment.Single(item => item.Id == 500).Equipped);
        Assert.Null(first.Snapshot.Forces.Single(force => force.Id == 2).EquipmentProduction);
        Assert.DoesNotContain(firstEvents, e => e is EquipmentProductionStartedEvent);
        StateAssert.Equal(first.Snapshot, second.Snapshot);
        EventAssert.Equal(firstEvents, secondEvents);
    }

    [Fact]
    public void ExistingFieldUnitsReceiveDeterministicDetachmentsAndAiResearchesTheirEquipment()
    {
        var basis = Fixture();
        var troopTypes = new[]
        {
            new TroopTypeDefinition(100, "枪兵", 2, ImmutableDictionary<int, int>.Empty)
                { LevelResearchProjects = ImmutableDictionary<int, string>.Empty.Add(2, "spear") },
            new TroopTypeDefinition(200, "盾兵", 2, ImmutableDictionary<int, int>.Empty)
                { LevelResearchProjects = ImmutableDictionary<int, string>.Empty.Add(2, "shield") }
        };
        var content = basis with { Military = basis.Military! with
        {
            TroopTypes = [.. troopTypes],
            ResearchProjects = [new("spear", 1000, 30, []), new("shield", 100, 30, [])]
        } };
        var definition = new ScenarioSessionDefinition(content);
        var settings = new NewGameSettings { MixedTroopsEnabled = true };
        var first = definition.CreateWorld(42, settings: settings).CreateSnapshot();
        var second = definition.CreateWorld(42, settings: settings).CreateSnapshot();
        var firstUnit = first.Units.Single(unit => unit.Id == 20);
        Assert.Equal(firstUnit.Detachments, second.Units.Single(unit => unit.Id == 20).Detachments);
        Assert.Equal(firstUnit.Soldiers, firstUnit.Detachments.Values.Single().Soldiers);
        Assert.Equal(100, firstUnit.Detachments.Values.Single().TroopTypeId);

        var seat = new ControlSeat("ai:2", 2, ControllerKind.Automatic);
        var state = first with { Simulation = first.Simulation with
        {
            Boundary = SimulationBoundary.Stable, Controls = [seat], ActiveControl = 0
        } };
        var engine = GameSession.EngineFactory(content)(WorldState.Restore(state));
        List<DomainEvent> events = [];
        OriginalAiResearch.TryStart(engine, definition.MilitaryRules, seat, events);
        Assert.Equal("spear", Assert.Single(events.OfType<ResearchStartedEvent>()).ProjectId);
        Assert.Equal("spear", engine.Snapshot.Forces.Single(force => force.Id == 2).ActiveResearch?.ProjectId);
    }

    [Fact]
    public void ExistingSiegeFormationBecomesEquipmentWithoutRequiringResearch()
    {
        var basis = Fixture();
        var original = basis.Units.Single(unit => unit.Id == 20);
        var siegeFormation = basis.Military!.Formations[0] with
        {
            Id = 401, Combat = new(10, 10, 10, 10, 50), Attack = new(10, true)
        };
        var content = basis with
        {
            Units = basis.Units.Select(unit => unit.Id == original.Id ? unit with { FormationId = 401 } : unit).ToArray(),
            Military = basis.Military with
            {
                Formations = [basis.Military.Formations[0], siegeFormation], DefaultDeploymentFormationId = 400,
                TroopTypes = [new(100, "枪兵", 1, ImmutableDictionary<int, int>.Empty)],
                ResearchProjects = [new("siege", 100, 30, [])],
                SiegeEquipment = [new(10, "冲车", "siege", 401, 100)]
            }
        };
        var world = new ScenarioSessionDefinition(content).CreateWorld(42,
            settings: new NewGameSettings { MixedTroopsEnabled = true }).CreateSnapshot();
        var unit = world.Units.Single(item => item.Id == original.Id);
        Assert.Equal(400, unit.FormationId);
        Assert.Equal(10, unit.SiegeEquipmentId);
        Assert.Equal(original.MemberPositions, unit.MemberPositions);
        Assert.Empty(world.Forces.Single(force => force.Id == original.OwnerId).CompletedResearch);
    }

    [Fact]
    public void PreviousShapeSaveDefaultsMixedRulesToDisabled()
    {
        var content = Fixture();
        var session = GameSession.Start(content, 42);
        var json = JsonNode.Parse(SaveService.Encode(session))!;
        var world = json["session"]!["world"]!;
        world["simulation"]!["newGame"]!.AsObject().Remove("mixedTroopsEnabled");
        foreach (var force in world["forces"]!.AsArray())
        {
            force!.AsObject().Remove("completedResearch");
            force.AsObject().Remove("activeResearch");
        }
        foreach (var unit in world["units"]!.AsArray())
        {
            unit!.AsObject().Remove("detachments");
            unit.AsObject().Remove("navalTrainingId");
            unit.AsObject().Remove("siegeEquipmentId");
        }
        var restored = SaveService.Decode(Encoding.UTF8.GetBytes(json.ToJsonString()), content);
        Assert.False(restored.Snapshot.Simulation.NewGame.MixedTroopsEnabled);
        Assert.All(restored.Snapshot.Units, unit => Assert.Empty(unit.Detachments));
        Assert.All(restored.Snapshot.Forces, force => Assert.Empty(force.CompletedResearch));
    }

    [Fact]
    public void MixedResearchSurvivesSaveAndCommandLogReplay()
    {
        var basis = Fixture();
        var content = basis with { Military = basis.Military! with
        {
            TroopTypes = [new(100, "枪兵", 1, ImmutableDictionary<int, int>.Empty)],
            ResearchProjects = [new("training", 100, 30, [])]
        } };
        var settings = new NewGameSettings { MixedTroopsEnabled = true };
        var first = GameSession.Start(content, 42, settings: settings);
        var replay = GameSession.Start(content, 42, settings: settings);
        Guid id = Guid.NewGuid();
        var request = first.Request(new CommandOperation(new StartResearchCommand(id, 1,
            first.Snapshot.Simulation.NextSequence, 1, "training")), id: id);
        var path = Path.GetTempFileName();
        try
        {
            CommandLogService.Write(path, CommandLogService.CreateHeader(first), [request]);
            Assert.Equal(SessionDisposition.Accepted, first.Apply(request).Disposition);
            Assert.Equal(1, CommandLogService.Replay(path, replay).Accepted);
            var restored = SaveService.Decode(SaveService.Encode(first), content);
            StateAssert.Equal(first.Capture(), restored.Capture());
            StateAssert.Equal(first.Capture(), replay.Capture());
            Assert.Equal("training", restored.Snapshot.Forces.Single(force => force.Id == 1).ActiveResearch?.ProjectId);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void PreviousShapePresetDefaultsEquipmentAndMemberDetachments()
    {
        var preset = new DeploymentPreset(Guid.NewGuid(), 1, "原编队",
            [new DeploymentPresetMember(1, new FormationSlot(0, 0), null)], 400, null,
            DeploymentSoldierMode.Fixed, 100);
        var json = JsonNode.Parse(CanonicalJson.Encode(preset))!;
        json.AsObject().Remove("navalTrainingId");
        json.AsObject().Remove("siegeEquipmentId");
        json["members"]![0]!.AsObject().Remove("detachment");
        var restored = StrictJson.Deserialize<DeploymentPreset>(Encoding.UTF8.GetBytes(json.ToJsonString()));
        Assert.Null(restored.NavalTrainingId);
        Assert.Null(restored.SiegeEquipmentId);
        Assert.Null(Assert.Single(restored.Members).Detachment);
    }
}
