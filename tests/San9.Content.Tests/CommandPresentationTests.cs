using San9.Content.Interface;
using San9.Content.Text;

namespace San9.Content.Tests;

public sealed class CommandPresentationTests
{
    [Fact]
    public void DecisionSharedMessagesRequireBindingsAndSoundReferences()
    {
        var texts=new TextCatalog(TextCatalog.CurrentSchemaVersion,[]) { Messages=new() {
            ["reply"]=new([new MessageValue(new("binding","person.0"))]) } };
        var definitions=new DecisionPresentations([],new(){["citizen"]=9001}) {
            Shared=new(){["custom-reply"]=new("reply",new(){["person.0"]="actor"},Sound:"reply") } };
        definitions.Validate(texts,new Dictionary<string,string>{{"reply","reply.wav"}},[9001]);
        Assert.Throws<InvalidDataException>(()=>definitions.Validate(texts,new Dictionary<string,string>(),[9001]));
        Assert.Throws<InvalidDataException>(()=>(definitions with { Shared=new(){["custom-reply"]=new("reply",[])} }).Validate(texts,new Dictionary<string,string>(),[9001]));
    }
    [Fact]
    public void FacilityBindingsRequireMessagesNpcSoundsAndRepairCompletion()
    {
        string[] roles = ["normal","cooperative","citizen","pair-first","pair-second","result","complete","complete-dialogue"];
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion,[]) { Messages = roles.ToDictionary(k => k,k => new MessageDefinition([new MessageLiteral(k)])) };
        var content = CommandPresentation.Empty with {
            Sounds = new() { ["facility-result"] = "result.wav", ["patrol-citizen"] = "citizen.wav", ["repair-complete"] = "complete.wav" },
            Facilities = new() { ["repair"] = new(roles.ToDictionary(k => k),9) } };
        content.Validate(texts,[9],[]);
        Assert.Throws<InvalidDataException>(() => content.Validate(texts,[],[]));
        Assert.Throws<InvalidDataException>(() => (content with { Sounds = [] }).Validate(texts,[9],[]));
        Assert.Throws<InvalidDataException>(() => (content with { Facilities = new() {
            ["repair"] = content.Facilities["repair"] with { Messages = roles.Where(k => k != "complete").ToDictionary(k => k) } } }).Validate(texts,[9],[]));
    }

    [Fact]
    public void NoticesResolveAuthoredKeysEvenWithoutPatrol()
    {
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, [])
        { Messages = new() { ["custom.notice"] = new([new MessageLiteral("Notice")]) } };
        var content = CommandPresentation.Empty with { Notices = new() { ["scroll-hover"] = "custom.notice" } };
        content.Validate(texts, [], []);
        Assert.Throws<InvalidDataException>(() => content.Validate(new(TextCatalog.CurrentSchemaVersion, []), [], []));
    }

    [Fact]
    public void AuthoredBindingsAcceptCustomIdentitiesAndRejectDanglingReferences()
    {
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, [])
        { Messages = new() { ["custom.patrol"] = new([new MessageLiteral("Custom dialogue")]) } };
        var roles = new[] { "normal", "cooperative", "citizen", "pair-first", "pair-second", "result" }
            .ToDictionary(role => role, _ => "custom.patrol");
        var patrol = new PatrolPresentationDefinition(roles, 9001,
            [new("custom.patrol", 9001, [8001], AdviceMetric.PublicOrder, 500)]);
        var content = new CommandPresentation(new() { ["patrol-citizen"] = "audio/citizen.wav", ["domestic-result"] = "audio/result.wav" }, patrol);
        content.Validate(texts, [9001], [8001]);
        Assert.Throws<InvalidDataException>(() => content.Validate(texts, [], [8001]));
        Assert.Throws<InvalidDataException>(() => content.Validate(texts, [9001], []));
        Assert.Throws<InvalidDataException>(() => (content with { Sounds = [] }).Validate(texts, [9001], [8001]));
        Assert.Throws<InvalidDataException>(() => content.Validate(new(TextCatalog.CurrentSchemaVersion, []), [9001], [8001]));
    }
}
