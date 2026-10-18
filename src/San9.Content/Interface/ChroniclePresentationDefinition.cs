namespace San9.Content.Interface;

public sealed record ChroniclePresentationDefinition(Dictionary<string, DecisionMessageDefinition> Messages,
    Dictionary<string, DecisionMessageDefinition> Hints)
{
    public static readonly string[] MessageRoles = ["emperor-succession",
        "rule", "rule-ranked", "ruler-hosted", "ruler-hosted-ranked", "adviser", "adviser-appointed",
        "demolition", "city-surrendered", "cities-surrendered", "facility-surrendered", "facilities-surrendered", "city-defected",
        "cities-defected", "facility-defected", "facilities-defected", "unified", "natural-death", "force-absorbed",
        "force-surrendered", "city-enticed", "city-enticed-lost", "facility-enticed", "facility-enticed-lost", "execution",
        "execution-local", "construction", "request-accepted", "request-promised", "city-captured", "city-lost",
        "facility-captured", "facility-lost", "succession", "independence", "founding", "story-ended",
        "eliminated", "eliminated-named", "request-fulfilled", "request-fulfilled-peer", "request-expired", "request-expired-peer",
        "request-broken", "request-broken-peer", "request-place-destroyed", "request-place-destroyed-peer", "request-owner-changed", "request-owner-changed-peer",
        "request-force-eliminated", "request-force-eliminated-peer", "chained-prelude", "chained-coup", "visits-recommendation", "visits-first",
        "visits-recruited", "wa-discovered", "ceremony", "abdication-han", "emperor-killed", "memorial",
        "five-tigers", "qiao-marriage", "sun-marriage", "guandu-won", "guandu-lost", "red-cliffs-won",
        "red-cliffs-lost", "emperor-supported", "emperor-deposed", "ling-succession", "shao-succession", "emperor-abdication",
        "emperor-proclamation", "emperor-enthroned", "rank-appointed", "rank-proclaimed", "alliance-led", "alliance-joined",
        "alliance-formed", "alliance-victory", "alliance-leader-eliminated", "alliance-leader-died", "alliance-empty", "alliance-target-eliminated",
        "alliance-target-died", "alliance-expired", "alliance-betrayed", "alliance-member-expelled", "alliance-member-left", "consolidation",
        "duel-death", "sniping-death", "melee-death", "soldier-promoted", "city-attacked", "facility-attacked",
        "emperor-abdication-untitled", "emperor-proclamation-untitled"
    ];
    public static readonly string[] HintRoles = ["records", "summary", "detailed", "close", "chronicle", "input", "records-help", "records-help-title", "records-help-detail", "records-help-detail-title", "chronicle-help", "chronicle-help-title"];
}
