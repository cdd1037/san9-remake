namespace San9.Core.Domain;

public enum DomesticActivity
{
    Patrol,
    Commerce,
    Cultivation,
    Repair,
    Recruit,
    Train,
}

public readonly record struct DomesticDevelopmentResult(int BaseIncrement, int Increment, int Cost);

/// <summary>Base development before combination bonuses; BEH-DOMESTIC-0001.</summary>
public static class DomesticDevelopment
{
    public static int ParticipantAbility(DomesticActivity activity, OfficerAbilities abilities) => activity switch
    {
        DomesticActivity.Patrol => abilities.Intelligence,
        DomesticActivity.Commerce or DomesticActivity.Cultivation => abilities.Politics,
        DomesticActivity.Repair or DomesticActivity.Recruit => abilities.Leadership,
        DomesticActivity.Train => abilities.War,
        _ => throw new ArgumentOutOfRangeException(nameof(activity)),
    };

    public static int TrainingIncrement(uint soldiers, int morale, int trust, IReadOnlyList<OfficerAbilities> participants)
    {
        if (participants.Count is < 1 or > 5 || morale is < 0 or > 100 || soldiers == 0)
            throw new ArgumentException("Invalid training inputs.");
        long scale = 10L * (11 - participants.Count) * (EconomyMath.RecruitMorale(trust) - morale + 100) / 100;
        return (int)Math.Min(100 - morale, participants.Count + participants.Sum(p => EconomyMath.ExecutedAbility(ParticipantAbility(DomesticActivity.Train, p))) *
            scale / EconomyMath.SquareRoot(soldiers + 10000L) / 10);
    }

    public static int RecruitCount(SettlementState city, uint funds, IReadOnlyList<OfficerAbilities> participants) =>
        RecruitCount(city.DraftPopulation, city.Soldiers, city.SoldierCapacity, funds, participants);

    public static int RecruitCount(uint draftPopulation, uint soldiers, uint soldierCapacity, uint funds, IReadOnlyList<OfficerAbilities> participants)
    {
        if (participants.Count == 0) return 0;
        long potential = 3 * (100 * participants.Sum(p => (long)ParticipantAbility(DomesticActivity.Recruit, p)) / (participants.Count + 4)) / 2;
        return (int)Math.Min(Math.Min(potential, draftPopulation), Math.Min(10L * funds, Math.Max(0L, (long)soldierCapacity - soldiers)));
    }

    public static OfficerState[] OrderParticipants(IEnumerable<OfficerState> people,
        IReadOnlyDictionary<int, OfficerAbilities> abilities, DomesticActivity activity) => people
        .OrderByDescending(p => p.Status == OfficerStatus.Ruler)
        .ThenByDescending(p => ParticipantAbility(activity, abilities[p.Id]))
        .ThenBy(p => p.Id).ToArray();

    public static DomesticDevelopmentResult CalculateResult(
        DomesticActivity activity,
        int current,
        int maximum,
        int funds,
        IReadOnlyList<OfficerAbilities> participants,
        bool doubled)
    {
        int baseIncrement = CalculateBaseEffect(activity, current, maximum, funds, participants);
        int cost = 50 * participants.Count;
        if (funds < cost)
            throw new ArgumentException("Funds cannot cover the development cost.", nameof(funds));
        int increment = (int)Math.Min((long)maximum - current, (doubled ? 2L : 1L) * baseIncrement);
        return new DomesticDevelopmentResult(baseIncrement, increment, cost);
    }

    public static int CalculateBaseEffect(
        DomesticActivity activity,
        int current,
        int maximum,
        int funds,
        IReadOnlyList<OfficerAbilities> participants)
    {
        ArgumentNullException.ThrowIfNull(participants);
        if (activity is < DomesticActivity.Patrol or > DomesticActivity.Repair)
            throw new ArgumentOutOfRangeException(nameof(activity));
        ArgumentOutOfRangeException.ThrowIfNegative(current);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximum, current);
        ArgumentOutOfRangeException.ThrowIfNegative(funds);
        if (participants.Count is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(participants));

        long abilitySum = 0;
        foreach (OfficerAbilities abilities in participants)
        {
            if (abilities.War < 0 || abilities.Intelligence < 0 || abilities.Politics < 0 || abilities.Leadership < 0)
                throw new ArgumentOutOfRangeException(nameof(participants));
            abilitySum += ParticipantAbility(activity, abilities);
        }

        long missing = maximum - current;
        long scale = 5 * (21 - participants.Count) * IntegerSquareRoot(4 * missing) / 100;
        long weighted = abilitySum * scale / 1000;
        long effect = participants.Count + (activity == DomesticActivity.Patrol ? 9 * weighted / 5 : 3 * weighted);
        long cap = activity == DomesticActivity.Repair ? Math.Min(missing, funds) : missing;
        return (int)Math.Min(cap, effect);
    }

    private static long IntegerSquareRoot(long value)
    {
        long lower = 0;
        long upper = value + 1;
        while (lower + 1 < upper)
        {
            long middle = lower + (upper - lower) / 2;
            if (middle <= value / middle)
                lower = middle;
            else
                upper = middle;
        }
        return lower;
    }
}
