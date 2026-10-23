using San9.Content.Interface;

namespace San9.Importer.San9PkTc101.Interface;

internal static class DecisionPresentationCompiler
{
    internal static DecisionPresentations Compile(IReadOnlyList<San9ScenarioPersonRecord> people)
    {
        int Person(int biography) => people.Take(700).Select((p, id) => (p, id)).Single(p => p.p.BiographyResourceCode == biography).id;
        DecisionMessageDefinition M(int source, string? speaker = null, string? sound = null, string? image = null,
            params (string Slot, string Role)[] inputs)
        {
            // Numeric slots have event-specific meanings; explicit bindings let validation catch omissions.
            Dictionary<string, string> bindings = new()
            {
                ["person.0"] = speaker ?? "actor",
                ["person.1"] = speaker is not null and not "actor" ? "actor" : "ruler",
                ["person.2"] = "ruler",
                ["force.0"] = "force",
                ["force.1"] = "target-force",
                ["building.0"] = "place",
                ["string.0"] = "subject",
                ["tactic.0"] = "tactic",
                ["treasure.0"] = "treasure"
            };
            foreach (var (slot, role) in inputs) bindings[slot] = role;
            return new($"messages.{source}", bindings, speaker, sound, image);
        }
        Dictionary<string, DecisionPresentationDefinition> events = [];
        Dictionary<string, DecisionMessageDefinition> shared = [];
        void Add(string role, DecisionWindowKind kind, DecisionMessageDefinition prompt,
            DecisionMessageDefinition[] introduction, (string Role, string Text)[] choices,
            params (string Role, DecisionMessageDefinition[] Messages)[] results) => events.Add(role,
                new(kind, prompt, introduction, results.ToDictionary(r => r.Role, r => r.Messages), choices.ToDictionary(c => c.Role, c => c.Text)));
        const string good = "decision-success", bad = "decision-failure", increase = "decision-increase", decrease = "decision-decrease";
        shared["learned-tactic"] = M(7232, sound: increase);
        shared["study-completed"] = M(6236, "actor", increase);
        shared["merchant-money-return"] = M(7169, sound: increase, inputs: [("force.1", "account"), ("number.0", "balance"), ("number.1", "profit")]);
        shared["merchant-food-return"] = M(7171, sound: increase, inputs: [("force.1", "account"), ("number.0", "balance"), ("number.1", "profit")]);
        for (int i = 0; i < 3; i++) shared["training-growth-" + i] = M(7992 + i, sound: increase);
        shared["diplomacy-foreign-arrival"] = M(7112, sound: "decision-announcement", inputs: [("person.0", "requester-ruler"), ("person.1", "actor"), ("person.2", "host")]);
        shared["diplomacy-unowned-target"] = M(6411, "actor", inputs: [("person.1", "host")]);
        shared["diplomacy-counteroffer-unpredictable"] = M(7321, "adviser", inputs: [("person.0", "adviser"), ("person.1", "host")]);
        shared["diplomacy-counteroffer-favorable"] = M(7333, "adviser", inputs: [("person.0", "adviser"), ("person.1", "host")]);
        shared["diplomacy-counteroffer-unfavorable"] = M(7337, "adviser", inputs: [("person.0", "adviser"), ("person.1", "host")]);
        shared["diplomacy-adviser"] = M(7329, "adviser", inputs: [("person.0", "adviser"), ("person.1", "host")]);
        shared["diplomacy-exchanged-prisoner"] = M(6375, sound: increase, inputs: [("person.0", "exchange-person")]);
        foreach (var (role, message) in DiplomacyConversationCompiler.Compile()) shared.Add(role, message);
        shared["alliance-responsibility"] = M(6070, inputs: [("force.0", "target-force")]);
        shared["alliance-request-conflict"] = M(9418, inputs: [("force.0", "conflict-force"), ("force.1", "conflict-force")]);
        shared["alliance-contact-conflict"] = M(9419);
        shared["alliance-order-conflict"] = M(9421);
        shared["alliance-formed"] = M(9395, inputs: [("force.0", "target-force")]);
        shared["alliance-failed"] = M(9396, inputs: [("force.0", "target-force")]);
        shared["ending-unification"] = M(7574, image: "decision-unification", inputs: [("number.0", "year")]);
        shared["ending-country-title"] = M(6138);
        shared["ending-confirm-country-title"] = M(6139);
        shared["ending-country-founded"] = M(7577, inputs: [("person.0", "ruler")]);
        shared["ending-prepare-undersized"] = M(7578, "ruler", inputs: [("person.0", "ruler")]);
        shared["ending-prepare-offices"] = M(7579, "ruler", inputs: [("person.0", "ruler")]);
        string[] offices = ["chancellor", "marshal", "protector", "successor"];
        for (int i = 0; i < offices.Length; i++) shared["ending-select-" + offices[i]] = M(7580 + i);
        shared["ending-confirm-offices"] = M(7584, inputs: [("person.0", "chancellor"), ("person.1", "marshal"), ("person.2", "protector"), ("person.3", "successor")]);
        shared["ending-appoint-ruler"] = M(7585, "ruler", inputs: [("person.0", "ruler"), ("person.1", "chancellor"), ("person.2", "marshal"), ("person.3", "protector")]);
        shared["ending-appoint-chancellor"] = M(7586, "chancellor", inputs: [("person.0", "chancellor"), ("person.1", "ruler")]);
        shared["ending-appoint-marshal"] = M(7587, "marshal", inputs: [("person.0", "marshal"), ("person.1", "ruler")]);
        shared["ending-appoint-protector"] = M(7588, "protector", inputs: [("person.0", "protector"), ("person.1", "ruler")]);
        shared["ending-appoint-successor"] = M(7589, "ruler", inputs: [("person.0", "ruler"), ("person.1", "successor")]);
        shared["ending-successor-reply"] = M(7590, "successor", inputs: [("person.0", "successor"), ("person.1", "ruler")]);
        shared["ending-policy"] = M(7591);
        shared["ending-confirm-expansion"] = M(7592);
        shared["ending-confirm-domestic"] = M(7640);
        shared["ending-introduction"] = M(7593);
        void Chapter(string role, int source, params (string Slot, string Role)[] inputs) =>
            shared["ending-" + role] = M(source, inputs: inputs);
        Chapter("expansion-start", 7594, ("person.0", "ruler"), ("number.0", "year-two"));
        Chapter("expansion-army-ready", 7595, ("person.0", "marshal"), ("person.1", "protector"), ("number.0", "year-three"));
        Chapter("expansion-undersized", 7596);
        Chapter("expansion-soldier-shortage", 7597, ("person.0", "ruler"));
        Chapter("expansion-leadership-shortage", 7598, ("person.0", "marshal"), ("person.1", "protector"), ("person.2", "ruler"));
        Chapter("expansion-west-start", 7599, ("person.0", "ruler"), ("person.1", "marshal"), ("person.2", "protector"), ("number.0", "year-six"));
        Chapter("expansion-west-ready", 7600, ("person.0", "protector"));
        Chapter("expansion-order-shortage", 7601, ("person.0", "protector"));
        Chapter("expansion-war-shortage", 7602, ("person.0", "protector"), ("person.1", "protector"), ("person.2", "protector"), ("number.0", "year-six"));
        Chapter("expansion-trade-start", 7603, ("person.0", "chancellor"), ("number.0", "year-six"));
        Chapter("expansion-trade-ready", 7604);
        Chapter("expansion-commerce-shortage", 7605);
        Chapter("expansion-naval-shortage", 7606);
        Chapter("expansion-final-start", 7607, ("person.0", "ruler"), ("number.0", "year-ten"));
        Chapter("expansion-empire", 7608, ("person.0", "ruler"));
        Chapter("expansion-barbarians", 7609, ("person.0", "ruler"));
        Chapter("expansion-tactic-shortage", 7610, ("person.0", "ruler"));
        Chapter("domestic-start", 7611, ("person.0", "ruler"));
        Chapter("domestic-building-ready", 7612, ("person.0", "chancellor"));
        Chapter("domestic-undersized", 7613);
        Chapter("domestic-durability-shortage", 7614, ("person.0", "ruler"));
        Chapter("domestic-knowledge-shortage", 7615);
        Chapter("domestic-government-start", 7616, ("person.0", "ruler"));
        Chapter("domestic-government-ready", 7617);
        Chapter("domestic-trust-shortage", 7618);
        Chapter("domestic-affinity-conflict", 7619, ("person.0", "ruler"), ("person.1", "chancellor"));
        Chapter("domestic-defense-start", 7620, ("number.0", "year-five"));
        Chapter("domestic-defense-ready", 7621, ("person.0", "chancellor"), ("person.1", "marshal"), ("person.2", "protector"));
        Chapter("domestic-resource-shortage", 7622, ("person.0", "ruler"));
        Chapter("domestic-leadership-shortage", 7623, ("person.0", "ruler"), ("person.1", "marshal"), ("person.2", "protector"));
        Chapter("domestic-culture-start", 7624, ("person.0", "chancellor"));
        Chapter("domestic-culture-ready", 7625);
        Chapter("domestic-order-shortage", 7626);
        Chapter("domestic-treasure-shortage", 7627, ("person.0", "ruler"));
        shared["ending-succession-year"] = M(7628, inputs: [("number.0", "succession-year")]);
        shared["ending-stable-succession"] = M(7629, inputs: [("person.0", "ruler"), ("person.1", "successor"), ("person.2", "chancellor")]);
        shared["ending-unstable-succession"] = M(7630, inputs: [("person.0", "ruler"), ("person.1", "chancellor")]);
        shared["ending-successor-conflict"] = M(7631, inputs: [("person.0", "ruler"), ("person.1", "successor"), ("person.2", "chancellor")]);
        string[] outcomes = ["great-empire", "commercial-power", "cultural-power", "city-state", "ordinary-state", "southern-dynasty", "divided-state", "fallen-state"];
        for (int i = 0; i < outcomes.Length; i++) shared["ending-result-" + outcomes[i]] = M(7632 + i, inputs: [("person.0", "ruler"), ("person.1", "successor"), ("person.2", "chancellor"), ("number.0", "fallen-year")]);
        var yesNo = new (string, string)[] { ("accept", "是"), ("decline", "否") };
        var allowNo = new (string, string)[] { ("accept", "允诺"), ("decline", "拒绝") };
        Add("country-title", DecisionWindowKind.CountrySelection, M(6138), [M(6138)], [("choose", "国号")]);
        events["country-title"] = events["country-title"] with { SelectionConfirmation = M(6139) };
        Add("emperor-transfer", DecisionWindowKind.VerticalChoice, M(9326, inputs: [("person.0", "other")]), [], [("accept", "拥立"), ("decline", "废黜")],
            ("accept", [M(9327, "other", inputs: [("person.0", "other")]), M(9328, sound: "decision-announcement"), M(9324, sound: increase)]),
            ("decline", [M(9330, "other", inputs: [("person.0", "other"), ("number.0", "dynasty-years")]), M(9331, sound: "decision-announcement")]));
        Add("office-request", DecisionWindowKind.PersonDisposition, M(6147), [M(6137, sound: "decision-announcement", image: "actor"), M(6188, "actor")], allowNo,
            ("accept", [M(6200, "ruler", inputs: [("person.0", "ruler"), ("person.1", "actor")]), M(6148, "actor"), M(6174, sound: increase)]),
            ("decline", [M(6211, "ruler", inputs: [("person.0", "ruler"), ("person.1", "actor")]), M(6160, "actor")]));
        Add("search-recruitment", DecisionWindowKind.PersonDisposition, M(7449), [M(6340, sound: good, image: "other", inputs: [("person.1", "other")])], [("accept", "登用"), ("decline", "静观")],
            ("success", [M(6261, "actor", inputs: [("person.1", "other"), ("person.2", "ruler")]), M(6302, "other", inputs: [("person.0", "other"), ("person.1", "actor")]), M(6341, sound: increase, inputs: [("person.0", "other")])]),
            ("failure", [M(6318, "other", inputs: [("person.0", "other"), ("person.1", "actor")]), M(6342, sound: decrease, inputs: [("person.0", "other")])]));
        Add("shanyang-gong", DecisionWindowKind.VerticalChoice, M(9360), [], [("accept", "保护"), ("decline", "处斩")],
            ("accept", [M(9361, sound: "decision-announcement"), M(9324, sound: increase)]), ("decline", [M(9363, sound: "execution-result")]));
        shared["shanyang-movie-introduction"] = M(7529);
        events["shanyang-title"] = events["country-title"] with { Results = new() { ["choose"] = events["shanyang-gong"].Results["accept"] } };
        Add("mystic-book-offer", DecisionWindowKind.VerticalChoice, M(8008, inputs: [("number.0", "cost")]), [M(7892), M(7893, "other", inputs: [("person.0", "other"), ("number.0", "cost")])], allowNo,
            ("accept", [M(7894, "other", inputs: [("person.0", "other")]), M(7895, sound: increase, inputs: [("number.0", "cost")])]),
            ("decline", [M(7896, "other", inputs: [("person.0", "other")]), M(7897, sound: decrease, inputs: [("number.0", "cost")])]));
        Add("mystic-book-return", DecisionWindowKind.VerticalChoice, M(8009), [M(7898), M(7899, "other", inputs: [("person.0", "other")])], allowNo,
            ("accept", [M(7900, "other", inputs: [("person.0", "other")]), M(7901, sound: decrease)]), ("decline", [M(7902, "other", inputs: [("person.0", "other")])]));
        Add("scholar-insult", DecisionWindowKind.VerticalChoice, M(8010), [M(7903), M(7904, "other", inputs: [("person.0", "other"), ("person.1", "actor")])], [("accept", "无视"), ("decline", "处斩")],
            ("accept", [M(7905, "other", inputs: [("person.0", "other")]), M(7857, sound: increase)]), ("decline", [M(7906, "other", inputs: [("person.0", "other")]), M(7858, sound: decrease)]));
        Add("longevity", DecisionWindowKind.Confirmation, M(7881, inputs: [("number.0", "cost")]), [M(7878, image: "decision-dippers"), M(7879, "north-dipper"), M(7880, "south-dipper")], yesNo,
            ("accept", [M(7882, "south-dipper"), M(7883, "north-dipper"), M(7175, sound: increase)]), ("decline", [M(7884, "north-dipper"), M(7885, sound: decrease)]));
        Add("treasure-monument", DecisionWindowKind.Confirmation, M(7978, inputs: [("number.0", "cost")]), [M(7977)], yesNo,
            ("accept", [M(7979, sound: good, image: "decision-tongque"), M(7856), M(7857, sound: increase)]), ("decline", [M(7980)]));
        Add("scholar-return", DecisionWindowKind.Confirmation, M(7918, inputs: [("number.0", "cost")]), [M(7917)], yesNo,
            ("accept", [M(7919, sound: good, inputs: [("number.0", "cost")]), M(7920, "other", inputs: [("person.0", "actor"), ("person.1", "other")]), M(7857, sound: increase)]), ("decline", [M(7921, sound: bad)]));
        Add("merchant-investment", DecisionWindowKind.VerticalChoice, M(8023), [M(7996, image: "trade-image"), M(7997, "citizen", inputs: [("person.0", "citizen"), ("person.1", "actor")])], [("invest", "投资"), ("decline", "拒绝"), ("execute", "处斩")],
            ("success", [M(7999, "citizen", inputs: [("number.0", "cost")]), M(8000), M(8001, "citizen", good)]), ("failure", [M(7999, "citizen", inputs: [("number.0", "cost")]), M(8000), M(8002, "citizen", bad), M(8003, sound: decrease, inputs: [("number.0", "cost")])]),
            ("decline", [M(8004, "citizen"), M(8005)]), ("execute", [M(8006, "citizen"), M(7858), M(8007, sound: decrease)]));
        Add("tiger", DecisionWindowKind.VerticalChoice, M(8022), [M(7968, image: "decision-beast"), M(7969, "citizen")], allowNo,
            ("success", [M(7970, sound: good), M(7847, "citizen"), M(7857, sound: increase)]), ("failure", [M(7971, sound: bad), M(7858, sound: decrease)]), ("decline", [M(7849, "citizen"), M(7858, sound: decrease)]));
        Add("beast", DecisionWindowKind.VerticalChoice, M(7973), [M(7972, image: "decision-beast")], [("accept", "战斗"), ("decline", "逃走")],
            ("success", [M(7974, sound: good), M(7857, sound: increase)]), ("failure", [M(7975, sound: bad)]), ("decline", [M(7976, sound: bad)]));
        Add("tactic-study", DecisionWindowKind.PersonSelection, M(6227), [], [("choose", "武将"), ("decline", "放弃")], ("accept", [M(6230, "actor", "decision-study")]));
        events["tactic-study"] = events["tactic-study"] with { CancelConfirmation = M(6229) };
        Add("succession", DecisionWindowKind.PersonSelection, M(7183), [M(7183)], [("choose", "后继者"), ("decline", "自动选择")]);
        Add("captured-ruler", DecisionWindowKind.PersonDisposition, M(7208), [M(7207, inputs: [("person.0", "ruler"), ("person.1", "other")])], [("accept", "解放"), ("decline", "处斩")]);
        foreach (string role in new[] { "attack-request", "prisoner-return" })
            Add(role, DecisionWindowKind.Diplomacy, M(6102), [], [("accept", "允诺"), ("decline", "拒绝"), ("prisoner", "俘虏"), ("money", "资金"), ("treasure", "宝物"), ("free", "无")]);
        foreach (string role in new[] { "attack-request", "prisoner-return" })
            events[role] = events[role] with
            {
                Introduction = [
                M(6351,sound:"decision-announcement",inputs:[("person.0","host"),("person.1","requester-ruler")]),
                M(6352,"actor",inputs:[("person.1","host"),("person.2","requester-ruler")]),
                M(6361,"host",inputs:[("person.0","host"),("person.1","actor"),("force.0","target-force"),("force.1","requester-force")]),
                role=="attack-request" ? M(6402,"actor",inputs:[("person.1","host"),("force.0","enemy-force")]) : M(6489,"actor",inputs:[("person.1","host"),("person.2","other")])],
                Results = role == "attack-request" ? new() { ["success"] = [M(6486, sound: good)], ["failure"] = [M(6488, sound: bad)] } :
                    new() { ["success"] = [M(6375, sound: good, inputs: [("person.0", "other")])], ["failure"] = [M(6558, sound: bad, inputs: [("person.0", "host"), ("person.1", "other")])] }
            };
        Add("alliance", DecisionWindowKind.Confirmation, M(9390), [], yesNo, ("decline", [M(9394)]));
        foreach (var (role, prompt, proposal, accepted, refused) in new[]
            { ("gift", 6098, 6559, 6568, 6581), ("surrender", 6101, 6596, 6605, 6618) })
        {
            Add(role, DecisionWindowKind.Diplomacy, M(prompt),
                [M(6351, sound: "decision-announcement", inputs: [("person.0", "host"), ("person.1", "requester-ruler")]),
                 M(6352, "actor", inputs: [("person.1", "host"), ("person.2", "requester-ruler")]),
                 M(6361, "host", inputs: [("person.0", "host"), ("person.1", "actor"), ("force.0", "target-force"), ("force.1", "requester-force")]),
                 M(proposal, "actor", inputs: [("person.0", "actor"), ("person.1", "host"), ("person.2", "requester-ruler")])],
                [("accept", "允诺"), ("decline", "拒绝")],
                ("accept", [M(accepted, "host", inputs: [("person.0", "host"), ("person.1", "actor"), ("force.0", "target-force"), ("force.1", "requester-force")]),
                    role == "gift" ? shared["gift-actor-accepted"] : M(6631, "actor", inputs: [("person.0", "actor"), ("person.1", "host")])]),
                ("decline", [M(refused, "host", inputs: [("person.0", "host"), ("person.1", "actor"), ("force.0", "target-force"), ("force.1", "requester-force")]),
                    role == "gift" ? shared["gift-actor-refused"] : M(6635, "actor", inputs: [("person.0", "actor"), ("person.1", "host")])]));
        }
        shared["alliance-formation-image"] = M(9391, image: "decision-alliance", inputs: [("force.0", "leader-force")]);
        shared["alliance-leader-declaration"] = M(9392, "other");
        shared["alliance-leader-trust"] = M(9393, inputs: [("force.0", "leader-force")]);
        foreach (var (role, prompt, intro, reply) in new[] {
            ("disaster-relief",8012,7922,7923), ("volunteers",8013,7846,7925), ("bribe",8015,7934,7935),
            ("travel-permit",8016,7939,7940), ("festival",8017,7944,7945),
            ("monument",7982,7981,0), ("homecoming-sacrifice",7986,7985,0), ("training-site",7990,7989,0) })
        {
            var kind = role is "monument" or "homecoming-sacrifice" or "training-site" ? DecisionWindowKind.Confirmation : DecisionWindowKind.VerticalChoice;
            (string Slot, string Role)[] promptInputs = role switch
            {
                "volunteers" => [("number.0", "soldiers")],
                "bribe" => [("number.0", "amount")],
                "monument" or "homecoming-sacrifice" or "training-site" => [("number.0", "cost")],
                _ => []
            };
            Add(role, kind, M(prompt, inputs: promptInputs), reply == 0 ? [M(intro)] : [M(intro, image: role == "festival" ? "decision-festival" : null), M(reply, "citizen", inputs: role == "bribe"
                ? [("person.0", "citizen"), ("person.1", "actor"), ("number.0", "amount")]
                : [("person.0", "citizen"), ("person.1", "actor")])],
                [("Accept", "允诺"), ("DonateMoney", "施与金钱 {cost}"), ("DonateFood", "施与兵粮 {cost}"), ("DemandMoney", "要求金钱"), ("Decline", "拒绝"), ("Execute", "处斩")]);
        }
        DecisionMessageDefinition Citizen(int source) => M(source, "citizen");
        DecisionMessageDefinition Up() => M(7857, sound: increase);
        DecisionMessageDefinition Down() => M(7858, sound: decrease);
        void Results(string role, params (string Choice, DecisionMessageDefinition[] Messages)[] results) =>
            events[role] = events[role] with { Results = results.ToDictionary(r => r.Choice, r => r.Messages) };
        Results("disaster-relief", ("DonateMoney", [Citizen(7847), Up()]), ("DonateFood", [Citizen(7847), Up()]), ("Decline", [Citizen(7924), Down()]));
        Results("volunteers", ("Accept", [Citizen(7927), M(7926, sound: increase, inputs: [("number.0", "soldiers")])]), ("Decline", [Citizen(7928), M(7929)]));
        events["volunteers"] = events["volunteers"] with { Introduction = [M(7846), M(7925, "citizen", inputs: [("number.0", "soldiers")])] };
        Results("bribe", ("Accept", [Citizen(7936), Down()]), ("DemandMoney", [M(7937, "citizen", inputs: [("number.0", "amount")]), Down()]), ("Decline", [Citizen(7938), Up()]));
        Results("travel-permit", ("Accept", [Citizen(7941), Up()]), ("DemandMoney", [Citizen(7942), Down()]), ("Decline", [Citizen(7943), Down()]));
        Results("festival", ("Accept", [Citizen(7946), Up()]), ("Decline", [Citizen(7947), M(7948)]),
            ("Execute", [Citizen(7949), M(7950, sound: "execution-result"), Down()]));
        Results("monument", ("Accept", [M(7983, sound: good, image: "decision-commemoration"), Up()]), ("Decline", [M(7984)]));
        Results("homecoming-sacrifice", ("Accept", [M(7987, sound: good, image: "decision-commemoration"), Up()]), ("Decline", [M(7988)]));
        Results("training-site", ("Accept", [M(7991, sound: good, image: "training-image")]), ("Decline", [M(7995)]));
        foreach (var (role, prompt, intro, reply) in new[] {
            ("money-demand",8011,7907,7908), ("scholar-punishment",7888,7886,7887), ("evildoer",8014,7846,7930),
            ("land-spirit",8018,7846,7951), ("bandit-suppression",8019,7851,7852),
            ("traveler-rescue",8020,7955,7852), ("bandit-mediation",8021,7957,7958) })
            Add(role, DecisionWindowKind.VerticalChoice, M(prompt, inputs: role is "money-demand" or "scholar-punishment" ? [("person.0", "other"), ("person.1", "actor"), ("number.0", "cost")] : []),
                [M(intro, inputs: role is "money-demand" or "scholar-punishment" ? [("person.0", "other"), ("person.1", "actor"), ("number.0", "cost")] : []), M(reply, "other", inputs: role == "money-demand" ? [("number.0", "cost")] : [])],
                [("Pay","允诺 {cost}"),("Execute","斩杀"),("Allow","容许"),("Fight","武力解决"),("Persuade","智力解决"),
                 ("Appease","以金钱 {cost} 怀柔"),("MediatePeace","和平仲裁"),("Decline","拒绝")]);
        Results("money-demand", ("Pay", [M(7911, "other"), Up()]), ("Execute", [M(7914, "other"), Down()]));
        events["scholar-punishment"] = events["scholar-punishment"] with
        {
            Introduction = [
            M(7886,inputs:[("person.0","actor"),("person.1","other")]),M(7887,"other")]
        };
        Results("scholar-punishment", ("Allow", [M(7889, "other"), Up()]),
            ("Execute", [M(7890, "other"), M(7891, "actor", inputs: [("person.1", "other")]), Down()]));
        Results("evildoer", ("success", [M(7931, sound: good), Citizen(7847), Up()]), ("failure", [M(7932, sound: bad), Citizen(7848), Down()]), ("Decline", [Citizen(7933), Down()]));
        Results("land-spirit", ("success", [M(7952, sound: good), Citizen(7847), Up()]), ("failure", [M(7953, sound: bad), Citizen(7848), Down()]), ("Decline", [Citizen(7849), Down()]));
        Results("bandit-suppression", ("success", [M(7853, sound: good), Citizen(7847), Up()]), ("failure", [M(7854, sound: bad), Citizen(7848), Down()]),
            ("Appease", [M(7954, sound: good, inputs: [("number.0", "cost")]), Citizen(7847), Up()]), ("Decline", [Citizen(7849), Down()]));
        Results("traveler-rescue", ("success", [M(7853, sound: good), M(7956, sound: increase, inputs: [("number.0", "amount")])]),
            ("failure", [M(7854, sound: bad), Citizen(7848), Down()]), ("Decline", [Citizen(7849), Down()]));
        Results("bandit-mediation", ("success", [M(7959, sound: good), Citizen(7960), M(7855, sound: increase, inputs: [("number.0", "soldiers")]), Up()]),
            ("failure", [M(7854, sound: bad), Citizen(7962), Down()]), ("Decline", [Down()]));
        foreach (string role in new[] { "evildoer", "land-spirit", "bandit-suppression", "traveler-rescue", "bandit-mediation" })
            events[role] = events[role] with { Introduction = events[role].Introduction.Select(m => m.Speaker == "other" ? m with { Speaker = "citizen", Bindings = new(m.Bindings) { ["person.0"] = "citizen" } } : m).ToArray() };
        foreach (string role in new[] { "bandit-suppression", "traveler-rescue", "bandit-mediation" })
            events[role] = events[role] with { Introduction = events[role].Introduction.Select((m, i) => i == 0 ? m with { Illustration = "bandit-image" } : m).ToArray() };
        Dictionary<string, string> subjects = [];
        string[] categoryIds = ["salt", "textiles", "distribution", "trade", "livestock", "iron", "farmland", "orchard"];
        string[] categoryNames = ["盐田", "纺织品", "流通", "贸易", "放牧", "铁矿", "农地", "果树"];
        for (int i = 0; i < categoryIds.Length; i++) subjects["merchant." + categoryIds[i]] = categoryNames[i];
        string[] disasters = ["旱魃", "洪水", "歉收", "瘟疫"];
        for (int i = 0; i < disasters.Length; i++) subjects["disaster." + i] = disasters[i];
        subjects["civic-person.0"] = "商人"; subjects["civic-person.1"] = "官吏";
        int[] monumentPlaces = [110, 12, 98, 30, 150, 89, 73, 120, 182, 69, 75, 77];
        string[] monumentNames = ["大公望‧钓鱼台", "张良", "博浪沙", "楚‧庄王", "屈原", "子产", "介之推", "项羽", "伏波将军", "马陵之战", "长平之战", "田单"];
        for (int i = 0; i < monumentPlaces.Length; i++) subjects["monument." + monumentPlaces[i]] = monumentNames[i];
        int[] sacrificePlaces = [30, 113, 80, 110, 11, 77, 9, 5, 78, 76, 105, 16, 21, 82, 15, 85];
        string[] sacrificeNames = ["养由基", "李广", "孙膑", "太公望", "黄石公", "司马穰且", "吴起", "信陵君", "管仲", "晏婴", "商鞅", "吕不韦", "淮南王‧刘安", "孔丘", "老子", "庄周"];
        for (int i = 0; i < sacrificePlaces.Length; i++) subjects["homecoming-sacrifice." + sacrificePlaces[i]] = sacrificeNames[i];
        string[] tactics = ["步兵", "骑兵", "弓骑", "弩兵", "水军", "攻城", "智略", "谋略", "策略"];
        for (int i = 0; i < tactics.Length; i++) subjects["tactic-category." + i] = tactics[i];
        int[] bandits = [0, 1, 0, 2, 1, 2, 1, 3, 3, 0, 4, 4, 1, 2]; string[] banditNames = ["马贼", "山贼", "海贼", "盗贼", "江贼"];
        for (int i = 0; i < bandits.Length; i++) subjects["bandits." + i] = banditNames[bandits[i]];
        return new(events, new() { ["citizen"] = Person(671), ["soldier"] = Person(670), ["north-dipper"] = Person(666), ["south-dipper"] = Person(667) })
        { Subjects = subjects, Shared = shared };
    }
}
