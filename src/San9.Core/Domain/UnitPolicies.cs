namespace San9.Core.Domain;

public sealed record UnitPolicies(
    [property: System.Text.Json.Serialization.JsonRequired] bool AttackApproachingEnemies = false,
    [property: System.Text.Json.Serialization.JsonRequired] bool Pursue = false,
    [property: System.Text.Json.Serialization.JsonRequired] bool AttackAfterAction = false,
    [property: System.Text.Json.Serialization.JsonRequired] bool AutonomousRetreat = false);

public static class UnitPolicyDecisions
{
    public static bool Attack(bool permitted, int personality, long strengthRatio) => permitted
        ? personality < 2 || strengthRatio + (personality == 2 ? 25 : 0) > 100
        : personality < 2 && strengthRatio - (personality == 1 ? 25 : 0) >= 100;

    public static bool Retreat(bool permitted, int personality, int morale, uint soldiers, long enemySoldiers) => permitted
        ? morale < 4 * personality + 25 || soldiers < 300 * personality + 2000 && enemySoldiers > soldiers
        : personality >= 2 && (morale < 4 * (personality - 2) + 25 ||
            soldiers < 500 * (personality + 1) && enemySoldiers > soldiers);
}
