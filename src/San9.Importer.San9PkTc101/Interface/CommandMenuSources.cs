namespace San9.Importer.San9PkTc101.Interface;

// RE-SAN9PK-0073: CCommandMenuDlg descriptors at 0x657AA0 and 0x657E48.
internal static class CommandMenuSources
{
    internal static readonly (string Id, int Illustration, int Message, int Hint)[] Entries = [
        ("facilities", 31, 6086, 6086),
        ("military", 7, 5975, 5975),
        ("talent", 13, 6097, 6097),
        ("strategy", 20, 5977, 5977),
        ("diplomacy", 4, 5979, 5979),
        ("appointments", 26, 6084, 6084),
        ("patrol", 14, 9057, 6003),
        ("commerce", 33, 9058, 6005),
        ("cultivate", 16, 9059, 6007),
        ("repair", 17, 9161, 5951),
        ("recruit", 2, 8569, 5908),
        ("train", 24, 9060, 6011),
        ("buy", 23, 9162, 6106),
        ("sell", 0, 9163, 6108),
        ("demolish", 37, 9061, 6022),
        ("sortie", 34, 8560, 6024),
        ("build", 21, 9062, 5937),
        ("transport", 43, 9164, 6109),
        ("summon", 41, 9064, 6064),
        ("move", 11, 8571, 6065),
        ("search", 36, 8572, 5917),
        ("employ", 38, 8573, 5972),
        ("alienate", 27, 9068, 6045),
        ("burn", 40, 9069, 6043),
        ("seize", 3, 9070, 6041),
        ("rumor", 28, 8575, 5982),
        ("false-report", 6, 8574, 6035),
        ("disorder", 18, 9065, 6038),
        ("inspire", 5, 9066, 6052),
        ("rescue", 25, 9067, 6049),
        ("gift", 44, 9071, 6055),
        ("request", 42, 8576, 5986),
        ("return", 9, 9072, 5990),
        ("surrender", 19, 9073, 6057),
        ("delegate", 12, 8561, 5904),
        ("strategist", 8, 8562, 5895),
        ("rank", 29, 8565, 5902),
        ("reward", 10, 9074, 6032),
        ("bestow", 15, 8563, 5899),
        ("confiscate", 1, 8564, 5901),
        ("execute", 32, 8566, 5892),
        ("exile", 39, 8567, 5894),
        ("attack", 22, 9225, 6116),
        ("march", 30, 9226, 6115),
        ("redirect-transport", 43, 9227, 6114),
    ];

    // RE-0073/0095/0096/0097: ordered validation reasons are separate from condition cards.
    internal static readonly (string Id, int Message)[] Reasons = [ ("unavailable",5997), ("already-used",6001), ("public-order-max",6002),
            ("no-officer",5970), ("funds",5889), ("confused",5999), ("engaged",6000),
            ("commerce-max",6004), ("cultivation-max",6006), ("durability-max",6008), ("no-soldiers",5887),
            ("morale-max",6010), ("soldiers-max",6124), ("no-recruits",5906), ("merchant-absent",5909),
            ("food-max",6110), ("money-max",6111), ("no-food",6107), ("insufficient-food",5911),
            ("no-funds",5888), ("recruit-no-funds",5907), ("recruit-used",5905), ("trade-used",6105),
            ("no-move-destination",6063), ("no-other-facility",6059), ("no-construction-site",5936), ("no-employ-target",5971),
            ("no-alienate-target",6068), ("no-target-facility",6040), ("no-false-report-target",6034), ("no-disorder-target",6037),
            ("no-inspire-target",6051), ("no-rescue-target",6048), ("no-rumor-forces",5981), ("no-rumor-officers",5995),
            ("no-target-force",5985), ("no-gift-resources",6054), ("no-captured-officer",5989), ("unit-routed",6117),
            ("unit-confused",6118), ("unit-no-formation",6119), ("unit-false-report",6120), ("no-adviser-candidate",5890),
            ("no-stationed-officer",6029), ("no-reward-candidate",6031), ("no-treasure",5898), ("no-treasure-holder",5900),
            ("no-stationed-treasure-holder",6103), ("no-prisoner",5891), ("no-exile-candidate",5893), ("no-subordinate",5969),
            ("no-office-candidate",5994), ("no-office",6104), ("no-delegation-city",5903), ("no-corps-leader",5968),
            ("no-normal-facility",6060), ("no-recall-candidate",6062)
    ];
    internal static readonly (string Id, int Message, int Amount)[] NumberedReasons = [ ("reward-funds",6028,100), ("loyalty-max",6030,100), ("rescue-soldiers",6047,3000)
    ];
}
