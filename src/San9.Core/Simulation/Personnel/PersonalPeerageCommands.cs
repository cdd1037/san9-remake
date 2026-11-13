using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record SetPersonalPeerageCommand(Guid CommandId, int IssuerId, long Sequence, int OfficerId,
    PersonalPeerage? Peerage) : GameCommand(CommandId, IssuerId, Sequence);
public sealed record PersonalPeerageChangedEvent(GameClock Clock, int ForceId, int OfficerId,
    PersonalPeerage? Previous, PersonalPeerage? Current) : DomainEvent;

internal static class PersonalPeerageCommands
{
    internal static CommandRejection Eligibility(WorldMutation world, SetPersonalPeerageCommand command, SimulationRules rules)
    {
        if (world.Simulation.NewGame.PersonalPeerageEnabled == false) return CommandRejection.InvalidTask;
        if (!world.Officers.TryGetValue(command.OfficerId, out var person)) return CommandRejection.UnknownPerson;
        if (!person.IsServing || person.OwnerId != command.IssuerId || !world.Forces.TryGetValue(command.IssuerId, out var force) ||
            force.LeaderId == person.Id) return CommandRejection.PersonUnavailable;
        if (command.Peerage is { IsValid: false }) return CommandRejection.InvalidTask;
        if (command.Peerage == person.PersonalPeerage) return CommandRejection.PeerageUnchanged;
        if (force.PersonalPeerageRegimeId is not string regimeId ||
            !rules.Economy.PersonalPeerageRegimes.TryGetValue(regimeId, out var regime) ||
            regime.Period(world.Clock.Year) is not { } period) return CommandRejection.PeerageRegimeUnavailable;
        if (force.RankId is not int rank) return CommandRejection.PeerageUnauthorized;
        var policy = period.Policy(rules.Economy.PersonalPeerages);
        bool emperor = rules.Politics.ImperialRankId is int imperial && rank == imperial;
        CommandRejection Authority(PersonalPeerageGrade grade, bool granting)
        {
            if (!period.AuthorizedRanks.TryGetValue(grade, out var authorized) || !authorized.Contains(rank)) return CommandRejection.PeerageUnauthorized;
            if (grade is PersonalPeerageGrade.Duke or PersonalPeerageGrade.King && !emperor) return CommandRejection.PeerageEmperorRequired;
            if (granting && grade == PersonalPeerageGrade.King && (person.BloodlineId is not int lineage || !period.RoyalBloodlines.Contains(lineage)))
                return CommandRejection.PeerageRoyalLineageRequired;
            if (grade == PersonalPeerageGrade.King && (ImperialCourt.City(world, rules.Economy) is not int city || world.Settlements[city].OwnerId != force.Id))
                return CommandRejection.PeerageCourtControlRequired;
            return CommandRejection.None;
        }
        if (command.Peerage is { } requested && Authority(requested.Grade, true) is var targetRejection && targetRejection != CommandRejection.None) return targetRejection;
        if (person.PersonalPeerage is { } existing && Authority(existing.Grade, false) is var existingRejection && existingRejection != CommandRejection.None) return existingRejection;
        if (command.Peerage is { } next && person.PersonalPeerage?.Grade != next.Grade)
        {
            int count = world.Officers.Values.Count(p => p.Id != person.Id && p.IsServing && p.OwnerId == force.Id && p.PersonalPeerage?.Grade == next.Grade);
            if (!policy.CanGrant(next.Grade, count, true, next.Grade != PersonalPeerageGrade.King || person.BloodlineId is int blood && period.RoyalBloodlines.Contains(blood)))
                return CommandRejection.PeerageQuotaFull;
        }
        return CommandRejection.None;
    }

    internal static CommandRejection Apply(WorldMutation world, SetPersonalPeerageCommand command, SimulationRules rules, List<DomainEvent> events)
    {
        var rejection = Eligibility(world, command, rules);
        if (rejection != CommandRejection.None) return rejection;
        var person = world.Officers[command.OfficerId];
        var force = world.Forces[command.IssuerId];
        var period = rules.Economy.PersonalPeerageRegimes[force.PersonalPeerageRegimeId!].Period(world.Clock.Year)!;
        int change = period.Policy(rules.Economy.PersonalPeerages).LoyaltyChange(person.PersonalPeerage?.Grade, command.Peerage?.Grade);
        world.Replace(person with { PersonalPeerage = command.Peerage });
        RecruitmentRules.SetLoyalty(world, person.Id, command.IssuerId,
            (int)Math.Clamp((long)person.Loyalty + change, 0, 255), "personal-peerage", events);
        events.Add(new PersonalPeerageChangedEvent(world.Clock, command.IssuerId, person.Id, person.PersonalPeerage, command.Peerage));
        return CommandRejection.None;
    }
}
