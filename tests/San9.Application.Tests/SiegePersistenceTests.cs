using System.Collections.Immutable;
using System.Text.Json.Nodes;
using San9.Application.Persistence;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Application.Tests;

public sealed class SiegePersistenceTests
{
    [Fact]
    public void SparseCreditSurvivesExactAndPlayerLoadingAndSelectsTheSameRecipient()
    {
        var basis = MissionScenario.Create();
        var definition = basis with
        {
            OriginalAi = basis.OriginalAi with { WorkRules = basis.OriginalAi.WorkRules.ToImmutableDictionary(
                p => p.Key, p => p.Value with { OpeningWaitDays = 10000 }) },
            Military = basis.Military! with
            {
                Formations = [.. basis.Military.Formations.Select(f => f with
                    { Combat = new(10, 10, 0, 10, 50), Attack = new(1, false), CaptureRanks = [.. Enumerable.Repeat(BattleRank.Middle, f.OfficerCapacity)] })],
                Places = [.. basis.Military.Places.Select(p => p with { Combat = new(10, 10, 0, 10, 50) })]
            },
            Settlements = [.. basis.Settlements.Select(s => s.Id == 3 ? s with { Soldiers = 1 } : s)],
            Units = [.. basis.Units.Select(u => u with
            {
                Position = new(100, 7), Order = new(MilitaryOrderKind.AttackSettlement, null, 3),
                MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(u.Id == 10 ? 3 : 23, BattleRank.Middle),
                Condition = u.Id == 10 ? UnitCondition.Confused : UnitCondition.Normal,
                ConditionDaysRemaining = u.Id == 10 ? 100 : 0
            })]
        };
        var initial = GameSession.Start(definition, 42).Capture();
        var contribution = new SiegeContribution(500, ImmutableDictionary<int, long>.Empty.Add(10, 500));
        var world = initial.World with { Settlements = initial.World.Settlements.Select(s => s.Id == 3 ? s with
            { Soldiers = 1, SiegeContributions = ImmutableDictionary<int, SiegeContribution>.Empty.Add(1, contribution) } : s).ToArray() };
        var session = GameSession.Restore(definition, initial with { World = world });
        byte[] bytes = SaveService.Encode(session);
        var savedPlaces = JsonNode.Parse(bytes)!["session"]!["world"]!["settlements"]!.AsArray();
        Assert.Single(savedPlaces, s => s!.AsObject().ContainsKey("siegeContributions"));
        Assert.Single(savedPlaces.Single(s => (int)s!["id"]! == 3)!["siegeContributions"]!.AsObject());
        var exact = SaveService.Decode(bytes, definition);
        var player = SaveService.Load(bytes, definition);
        Assert.Equal(500, exact.Snapshot.Settlements.Single(s => s.Id == 3).SiegeContributions![1].Score);
        Assert.Equal(500, player.Snapshot.Settlements.Single(s => s.Id == 3).SiegeContributions![1].Score);

        var first = session.AdvanceXun();
        var second = exact.AdvanceXun();
        EventAssert.Equal(first.Events, second.Events);
        Assert.Equal(SaveService.Encode(session), SaveService.Encode(exact));
        var capture = Assert.Single(first.Events.OfType<SettlementCapturedEvent>());
        Assert.Equal(1, capture.Source.ForceId);
        Assert.Equal(2, capture.Attacker.ForceId);
        Assert.Equal(1, session.Snapshot.Settlements.Single(s => s.Id == 3).OwnerId);
        Assert.Null(capture.Result.SiegeContributions);
        var nextSiege = session.Snapshot.Settlements.Single(s => s.Id == 3).SiegeContributions!;
        long newDamage = first.Events.Skip(first.Events.ToList().IndexOf(capture) + 1).OfType<BattleDamageEvent>()
            .Where(e => e.SettlementId == 3 && e.AttackerId == 20 && !e.AttackerIsSettlement)
            .Sum(e => e.SoldierLoss + 20L * e.DurabilityLoss);
        Assert.Equal(2, Assert.Single(nextSiege).Key);
        Assert.Equal(newDamage, nextSiege[2].Score);
        Assert.Contains(session.History, f => f.ForceId == 1 && f.Role == "city-attacked");
    }

    [Fact]
    public void SaveValidationRejectsUnknownForceAndInvalidContributionTotals()
    {
        var definition = MissionScenario.Create();
        var initial = GameSession.Start(definition, 42);
        foreach (var entry in new[]
        {
            KeyValuePair.Create(99, new SiegeContribution(10, ImmutableDictionary<int, long>.Empty)),
            KeyValuePair.Create(1, new SiegeContribution(10, ImmutableDictionary<int, long>.Empty.Add(10, 11)))
        })
        {
            var envelope = SaveService.Capture(initial);
            var world = envelope.Session.World;
            var malformed = envelope with { Session = envelope.Session with { World = world with
            {
                Settlements = world.Settlements.Select(s => s.Id == 3 ? s with
                    { SiegeContributions = ImmutableDictionary<int, SiegeContribution>.Empty.Add(entry.Key, entry.Value) } : s).ToArray()
            } } };
            Assert.Equal(SaveFailure.InvalidState, Assert.Throws<SaveException>(() =>
                SaveService.Decode(CanonicalJson.Encode(malformed), definition)).Failure);
        }
    }
}
