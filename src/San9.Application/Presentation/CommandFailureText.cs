using San9.Content.Text;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

public static class CommandFailureText
{
    public static string Describe(TextCatalog texts, CommandRejection reason) => reason == CommandRejection.None ? "" :
        CommandText.Resolve(texts, reason switch
    {
        CommandRejection.NotStable or CommandRejection.WrongSequence => "failure.stale",
        CommandRejection.NotActiveController => "failure.controller",
        CommandRejection.PersonUnavailable or CommandRejection.UnknownPerson => "failure.officer",
        CommandRejection.InsufficientFunds => "failure.resources",
        CommandRejection.TreasureUnavailable or CommandRejection.UnknownTreasure => "failure.treasure",
        CommandRejection.NoDestination => "failure.destination",
        CommandRejection.InvalidCorps => "failure.corps",
        CommandRejection.ResourceOverflow => "failure.overflow",
        CommandRejection.SettlementConfused => "failure.confused",
        CommandRejection.DuplicateCommandId => "failure.duplicate",
        CommandRejection.DecisionPending => "failure.pending",
        _ => "failure.target"
    });
}
