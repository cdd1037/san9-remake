using System.Collections.ObjectModel;
using System.Globalization;
using San9.Content.Text;

namespace San9.Importer.San9PkTc101.Text;

/// <summary>TC101 archive layout to publication identities. Source addresses never leave the importer.</summary>
public static class PublicationMessageCatalog
{
    public static IReadOnlyDictionary<string, string> Keys { get; } = BuildKeys();

    public static Dictionary<string, Dictionary<int, string>> SourceIdMap() => Keys
        .GroupBy(pair => pair.Key[..pair.Key.IndexOf('.')], StringComparer.Ordinal)
        .ToDictionary(group => group.Key switch
        {
            "messages" => "M_MsgPK.s9",
            "biographies" => "M_RtdnPK.s9",
            _ => throw new InvalidDataException($"Unknown source message archive: {group.Key}.")
        }, group => group.OrderBy(pair => int.Parse(pair.Key[(pair.Key.IndexOf('.') + 1)..], CultureInfo.InvariantCulture))
            .ToDictionary(pair => int.Parse(pair.Key[(pair.Key.IndexOf('.') + 1)..], CultureInfo.InvariantCulture), pair => pair.Value));

    public static Dictionary<string, MessageDefinition> Compile(IReadOnlyDictionary<string, MessageDefinition> source)
    {
        if (source.Count != Keys.Count || source.Keys.Any(key => !Keys.ContainsKey(key)))
            throw new InvalidDataException("Message archive coverage differs from the reviewed publication catalog.");

        MessageNode[] Rename(MessageNode[] nodes) => nodes.Select<MessageNode, MessageNode>(node => node switch
        {
            MessageReference r => r with { Key = Keys.TryGetValue(r.Key, out string? key) ? key
                : throw new InvalidDataException($"Unmapped message reference: {r.Key}.") },
            MessageConditional c => c with { Body = Rename(c.Body) },
            MessageChoice c => c with { Choices = c.Choices.Select(Rename).ToArray() },
            MessageRandomChoice c => c with { Choices = c.Choices.Select(Rename).ToArray() },
            MessageStyled s => s with { Body = Rename(s.Body) },
            _ => node
        }).ToArray();

        var result = source.ToDictionary(p => Keys[p.Key], p => new MessageDefinition(Rename(p.Value.Nodes)), StringComparer.Ordinal);
        MessageGraph.Validate(result);
        return result;
    }

    private static IReadOnlyDictionary<string, string> BuildKeys()
    {
        Dictionary<string, string> keys = new(StringComparer.Ordinal);
        // Inclusive source ranges. Ordinals identify distinct authored records/variants, not runtime entity slots.
        // Do not infer personality, event wiring, or unused status from text alone. Empty records are retained.
        void Range(int first, int last, string prefix, params string[] fields)
        {
            for (int id = first; id <= last; id++)
            {
                int offset = id - first;
                string suffix = fields.Length == 0
                    ? (first == last ? "" : $".variant-{(offset + 1).ToString("D3", CultureInfo.InvariantCulture)}")
                    : $".{(offset / fields.Length).ToString("D3", CultureInfo.InvariantCulture)}.{fields[offset % fields.Length]}";
                keys.Add($"messages.{id}", prefix + suffix);
            }
        }
        void Sections(string category, int last, params (int Start, string Name)[] sections)
        {
            for (int i = 0; i < sections.Length; i++)
                Range(sections[i].Start, i + 1 < sections.Length ? sections[i + 1].Start - 1 : last,
                    category + "." + sections[i].Name);
        }

        Range(0, 39, "scenarios.standard", "title", "introduction");
        Range(40, 59, "scenarios.additional", "title", "introduction");
        Range(60, 71, "scenarios.tutorial", "title", "introduction");
        string[] chapters = MessageCampaignChapters.Entries.Select(entry => entry.Name)
            .Where(name => !name.EndsWith(".prologue", StringComparison.Ordinal)).ToArray();
        for (int i = 0; i < chapters.Length; i++)
        {
            Range(72 + i * 2, 72 + i * 2, $"campaign.{chapters[i]}.title");
            Range(73 + i * 2, 73 + i * 2, $"campaign.{chapters[i]}.summary");
        }
        Range(136, 165, "names.province", "name", "alternate");
        Range(166, 565, "names.region", "name", "alternate");
        Range(566, 703, "labels.settlement");
        Range(704, 720, "names.terrain");
        Range(721, 820, "names.city", "name", "alternate");
        Range(821, 1031, "labels.settlement-details");
        Range(1032, 1218, "labels.force");
        Range(1219, 1341, "descriptions.tactic", "name", "alternate", "description");
        Range(1342, 1391, "labels.tactic");
        Range(1392, 1631, "names.treasure", "name", "alternate");
        Range(1632, 1651, "labels.treasure");
        Range(1652, 1669, "names.rank", "name", "alternate");
        Range(1670, 1677, "labels.rank");
        Range(1678, 4877, "names.person", "surname", "given-name", "courtesy-name", "alternate");
        Range(4878, 5064, "labels.person");
        Range(5065, 5216, "names.office", "name", "alternate");
        Range(5217, 5234, "labels.office");
        Range(5235, 5314, "labels.unit");
        Range(5315, 5354, "names.country", "name", "alternate");
        Range(5355, 5362, "labels.country");
        Range(5363, 5446, "descriptions.formation", "name", "alternate", "description");
        Range(5447, 5467, "labels.formation");
        Range(5468, 5477, "dialogue.execution");
        Range(5478, 5495, "dialogue.refuse-surrender");
        Range(5496, 5502, "dialogue.dismissal");
        Range(5503, 5514, "dialogue.release-captive");
        Range(5515, 5522, "dialogue.appoint-strategist");
        Range(5523, 5529, "dialogue.replace-strategist");
        Sections("dialogue", 5884,
            (5530, "dismiss-strategist"), (5535, "promotion"), (5542, "demotion"),
            (5550, "corps-assignment"), (5552, "accept-recruitment"), (5572, "office-assignment"),
            (5574, "treasure-transfer"),
            (5576, "order-sortie"), (5582, "order-construction"), (5588, "order-demolition"),
            (5592, "order-patrol"), (5599, "order-commerce"), (5606, "order-cultivation"),
            (5613, "order-repair"), (5621, "repair-complete"), (5627, "order-recruit-soldiers"),
            (5633, "order-drill"), (5640, "order-trade"), (5646, "order-transfer"),
            (5658, "order-transfer-injured"), (5669, "order-search"), (5675, "order-recruit-officer"),
            (5681, "order-false-report"), (5687, "order-alienation"), (5693, "order-dissension"),
            (5699, "order-infiltration"), (5705, "order-sabotage"), (5711, "order-encouragement"),
            (5717, "order-reinforcement"), (5723, "order-confusion"), (5729, "order-ransom"),
            (5735, "order-request-attack"), (5741, "order-gift"), (5747, "order-demand-surrender"),
            (5753, "captive-disposition-notice"), (5757, "declare-war"), (5761, "end-alliance"),
            (5767, "break-treaty"), (5770, "order-march"),
            (5776, "trade-results"), (5778, "order-persuasion"), (5784, "refuse-persuasion"),
            (5788, "treasure-confiscated"), (5792, "order-transport"), (5798, "cooperative-cultivation"),
            (5805, "cooperative-commerce"), (5812, "cooperative-repair"), (5819, "cooperative-drill"),
            (5826, "cooperative-patrol"), (5833, "redirect-transport"), (5839, "redirect-attack"),
            (5845, "redirect-march"), (5851, "order-retreat"), (5857, "personnel-results"),
            (5860, "cancel-attack-agreement"), (5861, "domestic-special-exchanges"),
            (5873, "domestic-and-regional-advice"), (5879, "change-unit-policy"));
        Range(5885, 6126, "commands.requirements-and-confirmations");
        Sections("dialogue", 6260,
            (6127, "force-surrender"), (6136, "force-and-personnel-changes"), (6141, "coming-of-age"),
            (6148, "welcome-officer"), (6160, "reject-service-response"), (6179, "escape-captivity"),
            (6188, "offer-service"), (6200, "accept-service"), (6211, "reject-service"),
            (6230, "depart-for-study"), (6236, "return-from-study"), (6246, "propose-unification"),
            (6253, "accept-unification"), (6260, "food-exhausted"));
        Range(6261, 6339, "dialogue.recruitment");
        Range(6340, 6350, "dialogue.search-results");
        Range(6351, 6401, "dialogue.diplomatic-audience");
        Range(6402, 6488, "dialogue.request-attack");
        Range(6489, 6558, "dialogue.ransom");
        Range(6559, 6595, "dialogue.diplomatic-gift");
        Range(6596, 6641, "dialogue.demand-surrender");
        Range(6642, 6686, "battle.target-orders");
        Range(6687, 6715, "battle.injury-and-defeat");
        Range(6716, 6976, "battle.tactics");
        Range(6977, 6994, "battle.attack-facility");
        Range(6995, 7094, "battle.duel-challenge");
        Range(7095, 7109, "battle.mission-change-retreat");
        Range(7110, 7113, "reports.defection-and-audience");
        Range(7114, 7114, "battle.transport-target-unavailable");
        Range(7115, 7164, "battle.tactics-and-duels");
        Range(7165, 7193, "reports.personnel-and-resources");
        Range(7194, 7196, "reports.force-destroyed");
        Range(7197, 7231, "dialogue.captive-disposition");
        Range(7232, 7238, "reports.personnel-and-relations");
        Sections("dialogue", 7505,
            (7239, "rumor-person"), (7243, "rumor-hidden-person"), (7247, "rumor-nearby-person"),
            (7251, "rumor-treasure"), (7258, "rumor-hidden-treasure"), (7262, "rumor-nearby-treasure"),
            (7266, "advise-rest"), (7270, "foresee-own-death"), (7274, "foresee-person-death"),
            (7293, "warn-enemy-march"), (7297, "auspicious-sign"), (7301, "recruitment-uncertain"),
            (7305, "recruitment-likely"), (7309, "recruitment-unlikely"), (7313, "scheme-likely"),
            (7317, "scheme-unlikely"), (7321, "scheme-uncertain"), (7325, "expect-conditions"),
            (7329, "advise-conditions"), (7333, "terms-reasonable"), (7337, "terms-unreasonable"),
            (7341, "request-likely"), (7345, "request-unlikely"), (7349, "ransom-unlikely"),
            (7353, "gift-welcome"), (7357, "gift-unwelcome"), (7361, "accept-gift-advice"),
            (7365, "reject-gift-advice"), (7369, "surrender-likely"), (7373, "surrender-unlikely"),
            (7377, "advise-surrender"), (7381, "advise-resistance"), (7385, "diplomacy-results"),
            (7395, "mission-too-late"), (7399, "diplomacy-confirmations"),
            (7408, "attack-already-agreed"), (7412, "mission-return"),
            (7417, "rumor-person-location"), (7421, "rumor-hermit-location"),
            (7425, "rumor-treasure-location"), (7432, "rumor-rare-treasure-location"),
            (7436, "rumor-enemy-march"), (7440, "relations-and-alliance"),
            (7450, "defense-in-danger"), (7455, "advise-naval-defense"),
            (7463, "advise-hold-fortress"), (7470, "advise-break-siege"),
            (7474, "advise-attack-siege-engines"), (7482, "advise-sortie"),
            (7490, "suspected-attack"), (7494, "vulnerable-target"),
            (7498, "food-shortage"), (7502, "money-shortage"));
        Range(7506, 7508, "reports.unit-personnel");
        Range(7509, 7519, "system.setup");
        Range(7520, 7534, "names.movie");
        Range(7535, 7557, "names.music");
        Range(7558, 7573, "system.data-management");
        Range(7574, 7640, "ending.unification");
        Range(7641, 7740, "descriptions.treasure");
        Range(7741, 7760, "descriptions.country");
        Range(7761, 7769, "descriptions.rank");
        Range(7770, 7845, "descriptions.office");
        Range(7846, 8023, "encounters.search");
        Range(8024, 8036, "system.session");
        Range(8037, 8158, "interface.setup");
        Range(8159, 8451, "interface.strategy");
        Range(8452, 8559, "interface.editor");
        Range(8560, 8576, "commands.conditions");
        for (int i = 0; i < MessageHelpTopics.Entries.Length; i++)
        {
            var (start, topic) = MessageHelpTopics.Entries[i];
            int end = i + 1 < MessageHelpTopics.Entries.Length ? MessageHelpTopics.Entries[i + 1].Start - 1 : 9323;
            if (topic == "command-condition-cards") Range(start, end, $"help.{topic}");
            else
            {
                Range(start, start, $"help.{topic}.title");
                if (start < end) Range(start + 1, end, $"help.{topic}.description");
            }
        }
        Range(9324, 9421, "history.events");
        Range(9422, 9904, "tutorial.strategy");
        Range(9905, 10121, "reports.turn");
        Range(10122, 10139, "tutorial.turn-log");
        Range(10140, 10144, "reports.battle");
        Sections("battle", 11356,
            (10145, "engage-unit"), (10154, "unwilling-retreat"), (10163, "ignore-enemy"),
            (10172, "turn-to-attack"), (10181, "low-morale-retreat"), (10190, "low-strength-retreat"),
            (10199, "low-morale-withdrawal"), (10208, "low-strength-withdrawal"),
            (10217, "pursue-defeated"), (10226, "pursue-retreating"), (10235, "stop-pursuit"),
            (10244, "withdraw"), (10253, "next-unit-target"), (10262, "next-facility-target"),
            (10271, "withdraw-outmatched"), (10276, "withdraw-to-base"),
            (10285, "continue-unit-attack"), (10290, "continue-facility-attack"),
            (10295, "change-destination"), (10304, "cancel-construction"), (10313, "cancel-transport"),
            (10322, "change-retreat-destination"), (10331, "enter-facility"),
            (10340, "battering-ram"), (10349, "siege-tower"), (10358, "catapult"),
            (10367, "elephants"), (10376, "assault-facility"), (10385, "naval-arrow-attack"),
            (10394, "naval-assault"), (10403, "charge"), (10410, "surround-enemy"),
            (10419, "attack-weaker-enemy"), (10430, "attack-larger-enemy"),
            (10441, "attack-cautiously"), (10452, "hold-defense"), (10461, "return-arrow-fire"),
            (10470, "naval-counterattack"), (10479, "engage-reinforcements"),
            (10488, "defend-outnumbered"), (10497, "defend-with-advantage"), (10508, "counterattack"),
            (10519, "construction-blocked"), (10528, "construction-start"), (10537, "construction-complete"),
            (10546, "false-report-send"), (10555, "false-report-believed"), (10564, "false-report-exposed"),
            (10573, "round-formation-detect-spy"), (10582, "confusion-attempt"), (10591, "confusion-hit"),
            (10600, "confusion-avoided"), (10609, "encouragement"), (10618, "morale-restored"),
            (10622, "reinforcement-arrival"), (10633, "arson-attempt"), (10642, "arson-hit"),
            (10651, "arson-detected"), (10660, "treasure-seized"), (10669, "treasure-lost"),
            (10678, "treasure-theft-detected"), (10684, "rescue-captives"), (10693, "captives-escaped"),
            (10702, "steal-money"), (10711, "money-stolen"), (10720, "money-theft-detected"),
            (10729, "round-formation-resistance"), (10745, "scheme-ineffective"),
            (10754, "scheme-exposed"), (10763, "tactic-resisted"), (10772, "release-captives"),
            (10781, "confusion-tactic"), (10790, "confusion-tactic-resisted"),
            (10799, "confusion-tactic-hit"), (10808, "confusion-recovery"),
            (10817, "taunt"), (10830, "taunt-resisted"), (10839, "taunt-hit"),
            (10848, "taunt-partially-resisted"), (10857, "rally"), (10867, "persuade-soldiers"),
            (10876, "persuasion-resisted"), (10885, "soldiers-defect"), (10892, "soldiers-desert"),
            (10901, "pit-trap"), (10910, "pit-trap-avoided"), (10919, "pit-trap-hit"),
            (10928, "pit-trap-aftermath"), (10937, "fire-trap"), (10946, "fire-trap-avoided"),
            (10955, "fire-trap-hit"), (10964, "fire-trap-aftermath"),
            (10971, "water-trap"), (10980, "water-trap-avoided"), (10989, "water-trap-hit"),
            (10998, "water-trap-aftermath"), (11005, "rock-trap"), (11014, "rock-trap-avoided"),
            (11023, "rock-trap-hit"), (11032, "rock-trap-aftermath"),
            (11037, "illusion"), (11046, "illusion-resisted"), (11053, "illusion-hit"),
            (11062, "illusion-aftermath"), (11071, "sorcery"), (11080, "sorcery-resisted"),
            (11087, "sorcery-hit"), (11094, "sorcery-aftermath"), (11099, "healing"),
            (11105, "defeat-retreat"), (11114, "captured"), (11124, "return-to-base"),
            (11133, "ram-battle-cry"), (11140, "siege-arrow-battle-cry"), (11147, "catapult-battle-cry"),
            (11154, "elephant-battle-cry"), (11161, "arrow-battle-cry"), (11168, "advance-battle-cry"),
            (11175, "defensive-battle-cry"), (11184, "challenge-battle-cry"), (11190, "melee-battle-cry"),
            (11199, "press-attack"), (11208, "victory-in-sight"), (11217, "desperate-fighting"),
            (11226, "sustained-arrow-fire"), (11233, "sustained-charge"), (11240, "low-morale-warning"),
            (11249, "march-near-enemy"), (11258, "march-progress"), (11267, "transport-progress"),
            (11276, "construction-progress"), (11285, "clear-route"), (11294, "join-force"),
            (11303, "join-force-attack-unit"), (11312, "join-force-attack-facility"),
            (11321, "unexpected-situation"), (11330, "destroy-obstacle"), (11339, "demolition-progress"),
            (11348, "waypoint-arrival"));
        Range(11357, 11454, "chronicle.events");
        Range(11455, 11662, "name-generation.surname");
        Range(11663, 12612, "name-generation.given-name");
        Sections("training", 12922,
            (12613, "ability-report"), (12615, "setup"), (12625, "appoint-mentor"),
            (12629, "mentor-accepts"), (12633, "clear-instruction"), (12637, "poor-understanding"),
            (12640, "student-response"), (12645, "mentor-reprimand"), (12647, "praise-progress"),
            (12651, "encourage-student"), (12655, "student-motivation"),
            (12663, "demand-results"), (12669, "student-determination"),
            (12676, "advise-rest"), (12683, "training-exchanges"),
            (12703, "mentor-dissatisfaction"), (12706, "replace-mentor"), (12708, "reject-complaint"),
            (12711, "request-mentor-change"), (12712, "reconsider-mentor-change"),
            (12715, "approve-mentor-change"), (12718, "training-interruption"),
            (12722, "mentor-reflection"), (12724, "family-visit"),
            (12728, "request-travel"), (12732, "discourage-leaving"),
            (12740, "student-conflict"), (12749, "discipline"), (12755, "progress-report"),
            (12760, "excellent-progress"), (12763, "no-progress"), (12766, "small-progress"),
            (12769, "normal-progress"), (12772, "student-progress-response"),
            (12777, "continue-training"), (12780, "complete-training"),
            (12783, "final-ability-assessment"), (12800, "final-tactic-assessment"),
            (12820, "student-gratitude"), (12823, "student-departure"), (12838, "welcome-return"),
            (12850, "tactic-instruction"), (12859, "tactic-study-start"),
            (12863, "tactic-study-result"), (12871, "mentor-tactic-feedback"),
            (12883, "tactics-learned"), (12894, "student-lost"), (12898, "officer-appointed"),
            (12899, "talent-discovered"), (12900, "instruction-result"),
            (12901, "mentor-unavailable"), (12902, "instruction-ended"),
            (12903, "mentor-learning"), (12906, "report-headings"), (12908, "recommend-talent"));
        Sections("campaign", 14106, MessageCampaignChapters.Entries);
        for (int i = 0; i < chapters.Length; i++)
            Range(14107 + i, 14107 + i, $"campaign.{chapters[i]}.objective");
        Range(14139, 14208, "campaign.outcomes");

        // Named entry points used by UI/business callers, independent of the archive layout.
        keys["messages.5534"] = "reports.strategist-appointed";
        keys["messages.5550"] = "reports.corps-delegation-changed";
        keys["messages.5551"] = "reports.corps-created";
        keys["messages.5572"] = "reports.offices-changed";
        keys["messages.5573"] = "reports.office-assignment-discontent";
        keys["messages.5574"] = "reports.treasure-awarded";
        keys["messages.5575"] = "reports.treasure-confiscated";
        keys["messages.5754"] = "reports.captive-executed";
        keys["messages.5776"] = "reports.food-sold";
        keys["messages.5777"] = "reports.food-bought";
        keys["messages.5857"] = "reports.officers-dismissed";
        keys["messages.5858"] = "reports.officers-dismissed-to-ruler";
        keys["messages.5859"] = "reports.loyalty-reward";
        keys["messages.5878"] = "interface.unit-policy-unchanged";
        keys["messages.6138"] = "interface.choose-country-name";
        keys["messages.6139"] = "interface.confirm-country-name";
        keys["messages.7235"] = "reports.unit-return";
        keys["messages.7236"] = "ending.time-limit";
        keys["messages.8427"] = "interface.map-scroll.hover";
        keys["messages.8428"] = "interface.map-scroll.click";
        keys["messages.8676"] = "help.appearance.title";
        keys["messages.8677"] = "help.appearance.description";
        for (int id = 0; id < 679; id++)
            keys.Add($"biographies.{id}", $"biographies.person.{id.ToString("D3", CultureInfo.InvariantCulture)}");
        if (keys.Count != 14888 || keys.Values.Distinct(StringComparer.Ordinal).Count() != keys.Count)
            throw new InvalidDataException("Incomplete or duplicate publication message identities.");
        return new ReadOnlyDictionary<string, string>(keys);
    }
}
