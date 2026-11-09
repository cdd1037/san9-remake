using San9.Core.Domain;

namespace San9.Client.Godot;

public partial class CompanionSettingsView
{
    private abstract record Option(string Title, string Note);
    private sealed record ToggleOption(string Title, string Note,
        Func<CompanionSettings, bool> Read, Func<CompanionSettings, bool, CompanionSettings> Write, NumericOption? Parameter = null,
        string EnabledText = "开启", string DisabledText = "关闭", bool PresetValue = true, bool DomesticOnly = false) : Option(Title, Note);
    private sealed record NumericOption(string Title, string Note, int Minimum, int Maximum,
        Func<CompanionSettings, int> Read, Func<CompanionSettings, int, CompanionSettings> Write,
        string Suffix = "", int? PresetValue = null, bool DomesticOnly = false) : Option(Title, Note);
    private sealed record NumberGroupOption(string Title, string Note, NumericOption[] Fields, int Width) : Option(Title, Note);
    private sealed record PlayerAutomationOption() : Option("直属军团自动安排", "");
    private sealed record FormationAppearanceOption() : Option("阵型差异外观",
        "普通八阵使用不同中央兵模，锥行、锋矢与其他阵型区分。关闭面板后生效。");
    private sealed record SectionDefinition(string Title, Option[] Options,
        Func<bool, IEnumerable<ToggleOption>>? Permissions = null);
    private sealed record PageDefinition(string Title, SectionDefinition[] Sections);

    private static readonly NumericOption[] IncomeOptions =
    [
        new("资金", "普通电脑势力的季度资金收入倍率。",
            0, int.MaxValue, settings => settings.ComputerRevenuePercent,
            (settings, value) => settings with { ComputerRevenuePercent = value }, "%", PresetValue: 200),
        new("兵粮", "普通电脑势力的七月兵粮收获倍率。",
            0, int.MaxValue, settings => settings.ComputerHarvestPercent,
            (settings, value) => settings with { ComputerHarvestPercent = value }, "%", PresetValue: 200),
    ];
    private static readonly NumericOption[] AllocationOptions =
    [
        new("1人", "一人部队发动系数，默认20。",
            0, 20, settings => settings.TacticAllocation.One,
            (settings, value) => settings with { TacticAllocation = settings.TacticAllocation with { One = value } }),
        new("2人", "二人部队发动系数，默认14。",
            0, 20, settings => settings.TacticAllocation.Two,
            (settings, value) => settings with { TacticAllocation = settings.TacticAllocation with { Two = value } }),
        new("3人", "三人部队发动系数，默认10。",
            0, 20, settings => settings.TacticAllocation.Three,
            (settings, value) => settings with { TacticAllocation = settings.TacticAllocation with { Three = value } }),
        new("4人", "四人部队发动系数，默认9。",
            0, 20, settings => settings.TacticAllocation.Four,
            (settings, value) => settings with { TacticAllocation = settings.TacticAllocation with { Four = value } }),
        new("5人及以上", "五人及以上部队发动系数，默认8。",
            0, 20, settings => settings.TacticAllocation.FiveOrMore,
            (settings, value) => settings with { TacticAllocation = settings.TacticAllocation with { FiveOrMore = value } }),
    ];

    private static readonly ToggleOption[] DomesticAutomationOptions =
    [
        new("自动内政", "使用闲置武将安排训练、修筑、商业、开垦和巡察。",
            settings => settings.PlayerAutoDomesticEnabled,
            (settings, value) => settings with { PlayerAutoDomesticEnabled = value }, DomesticOnly: true),
        new("探索所在地", "内政之后使用剩余武将，探索事件仍由玩家作答。",
            settings => settings.PlayerAutoSearchEnabled,
            (settings, value) => settings with { PlayerAutoSearchEnabled = value },
            new("最低政治", "按当前有效政治筛选，包含伤势、宝物和官职影响。默认45，0不限制政治。",
                0, int.MaxValue, settings => settings.PlayerAutoSearchMinimumPolitics,
                (settings, value) => settings with { PlayerAutoSearchMinimumPolitics = value }, DomesticOnly: true), DomesticOnly: true),
    ];

    private static readonly PageDefinition[] Pages =
    [
        new("辅助",
        [
            new("军团安排",
            [
                new PlayerAutomationOption(),
                new ToggleOption("允许干预委任军团", "玩家可向子军团下令；AI随后使用剩余资格，并保留本旬已下令部队的命令。",
                    settings => settings.AllowDelegatedCorpsOrders,
                    (settings, value) => settings with { AllowDelegatedCorpsOrders = value }),
            ]),
            new("汇报、操作与外观",
            [
                new ToggleOption("过滤一般探索汇报", "隐藏获得金钱及无收获的汇报，保留宝物、人物和特殊事件提示。手动与自动探索均适用。",
                    settings => settings.HideRoutineSearchReports,
                    (settings, value) => settings with { HideRoutineSearchReports = value }),
                new ToggleOption("选阵后自动配置兵法", "确认陆上阵形后重新配置全队兵法，会覆盖手动选择。",
                    settings => settings.ConfigureTacticsOnFormationSelection,
                    (settings, value) => settings with { ConfigureTacticsOnFormationSelection = value }),
                new FormationAppearanceOption(),
            ]),
        ]),
        new("势力",
        [
            new("钱粮与合并",
            [
                new NumberGroupOption("电脑钱粮收入倍率",
                    "资金按季度收入，兵粮在七月收获。两项均默认100%，200%为双倍，0%停止对应收入；适用于普通电脑势力。",
                    IncomeOptions, 112),
                new ToggleOption("禁止电脑势力合并", "暂停电脑小势力的自动合并事件。",
                    settings => settings.DisableComputerForceConsolidation,
                    (settings, value) => settings with { DisableComputerForceConsolidation = value }),
            ]),
            new("人事",
            [
                new ToggleOption("取消电脑褒奖时机限制",
                    "电脑势力从开局起每旬均可安排褒奖，不再受开局等待和周期限制。",
                    settings => settings.DisableComputerRewardTimingRestrictions,
                    (settings, value) => settings with { DisableComputerRewardTimingRestrictions = value }),
            ]),
            new("自动计略", [], StrategyPermissions),
            new("自动外交", [], DiplomacyPermissions),
            new("军团自动行为", [], CorpsOptions),
        ]),
        new("武将",
        [
            new("能力与寿命",
            [
                new NumericOption("武将基础能力上限", "默认100，125为原伴侣预设。普通成长、事件奖励和武将编辑统一使用此值，已有超限能力不倒扣。",
                    100, int.MaxValue, settings => settings.OfficerAbilityCap,
                    (settings, value) => settings with { OfficerAbilityCap = value }, PresetValue: 125),
                new ToggleOption("分档增加武将能力", "基础能力越高，普通成长概率越低：低能力5%，每15点递减，最低1%；不属于玩家势力的人物再加1%。",
                    settings => settings.DynamicOfficerAbilityGrowth,
                    (settings, value) => settings with { DynamicOfficerAbilityGrowth = value }),
                new ToggleOption("无限寿命", "所有武将不因寿命自然死亡；战死、处斩及剧情死亡仍按各自规则处理。关闭后从下一次季度判定恢复，超龄武将可能寿终。",
                    settings => settings.InfiniteLifespan,
                    (settings, value) => settings with { InfiniteLifespan = value }),
            ]),
            new("熟练度成长",
            [
                new ToggleOption("分档增加兵法熟练度", "精通加成之后，熟练度增量乘以max(1, 4−当前熟练度÷250)÷3。越熟练增长越慢。",
                    settings => settings.DynamicTacticProficiencyGrowth,
                    (settings, value) => settings with { DynamicTacticProficiencyGrowth = value }),
                new ToggleOption("首发与联动武将额外成长", "全员基础熟练度+3，电脑势力+4；首发者额外加一倍基础值，首发者或联动者额外再+2。",
                    settings => settings.RewardTacticParticipants,
                    (settings, value) => settings with { RewardTacticParticipants = value }),
                new ToggleOption("取消交战每日熟练度增长", "取消陆战和水战交战中的每日自动熟练度增长。兵法发动、训练、攻城等其他成长来源继续按各自规则结算。",
                    settings => settings.DisableDailyCombatProficiencyGain,
                    (settings, value) => settings with { DisableDailyCombatProficiencyGain = value }),
            ]),
            new("阵亡",
            [
                new NumericOption("免阵亡能力门槛", "基础统率、武力、智力、政治任一项达到此值时免于战斗阵亡。默认80，预设125；0保护所有武将，与成长上限独立。战斗阵亡须在PK设定中开启。",
                    0, int.MaxValue, settings => settings.BattleDeathProtectionThreshold,
                    (settings, value) => settings with { BattleDeathProtectionThreshold = value }, PresetValue: 125),
                new ToggleOption("部队主将可阵亡", "允许部队主将参与单挑、混战和阻击的阵亡判定，仍受能力门槛与原有豁免保护；单挑中主将仍不会被俘。",
                    settings => settings.AllowCommanderBattleDeaths,
                    (settings, value) => settings with { AllowCommanderBattleDeaths = value }),
                new NumericOption("单挑阵亡系数", "范围0～10，默认0。单挑阵亡概率为min(30, 80+4×系数−有效武力)%；仍受阵亡资格限制。",
                    0, 10, settings => settings.DuelDeathCoefficient,
                    (settings, value) => settings with { DuelDeathCoefficient = value }),
            ]),
            new("俘虏处置",
            [
                new ToggleOption("禁止玩家势力处斩俘虏", "玩家势力不能处斩普通俘虏，包含手动处斩及委任军团；不包括君主处置。",
                    settings => settings.DisablePlayerPrisonerExecution,
                    (settings, value) => settings with { DisablePlayerPrisonerExecution = value }),
                new ToggleOption("禁止电脑势力处斩俘虏", "电脑势力及其军团不能处斩普通俘虏；不包括君主处置。",
                    settings => settings.DisableComputerPrisonerExecution,
                    (settings, value) => settings with { DisableComputerPrisonerExecution = value }),
            ]),
        ]),
        new("兵法",
        [
            new("发动",
            [
                new NumberGroupOption("人数发动系数",
                    "默认20／14／10／9／8，各项范围0～20，0关闭对应人数的个人兵法分配。先按人数选档，再计算能力修正、阵位和擅长系；自动水军与设施不增加个人分配判定。",
                    AllocationOptions, 80),
                new ToggleOption("统率和智力影响兵法发动概率", "武力兵法按执行统率、其他兵法按执行智力调整成员发动分配。人数系数乘以min(150, 执行能力+60)÷100。",
                    settings => settings.AbilitiesAffectTacticAllocation,
                    (settings, value) => settings with { AbilitiesAffectTacticAllocation = value }),
                new ToggleOption("士气影响反击兵法发动率", "部队和设施反击兵法的入口概率加上当前士气−80",
                    settings => settings.MoraleAffectsTacticCounters,
                    (settings, value) => settings with { MoraleAffectsTacticCounters = value }),
            ]),
            new("联动",
            [
                new ToggleOption("智力兵法按亲密概率带动武力兵法", "武力首发候选按与智力发动者的相性、亲缘及关系独立筛选，不影响后续武力兵法联动。",
                    settings => settings.AffinityAffectsMartialChains,
                    (settings, value) => settings with { AffinityAffectsMartialChains = value }),
                new ToggleOption("箕形利于联动、雁行附加阻击", "按配置预设交换阵型的兵法联动倍率和阻击加成。原版为锥行的联动优势移给箕形、箕形的阻击优势移给雁行。",
                    settings => settings.RedistributeFormationTacticBonuses,
                    (settings, value) => settings with { RedistributeFormationTacticBonuses = value }, PresetValue: false),
            ]),
            new("效果",
            [
                new ToggleOption("智力兵法成功率修正", "成功率改为100+(6×(执行智力差−5)−识破修正)×7÷30。习得但未装备教谕、陷阱时，识破修正为86。",
                    settings => settings.AdjustIntelligenceTacticSuccess,
                    (settings, value) => settings with { AdjustIntelligenceTacticSuccess = value }),
                new ToggleOption("混乱威力影响持续时间", "混乱的智力项除以max(16, 40−威力)；熟练档大于1且执行智力至少30时，档位减1。只影响混乱本体。",
                    settings => settings.ConfusionPowerAffectsDuration,
                    (settings, value) => settings with { ConfusionPowerAffectsDuration = value }),
                new ToggleOption("君主都督交战时随机鼓舞", "主动交战时，主将为无伤的君主或都督且士气低于70，有4%几率自鼓舞。士气增加至少6，按当前四项能力之和÷24计算。",
                    settings => settings.LeadersInspireInCombat,
                    (settings, value) => settings with { LeadersInspireInCombat = value }),
                new ToggleOption("阻击概率修正", "熟练度平方项改为熟练度÷(40−2×阻击系数)，最多50；有效阵型加成大于0时减5。系数范围0～10、默认5。",
                    settings => settings.AdjustSnipingChance,
                    (settings, value) => settings with { AdjustSnipingChance = value },
                    new("系数", "仅在阻击概率修正开启时参与计算。范围0～10，默认5；熟练度除数为40−2×此值，默认除以30。",
                        0, 10, settings => settings.SnipingChanceCoefficient,
                        (settings, value) => settings with { SnipingChanceCoefficient = value })),
            ]),
            new("设施兵法",
            [
                new ToggleOption("港口、城塞连射，阵齐射", "按配置预设：港口、城塞发动连射，阵发动齐射。",
                    settings => settings.UseFacilityTacticPreset,
                    (settings, value) => settings with { UseFacilityTacticPreset = value }),
                new ToggleOption("减半设施智力兵法间隔", "设施发动智力兵法、鼓舞或治疗后，发动者的兵法间隔减半并向下取整。",
                    settings => settings.HalveFacilityIntelligenceCooldown,
                    (settings, value) => settings with { HalveFacilityIntelligenceCooldown = value }),
            ]),
            new("水军兵法",
            [
                new ToggleOption("艨艟可以发动箭雨", "近距离优先强袭，否则改用箭雨，包括攻击设施。",
                    settings => settings.EnableNavalTacticFallback,
                    (settings, value) => settings with { EnableNavalTacticFallback = value }),
                new ToggleOption("斗舰不能发动箭岚", "从斗舰的自动船型兵法中移除箭岚，保留强攻。不改变电脑选船策略。",
                    settings => settings.SuppressAdvancedNavalTactic,
                    (settings, value) => settings with { SuppressAdvancedNavalTactic = value }),
                new ToggleOption("调整水军兵法发动率", "未习得当前船只对应技术的首发武将，有一半几率失去本次兵法机会；不影响智力兵法带动的后续兵法。",
                    settings => settings.RequireNavalTacticTraining,
                    (settings, value) => settings with { RequireNavalTacticTraining = value }),
            ]),
        ]),
        new("战斗",
        [
            new("伤害与防御",
            [
                new ToggleOption("兵数与统率增加兵法伤害", "兵力至少一万时，按发动武将超过60的统率和当前兵力提高武力兵法伤害；设施按守军兵力计算。",
                    settings => settings.BoostMartialTacticDamage,
                    (settings, value) => settings with { BoostMartialTacticDamage = value }),
                new ToggleOption("少兵部队减少兵法伤害", "兵力不足一万时，武力兵法本体伤害乘以兵数平方根÷100；不改变普通攻击。",
                    settings => settings.ReduceSmallUnitTacticDamage,
                    (settings, value) => settings with { ReduceSmallUnitTacticDamage = value }),
                new NumericOption("武力兵法伤害系数", "武力兵法本体伤害乘以此数值÷10；范围5～10，10保持原值。",
                    5, 10, settings => settings.MartialTacticDamageCoefficient,
                    (settings, value) => settings with { MartialTacticDamageCoefficient = value }, "/10"),
                new ToggleOption("远程兵法攻击设施减伤", "弓骑、弩兵系兵法攻击设施时，耐久提供额外保护，最多减半；井阑的弩兵兵法不受此项影响。",
                    settings => settings.ReduceRangedTacticDamageAgainstFacilities,
                    (settings, value) => settings with { ReduceRangedTacticDamageAgainstFacilities = value }),
                new ToggleOption("提高耐久普防系数", "设施耐久计入普通防御的系数从0.4提高到0.6，不改变城墙自身受到的耐久伤害。",
                    settings => settings.IncreaseDurabilityDefense,
                    (settings, value) => settings with { IncreaseDurabilityDefense = value }),
                new ToggleOption("攻城熟练度影响器械攻城力", "配置预设的攻城阵型，对守军和耐久的普通攻击系数乘以1+(部队最高攻城熟练度−600)÷2048；不缩放武力兵法伤害。",
                    settings => settings.ProficiencyAffectsSiegePower,
                    (settings, value) => settings with { ProficiencyAffectsSiegePower = value }),
                new ToggleOption("减少战斗伤兵率", "战斗伤兵比例：部队乘10/13，设施乘10/12。",
                    settings => settings.ReduceBattleWounded,
                    (settings, value) => settings with { ReduceBattleWounded = value }),
                new NumericOption("交战内政损失倍率", "攻城交战造成的民心、商业、开垦损失倍率。默认100%，50%减半，0%不产生内政损失。",
                    0, int.MaxValue, settings => settings.CityBattleDamagePercent,
                    (settings, value) => settings with { CityBattleDamagePercent = value }, "%"),
            ]),
            new("俘获",
            [
                new ToggleOption("四维最高值计算俘获率", "开启后使用当前有效统率、武力、智力、政治的最高值计算个人俘获率，关闭时使用有效武力。",
                    settings => settings.UseHighestAbilityForCapture,
                    (settings, value) => settings with { UseHighestAbilityForCapture = value }),
                new NumericOption("无退路俘获加成", "无其他有效城市可退时，在常规俘获率上按百分点增加此数值，最终最多100%。默认100保持必俘，0按常规概率。",
                    0, 100, settings => settings.NoRetreatCaptureBonus,
                    (settings, value) => settings with { NoRetreatCaptureBonus = value }, "%", PresetValue: 0),
            ]),
        ]),
    ];

    private static readonly (AutomaticStrategyKind Kind, string Title, string Note)[] StrategyOptions =
    [
        (AutomaticStrategyKind.Alienate, "离间", "针对他方武将降低忠诚。"),
        (AutomaticStrategyKind.Gossip, "流言", "针对他方势力散布流言。"),
        (AutomaticStrategyKind.Burn, "烧夷", "破坏他方据点。"),
        (AutomaticStrategyKind.Seize, "夺取", "从他方据点夺取资源。"),
        (AutomaticStrategyKind.FalseReport, "伪报", "干扰敌方部队的行动。"),
        (AutomaticStrategyKind.Disorder, "扰乱", "使敌方部队陷入混乱。"),
        (AutomaticStrategyKind.Inspire, "激励", "恢复己方部队的士气。"),
        (AutomaticStrategyKind.Reinforce, "救援", "向己方部队派遣援兵。"),
    ];

    private static readonly (AutomaticDiplomacyKind Kind, string Title, string Note)[] DiplomacyOptions =
    [
        (AutomaticDiplomacyKind.Gift, "赠与", "自动向其他势力赠送资金、兵粮或宝物。只影响后续自动派遣，手动命令与在途任务不受影响。"),
        (AutomaticDiplomacyKind.RequestAttack, "请求", "自动请求其他势力出兵。只影响后续自动派遣，不影响手动请求、在途任务和已成立请求的履约。"),
        (AutomaticDiplomacyKind.ReturnPrisoner, "交还", "自动派遣使者请求交还本方俘虏。只影响后续自动派遣，手动命令与在途任务不受影响。"),
        (AutomaticDiplomacyKind.Surrender, "劝降", "自动劝说其他势力投降。只影响后续自动派遣，手动命令与在途任务不受影响。"),
    ];

    private static IEnumerable<ToggleOption> StrategyPermissions(bool delegated) => StrategyOptions.Select(option =>
        new ToggleOption(option.Title, option.Note,
            settings => (delegated ? settings.PlayerDelegatedStrategyPermissions : settings.ComputerStrategyPermissions).Allows(option.Kind),
            (settings, allowed) => delegated
                ? settings with { PlayerDelegatedStrategyPermissions = settings.PlayerDelegatedStrategyPermissions.WithAllowed(option.Kind, allowed) }
                : settings with { ComputerStrategyPermissions = settings.ComputerStrategyPermissions.WithAllowed(option.Kind, allowed) },
            EnabledText: "允许", DisabledText: "禁止",
            PresetValue: option.Kind is not (AutomaticStrategyKind.Alienate or AutomaticStrategyKind.Gossip or
                AutomaticStrategyKind.Burn or AutomaticStrategyKind.Seize)));

    private static IEnumerable<ToggleOption> DiplomacyPermissions(bool delegated) => DiplomacyOptions.Select(option =>
        new ToggleOption(option.Title, option.Note,
            settings => (delegated ? settings.PlayerDelegatedDiplomacyPermissions : settings.ComputerDiplomacyPermissions).Allows(option.Kind),
            (settings, allowed) => delegated
                ? settings with { PlayerDelegatedDiplomacyPermissions = settings.PlayerDelegatedDiplomacyPermissions.WithAllowed(option.Kind, allowed) }
                : settings with { ComputerDiplomacyPermissions = settings.ComputerDiplomacyPermissions.WithAllowed(option.Kind, allowed) },
            EnabledText: "允许", DisabledText: "禁止", PresetValue: option.Kind == AutomaticDiplomacyKind.ReturnPrisoner));

    private static ToggleOption[] CorpsOptions(bool delegated)
    {
        AutomaticCorpsOptions Read(CompanionSettings settings) => delegated
            ? settings.PlayerDelegatedCorpsOptions : settings.ComputerCorpsOptions;
        CompanionSettings Write(CompanionSettings settings, AutomaticCorpsOptions options) => delegated
            ? settings with { PlayerDelegatedCorpsOptions = options } : settings with { ComputerCorpsOptions = options };
        ToggleOption Option(string title, string note, Func<AutomaticCorpsOptions, bool> read,
            Func<AutomaticCorpsOptions, bool, AutomaticCorpsOptions> write) => new(title, note,
                settings => read(Read(settings)), (settings, allowed) => Write(settings, write(Read(settings), allowed)),
                EnabledText: "允许", DisabledText: "禁止");

        return
        [
            Option("重视内政方针允许征兵",
                "仅委任方针为“重视内政”时，允许在训练前自动征兵。一般委任自动进入内政模式不受此项影响；仍受征兵资格与资源约束。默认禁止。",
                options => options.DomesticDelegationRecruitment,
                (options, allowed) => options with { DomesticDelegationRecruitment = allowed }),
            Option("跨军团输送",
                "允许向同势力其他军团据点自动输送兵力。禁止时仍可在本军团内输送；手动命令和在途部队不受影响。默认允许。",
                options => options.CrossCorpsTransport,
                (options, allowed) => options with { CrossCorpsTransport = allowed }),
            Option("跨军团救援",
                "允许自动出兵增援同势力其他军团的受威胁据点。禁止时仍可增援本军团据点；不影响迎击、救援计略、手动命令或在途部队。默认允许。",
                options => options.CrossCorpsRelief,
                (options, allowed) => options with { CrossCorpsRelief = allowed }),
        ];
    }
}
