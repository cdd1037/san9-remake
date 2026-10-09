using San9.Content.Interface;
using San9.Content.Text;

namespace San9.Content.Tests;

public sealed class CommandPresentationTests
{
    [Fact]
    public void DemolitionRejectsMissingMessageArgumentsAndResultSound()
    {
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, []) { Messages = new()
        {
            ["confirm"] = new([new MessageValue(new("binding", "building.0"))]),
            ["dialogue"] = new([new MessageValue(new("binding", "person.0"))]),
            ["result"] = new([new MessageValue(new("binding", "force.0"))])
        } };
        var content = CommandPresentation.Empty with
        {
            Demolition = new("confirm", "dialogue", "result", "removed"),
            Sounds = new() { ["removed"] = "removed.wav" }
        };
        content.Validate(texts, [], []);
        Assert.Throws<InvalidDataException>(() => (content with { Sounds = [] }).Validate(texts, [], []));
        Assert.Throws<InvalidDataException>(() => (content with
            { Demolition = content.Demolition with { Confirmation = "missing" } }).Validate(texts, [], []));
        texts.Messages["confirm"] = new([new MessageValue(new("binding", "building.1"))]);
        Assert.Throws<InvalidDataException>(() => content.Validate(texts, [], []));
    }

    [Fact]
    public void MilitaryMessagesRejectIncompleteBranchesBindingsAndSounds()
    {
        string[] roles = ["sortie", "reinforce", "build", "transport", "transport-redirect", "attack", "march", "retreat", "adjust"];
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, []) { Messages = new()
        {
            ["order"] = new([new MessageValue(new("binding", "person.0"))])
        } };
        var content = CommandPresentation.Empty with
        {
            Military = roles.ToDictionary(r => r, _ => new DecisionMessageDefinition("order",
                new() { ["person.0"] = "commander" }, "commander", "order")),
            Sounds = new() { ["order"] = "order.wav" }
        };
        content.Validate(texts, [], []);
        Assert.Throws<InvalidDataException>(() => (content with { Sounds = [] }).Validate(texts, [], []));
        Assert.Throws<InvalidDataException>(() => (content with
            { Military = content.Military.Where(m => m.Key != "adjust").ToDictionary() }).Validate(texts, [], []));
        var invalid = new Dictionary<string, DecisionMessageDefinition>(content.Military)
            { ["attack"] = content.Military["attack"] with { Bindings = new() { ["person.0"] = "enemy" } } };
        Assert.Throws<InvalidDataException>(() => (content with { Military = invalid }).Validate(texts, [], []));
        invalid["attack"] = content.Military["attack"] with { Bindings = [] };
        Assert.Throws<InvalidDataException>(() => (content with { Military = invalid }).Validate(texts, [], []));
    }

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
