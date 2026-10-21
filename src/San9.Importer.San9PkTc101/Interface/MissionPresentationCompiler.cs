using San9.Content.Interface;

namespace San9.Importer.San9PkTc101.Interface;

/// <summary>Original submission and adviser speech bindings, verified in RE-0182.</summary>
internal static class MissionPresentationCompiler
{
    internal static Dictionary<string, DecisionMessageDefinition> Compile()
    {
        Dictionary<string, DecisionMessageDefinition> messages = [];
        void Add(string role,int source,string? speaker,params (string Slot,string Role)[] bindings) =>
            messages.Add(role,new($"messages.{source}",bindings.ToDictionary(p => p.Slot,p => p.Role),speaker));
        (string,string)[] pair = [("person.0","actor"),("person.1","ruler")];
        foreach (var (role,source) in new[] { ("false-report",5681),("disorder",5723),("inspire",5711),("rescue",5717) })
            Add(role+"-start",source,"actor",[.. pair,("person.2","target-person")]);
        Add("alienate-start",5693,"actor",[.. pair,("person.2","target-person"),("person.3","target-ruler"),("building.0","place")]);
        Add("burn-start",5705,"actor",[.. pair,("building.0","place"),("force.0","target-force")]);
        Add("seize-start",5699,"actor",[.. pair,("building.0","place"),("force.0","target-force")]);
        Add("rumor-start",5687,"actor",[.. pair,("force.0","target-force"),("force.1","second-force")]);
        Add("gift-start",5741,"actor",[.. pair,("force.0","target-force")]);
        Add("request-start",5735,"actor",[.. pair,("force.0","target-force"),("building.0","place")]);
        Add("return-start",5729,"actor",pair);
        Add("surrender-start",5747,"actor",[.. pair,("force.0","target-force")]);
        (string,string)[] advice = [("person.0","adviser"),("person.1","ruler")];
        Add("diplomacy-unpredictable",7321,"adviser",advice);
        Add("strategy-favorable",7313,"adviser",advice);
        Add("strategy-unfavorable",7317,"adviser",advice);
        Add("gift-favorable",7353,"adviser",advice);
        Add("gift-unfavorable",7357,"adviser",advice);
        Add("request-favorable",7341,"adviser",[.. advice,("force.0","target-force")]);
        Add("request-uncertain",7325,"adviser",advice);
        Add("request-unfavorable",7345,"adviser",[.. advice,("force.0","target-force")]);
        Add("return-favorable",7325,"adviser",advice);
        Add("return-unfavorable",7349,"adviser",[.. advice,("force.0","target-force")]);
        Add("surrender-favorable",7369,"adviser",advice);
        Add("surrender-unfavorable",7373,"adviser",[.. advice,("force.0","target-force")]);
        (string,string)[] resultPair = [("person.0","actor"),("person.1","target-person")];
        Add("alienate-success",10036,null,resultPair); Add("alienate-failure",9951,null,resultPair);
        Add("burn-success",9944,null,("person.0","actor"));
        Add("burn-failure",9948,null,("person.0","actor"),("building.0","place"));
        Add("seize-person",9932,null,resultPair);
        Add("seize-item", 9929, null, ("person.0", "actor"), ("person.1", "previous-holder"), ("treasure.0", "treasure"));
        Add("seize-failure",9937,null,("person.0","actor"),("building.0","place"));
        Add("rumor-success",9940,null,("person.0","actor"),("force.0","target-force"));
        Add("rumor-failure",9942,null,("person.0","actor"),("force.0","target-force"));
        Add("false-report-success",9923,null,resultPair);
        Add("false-report-failure",9926,null,[.. resultPair,("force.0","target-force")]);
        Add("disorder-success",9963,null,[.. resultPair,("force.0","target-force")]);
        Add("disorder-failure",9966,null,[.. resultPair,("force.0","target-force")]);
        Add("inspire-success",9953,null,("person.0","target-person"),("person.1","actor"));
        Add("inspire-failure",9956,null,resultPair);
        Add("rescue-success",9958,null,("person.0","target-person"),("person.1","actor"));
        Add("rescue-failure",9961,null,("person.0","target-person"));
        Add("gift-success",6594,null,("force.0","force"),("force.1","target-force"));
        Add("gift-failure",6595,null,("force.0","force"),("force.1","target-force"));
        Add("request-success",6486,null,("force.0","target-force"),("building.0","place"));
        Add("request-failure",6488,null,("force.0","target-force"),("building.0","place"));
        Add("return-success",6375,null,("person.0","target-person"));
        Add("return-failure",6558,null,("person.0","target-ruler"),("person.1","target-person"));
        Add("surrender-success",6640,null,("force.0","force"),("force.1","target-force"));
        Add("surrender-failure",6641,null,("force.0","force"),("force.1","target-force"));
        foreach (string role in messages.Keys.Where(k => k.EndsWith("-start", StringComparison.Ordinal)).ToArray())
            messages[role] = messages[role] with
            {
                Sound = role switch
            {
                "seize-start" => "seize-departure",
                "rumor-start" or "gift-start" or "request-start" or "return-start" or "surrender-start" => "mission-departure",
                _ => "strategy-departure"
                }
            };
        foreach (string role in new[] { "gift", "request", "return", "surrender" })
        {
            messages[role + "-success"] = messages[role + "-success"] with { Sound = "decision-success" };
            messages[role + "-failure"] = messages[role + "-failure"] with { Sound = "decision-failure" };
        }
        Add("corps-created",5551,null,("force.0","corps"));
        Add("corps-changed",5550,null,("force.0","corps"));
        Add("diplomacy-arrival",7112,null,("person.0","ruler"),("person.1","actor"),("person.2","target-ruler"));
        messages["diplomacy-arrival"] = messages["diplomacy-arrival"] with { Sound = "decision-announcement" };
        Add("diplomacy-greeting",6352,"actor",("person.0","actor"),("person.1","target-ruler"),("person.2","ruler"));
        Add("diplomacy-welcome",6361,"target-ruler",("person.0","target-ruler"),("person.1","actor"),("force.0","target-force"),("force.1","force"));
        Add("gift-proposal",6559,"actor",("person.0","actor"),("person.1","target-ruler"),("person.2","ruler"));
        Add("surrender-proposal",6596,"actor",("person.0","actor"),("person.1","target-ruler"),("person.2","ruler"));
        Add("request-proposal",6402,"actor",("person.0","actor"),("person.1","target-ruler"),("force.0","enemy-force"),("building.0","place"));
        Add("request-unowned-proposal",6411,"actor",("person.0","actor"),("person.1","target-ruler"),("building.0","place"));
        Add("return-proposal",6489,"actor",("person.0","actor"),("person.1","target-ruler"),("person.2","target-person"));
        Add("diplomacy-host-adviser",7329,"target-adviser",("person.0","target-adviser"),("person.1","target-ruler"));
        foreach (var (role,yes,no) in new[] { ("gift",7361,7365),("surrender",7377,7381) })
        {
            Add(role+"-host-favorable",yes,"target-adviser",("person.0","target-adviser"),("person.1","target-ruler"));
            Add(role+"-host-unfavorable",no,"target-adviser",("person.0","target-adviser"),("person.1","target-ruler"));
        }
        foreach (var (role,yes,no) in new[] { ("gift",6568,6581),("surrender",6605,6618) })
        {
            foreach (var (suffix,key) in new[] { ("accepted",yes),("refused",no) })
                Add(role+"-"+suffix,key,"target-ruler",("person.0","target-ruler"),("person.1","actor"),("force.0","target-force"),("force.1","force"));
        }
        Add("surrender-actor-accepted",6631,"actor",("person.0","actor"),("person.1","target-ruler"));
        Add("surrender-actor-refused",6635,"actor",("person.0","actor"),("person.1","target-ruler"));
        Add("surrender-eliminated",7194,null,("force.0","target-force"),("number.0","year"),("number.1","month"));
        messages["surrender-eliminated"] = messages["surrender-eliminated"] with { Illustration = "diplomacy-eliminated" };
        foreach (var (role,key) in new[] { ("false-report",10546),("disorder",10582),("inspire",10609),("rescue",10622) })
            Add(role+"-action",key,"actor",resultPair);
        Add("false-report-reaction-success",10555,"target-person",("person.0","target-person"),("building.0","home"));
        Add("false-report-reaction-failure",10564,"target-person",("person.0","target-person"),("person.1","actor"));
        Add("strategy-reaction-united",10745,"target-person",("person.0","target-person"),("person.1","actor"));
        Add("strategy-reaction-countered",10754,"target-person",("person.0","target-person"),("person.1","actor"));
        Add("false-report-reaction-defensive",10573,"target-person",("person.0","target-person"),("person.1","actor"));
        Add("disorder-reaction-defensive",10736,"target-person",("person.0","target-person"),("person.1","actor"));
        Add("disorder-reaction-success",10591,"target-person",("person.0","target-person"));
        Add("disorder-reaction-failure",10600,"target-person",("person.0","target-person"),("person.1","actor"));
        Add("inspire-reaction-success",10618,"soldier",("person.0","soldier"),("person.1","target-person"));
        Add("inspire-reaction-failure",10621,"soldier",("person.0","soldier"),("person.1","actor"));
        Add("rescue-reaction-success",10631,"soldier",("person.0","soldier"),("person.1","actor"));
        Add("rescue-reaction-failure",10632,"soldier",("person.0","soldier"),("person.1","actor"));
        Add("burn-action",10633,"actor",("person.0","actor"),("building.0","place"));
        Add("burn-reaction-success",10642,"defender",("person.0","defender"),("building.0","place"));
        Add("burn-reaction-failure",10651,"defender",("person.0","defender"),("person.1","actor"),("building.0","place"),("force.0","force"));
        Add("seize-person-action",10684,"actor",[.. resultPair,("building.0","place")]);
        Add("seize-item-action", 10660, "actor", ("person.0", "actor"), ("person.1", "previous-holder"), ("treasure.0", "treasure"));
        Add("seize-item-reaction", 10669, "previous-holder", ("person.0", "previous-holder"), ("treasure.0", "treasure"));
        Add("seize-person-reaction",10693,"defender",("person.0","defender"),("person.1","target-person"),("force.0","force"));
        Add("seize-action",10702,"actor",("person.0","actor"),("building.0","place"));
        Add("seize-reaction-success",10711,"defender",("person.0","defender"));
        Add("seize-reaction-failure",10720,"defender",("person.0","defender"),("person.1","actor"),("building.0","place"),("force.0","force"));
        return messages;
    }
}
