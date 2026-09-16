namespace San9.Core.Simulation;

internal static class TaskCancellation
{
    internal static void Apply(WorldMutation world, PersistentTask task, string reason, List<DomainEvent> events)
    {
        var current = world.Simulation.Tasks.SingleOrDefault(t => t.Id == task.Id);
        if (current is null) return;
        task = current;
        world.SetSimulation(world.Simulation with { Tasks = world.Simulation.Tasks.Remove(task) });
        GiftDiplomacy.Cancel(world, task, events);
        events.Add(new TaskCancelledEvent(task.Id, world.Clock, reason));
    }
}
