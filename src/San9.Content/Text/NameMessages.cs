namespace San9.Content.Text;

public static class NameMessages
{
    public const string City = "presentation.city-name";
    public const string Indented = "presentation.indented-name";

    public static void AddTo(IDictionary<string, MessageDefinition> messages)
    {
        messages.Add(City, Define("[img]icons/city[/img]"));
        messages.Add(Indented, Define("　"));
    }

    private static MessageDefinition Define(string prefix) => new(
        [new MessageLiteral(prefix), new MessageValue(new("binding", "name"))]);
}
