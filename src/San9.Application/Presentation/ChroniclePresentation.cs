using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Presentation;

public sealed record ChronicleLine(GameClock Clock, string BbCode, int? ForceColorCode);
public sealed record ChronicleDate(string Year, string Month, string Xun);
public sealed record ChroniclePresentation(string RulerName, ChronicleLine[] Lines)
{
    public static ChroniclePresentation Read(GameSession session, TextCatalog texts, San9.Content.Interface.ChroniclePresentationDefinition definition)
    {
        var view = session.View;
        var force = view.World.Forces.Single(f => f.Id == view.PlayerForceId);
        var context = session.Messages(texts);
        string name = ReadName(session, texts);
        var lines = session.History.Where(f => f.ForceId == force.Id)
            .Select(f => Render(f, context, view, texts, definition));
        var notes = session.Chronicle.Where(e => e.ForceId == force.Id).Select(e => new ChronicleLine(new(e.Year, e.Month, e.Day), MessageMarkup.Escape(e.Text), force.ColorCode));
        return new(name, lines.Concat(notes).OrderBy(l => l.Clock.DayNumber).ToArray());
    }

    internal static ChronicleLine Render(ChronicleFact fact, WorldMessageContext context, StrategySnapshot view,
        TextCatalog texts, San9.Content.Interface.ChroniclePresentationDefinition definition)
    {
        var values = fact.Values.ToDictionary();
        foreach (var (role, key) in fact.TextKeys) values[role] = new(0, texts.Resolve(key));
        if (values.TryGetValue("facility-name", out var facility))
            values["facility-name"] = facility.Entity is not null ? context.Query("settlement.name", [facility])
                : new(0, texts.Resolve(view.FacilityNameSuffixKeys[checked((int)facility.Number)]));
        foreach (string role in new[] { "name", "other-name" })
            if (values.TryGetValue(role, out var value) && value.Entity is { } entity)
                values[role] = context.Query(entity.Kind switch { MessageEntityKind.Person => "person.name", MessageEntityKind.Force => "force.name", _ => "settlement.name" }, [value]);
        var message = ProgressMessageProducer.Produce(definition.Messages[fact.Role], context.AtOccurrence(fact.Clock), texts, values, _ => 0).Message;
        return new(fact.Clock, message.BbCode, fact.ForceColorCode);
    }

    public static string Hint(GameSession session, TextCatalog texts, San9.Content.Interface.ChroniclePresentationDefinition definition, string role)
    {
        string name = ReadName(session, texts);
        return ProgressMessageProducer.Produce(definition.Hints[role], session.Messages(texts), texts,
            new Dictionary<string, MessageBinding> { ["name"] = new(0, name) }, _ => 0).Message.BbCode;
    }

    private static string ReadName(GameSession session, TextCatalog texts)
    {
        var force = session.Snapshot.Forces.Single(f => f.Id == session.View.PlayerForceId);
        return force.CountryTitleId is int title ? texts.Resolve(session.View.Information.CountryTitles.Single(t => t.Id == title).NameKey)
            : force.LeaderId is int leader ? session.Messages(texts).Query("person.name", [MessageBinding.Reference(MessageEntityKind.Person, leader)]).Text!
            : texts.Resolve(session.View.Forces.Single(f => f.Id == force.Id).NameKey);
    }

    public ChronicleDate DateAt(int index)
    {
        var clock = Lines[index].Clock;
        GameClock? previous = index == 0 ? null : Lines[index - 1].Clock;
        bool yearChanged = previous?.Year != clock.Year;
        bool monthChanged = yearChanged || previous?.Month != clock.Month;
        bool xunChanged = monthChanged || previous?.Xun != clock.Xun;
        return new(yearChanged ? $"{clock.Year}年" : "", monthChanged ? $"{clock.Month}月" : "",
            xunChanged ? clock.Xun switch { XunIndex.Early => "上旬", XunIndex.Middle => "中旬", XunIndex.Late => "下旬", _ => throw new InvalidOperationException() } : "");
    }
}
