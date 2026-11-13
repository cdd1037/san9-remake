using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application;

internal static class PersonalPeerageMigration
{
    internal static OfficerState[] Initialize(IEnumerable<OfficerState> officers, IEnumerable<OfficerInitialState> content, GameClock clock)
    {
        var evidence = content.Where(o => o.PersonalPeerageEvidenceRecords.Any())
            .ToDictionary(o => o.State.Id, o => o.PersonalPeerageEvidenceRecords.ToArray());
        return officers.Select(person =>
        {
            var title = person.PersonalPeerage;
            if (title is null && person.Status != OfficerStatus.Dead && evidence.TryGetValue(person.Id, out var records))
                title = records.SingleOrDefault(record => record.Applies(clock))?.Peerage;
            return title == person.PersonalPeerage ? person : person with { PersonalPeerage = title };
        }).ToArray();
    }

    internal static SessionSnapshot Initialize(SessionSnapshot snapshot, ScenarioInitialState content)
    {
        if (snapshot.PersonalPeerageInitialized == true) return snapshot;
        if (snapshot.World.Simulation.NewGame.PersonalPeerageEnabled == false) return snapshot with { PersonalPeerageInitialized = true };
        var original = content.Forces.ToDictionary(f => f.Id);
        return snapshot with { PersonalPeerageInitialized = true, World = snapshot.World with
        {
            Officers = Initialize(snapshot.World.Officers, content.Officers, snapshot.World.Clock),
            Forces = snapshot.World.Forces.Select(force =>
            {
                if (force.PersonalPeerageRegimeId is not null) return force;
                var country = force.CountryTitleId is int id ? content.Information.CountryTitles.FirstOrDefault(t => t.Id == id) : null;
                var regime = country?.Rule.PersonalPeerageRegimeId ?? original.GetValueOrDefault(force.Id)?.PersonalPeerageRegimeId;
                return regime is null ? force : force with { PersonalPeerageRegimeId = regime };
            }).ToArray()
        } };
    }
}
