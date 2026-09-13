using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Importer.San9PkTc101.Scenarios;

public static class DiplomacyContentCompiler
{
    public static AllianceState? Alliance(int remainingXun, IReadOnlyList<San9ScenarioForceRecord> forces,
        IReadOnlySet<int> supportedMainForces)
    {
        var marked = forces.Where(f => f.IsAllianceMember || f.IsAllianceLeader || f.IsAllianceTarget).ToArray();
        if (remainingXun == 0 && marked.Length == 0) return null;
        if (remainingXun <= 0 || marked.Any(f => !supportedMainForces.Contains(f.Id) || f.RuntimeOwningForceId != f.Id) ||
            marked.Count(f => f.IsAllianceTarget) != 1 || marked.Count(f => f.IsAllianceLeader) != 1 ||
            marked.Any(f => f.IsAllianceLeader && !f.IsAllianceMember || f.IsAllianceTarget && f.IsAllianceMember))
            throw new InvalidDataException("Source alliance roles or duration cannot be represented consistently.");
        var alliance = new AllianceState(marked.Single(f => f.IsAllianceTarget).Id, marked.Single(f => f.IsAllianceLeader).Id,
            marked.Where(f => f.IsAllianceMember).Select(f => f.Id).Order().ToImmutableArray(), remainingXun);
        try { alliance.Validate(supportedMainForces); }
        catch (ArgumentException error) { throw new InvalidDataException("Invalid source alliance.", error); }
        return alliance;
    }
}
