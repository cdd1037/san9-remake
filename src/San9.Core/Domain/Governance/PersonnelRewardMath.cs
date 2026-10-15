namespace San9.Core.Domain;

/// <summary>Shared deterministic reward effect; eligibility and payment belong to simulation.</summary>
public static class PersonnelRewardMath
{
    public static int Gain(int loyalty, int righteousness) =>
        Math.Min(Math.Min(50 * (20 - righteousness) / 100, 10), 100 - loyalty);
}
