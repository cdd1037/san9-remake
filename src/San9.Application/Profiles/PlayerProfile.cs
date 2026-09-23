using System.Collections.Immutable;

namespace San9.Application.Profiles;

/// <summary>Cross-session completion facts, independent of preferences and world state.</summary>
public sealed class PlayerProfile
{
    public static PlayerProfile Empty { get; } = new(Array.Empty<string>());

    public ImmutableHashSet<string> CompletedMilestones { get; }

    public PlayerProfile(IEnumerable<string> completedMilestones)
    {
        ArgumentNullException.ThrowIfNull(completedMilestones);
        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (string id in completedMilestones)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            builder.Add(id);
        }
        CompletedMilestones = builder.ToImmutable();
    }

    private PlayerProfile(ImmutableHashSet<string> completedMilestones)
        => CompletedMilestones = completedMilestones;

    public PlayerProfile Complete(string milestoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(milestoneId);
        var updated = CompletedMilestones.Add(milestoneId);
        return ReferenceEquals(updated, CompletedMilestones) ? this : new PlayerProfile(updated);
    }
}
