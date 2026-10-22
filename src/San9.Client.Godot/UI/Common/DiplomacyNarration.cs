using San9.Application.Presentation;

namespace San9.Client.Godot;

internal static class DiplomacyNarration
{
    public static IReadOnlyList<CommandMessage> Filter(IReadOnlyList<CommandMessage> messages, bool enabled) =>
        enabled ? messages : messages.Where(message => !message.DiplomacyDialogue).ToArray();
}
