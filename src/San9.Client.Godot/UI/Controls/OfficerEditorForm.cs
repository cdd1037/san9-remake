using Godot;
using San9.Core.Domain;

namespace San9.Client.Godot;

/// <summary>One layout for catalog and created officers; callbacks own all draft changes.</summary>
internal static class OfficerEditorForm
{
    private static void Number(Node parent, string label, int value, int min, Action<int> changed, string name = "")
    {
        ContentEditorControls.Caption(parent, label);
        ContentEditorControls.NumericInput(parent, value, min, int.MaxValue, v => changed((int)v), name);
    }

    public static void BuildBasic(Node dialogOwner, VBoxContainer page, Func<OfficerEditorValues> read,
        Action<Func<OfficerEditorValues, OfficerEditorValues>> edit, TextureService? textures,
        Action<Node> flags, Action<Node> relationships, Action rebuild)
    {
        var p = read();
        page.AddThemeConstantOverride("separation", 12);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 18); page.AddChild(row);
        var avatar = ContentEditorControls.Section(row, "头像");
        avatar.CustomMinimumSize = new(160, 0); avatar.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        var portrait = new PanelContainer();
        portrait.AddThemeStyleboxOverride("panel", ContentEditorStyle.Surface("151c24", "465260", 6)); avatar.AddChild(portrait);
        portrait.AddChild(OfficerPortraitPicker.Preview(textures, p.PortraitKey, new(148, 148)));
        if (p.PortraitKey is { } portraitKey && textures is not null &&
            textures.Portraits.TryGetValue(portraitKey, out var selectedPortrait) && selectedPortrait.DisplayName is { } portraitName)
            avatar.AddChild(new Label { Text = portraitName, TooltipText = portraitName,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore });
        var actions = new HBoxContainer(); actions.AddThemeConstantOverride("separation", 6); avatar.AddChild(actions);
        var choose = ContentEditorControls.ActionButton(actions, "选择", () =>
        {
            if (textures is null) return;
            OfficerPortraitPicker.Show(dialogOwner, textures, read().PortraitKey, key =>
            { edit(v => v with { PortraitKey = key }); rebuild(); });
        }, "ChoosePortrait");
        choose.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        choose.Disabled = textures is null || textures.Portraits.Count == 0;
        ContentEditorControls.ActionButton(actions, "清除", () =>
        { edit(v => v with { PortraitKey = null }); rebuild(); }, "ClearPortrait");
        flags(avatar);
        var details = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        details.AddThemeConstantOverride("separation", 10); row.AddChild(details);
        var identity = ContentEditorControls.ProfileGrid(details, "身份");
        OfficerEditorFields.Names(identity, p.Surname, p.GivenName, p.CourtesyName,
            v => edit(x => x with { Surname = v }), v => edit(x => x with { GivenName = v }),
            v => edit(x => x with { CourtesyName = v }));
        ContentEditorControls.Choices(identity, "性别", ["男", "女"], p.IsFemale ? 1 : 0,
            v => edit(x => x with { IsFemale = v == 1 }));
        OfficerEditorFields.Abilities(details, p.Abilities, change => edit(x => x with { Abilities = change(x.Abilities) }), 0,
            () => { edit(x => x with { Abilities = new(100, 100, 100, 100) }); rebuild(); });
        OfficerEditorFields.Personality(details, p.Personality, p.Righteousness, p.Ambition, p.Affinity,
            v => edit(x => x with { Personality = v }), v => edit(x => x with { Righteousness = v }),
            v => edit(x => x with { Ambition = v }), v => edit(x => x with { Affinity = v }));
        var life = ContentEditorControls.ProfileGrid(details, "生平与关系", header =>
        {
            var relativeAge = ContentEditorControls.Toggle(header, "相对年龄", p.AgeAtScenarioStart is not null, enabled =>
            {
                edit(x => x with { AgeAtScenarioStart = enabled ? 20 : null, BirthYear = enabled ? 0 : 170 }); rebuild();
            }, "RelativeAge", "开启后，以开局年份减去“年龄差”计算出生年。");
            ContentEditorStyle.Compact(relativeAge);
        });
        if (p.AgeAtScenarioStart is int age)
            Number(life, "年龄差", age, 0, v => edit(x => x with { AgeAtScenarioStart = v }));
        else Number(life, "出生年", p.BirthYear, 1, v => edit(x => x with { BirthYear = v }), "BirthYear");
        Number(life, "登场年", p.AppearanceYear, 1, v => edit(x => x with { AppearanceYear = v }), "AppearanceYear");
        relationships(life);
        Number(life, "寿命档", p.LifespanTier, 0, v => edit(x => x with { LifespanTier = v }));
        ContentEditorControls.TextArea(page, "列传", p.Biography, v => edit(x => x with { Biography = v }), "OfficerBiography");
    }

    public static void BuildTraining(VBoxContainer page, Func<OfficerEditorValues> read,
        Action<Func<OfficerEditorValues, OfficerEditorValues>> edit, IReadOnlyList<OfficerTacticChoice> tactics, int proficiencyCap)
    {
        var p = read();
        OfficerEditorFields.Training(page, () => read().Training, v => edit(x => x with { Training = v }), tactics, proficiencyCap);
        var bonus = ContentEditorControls.ProfileGrid(page, "兵法加成");
        ContentEditorControls.Caption(bonus, "连携加成");
        ContentEditorControls.NumericInput(bonus, p.TacticLinkBonus, 0, 100, v => edit(x => x with { TacticLinkBonus = (int)v }));
    }

    public static void BuildTraits(VBoxContainer page, OfficerEditorValues p, Action<Func<OfficerEditorValues, OfficerEditorValues>> edit)
    {
        var succession = ContentEditorControls.ProfileGrid(page, "继承与拥立");
        Number(succession, "继承优先级", p.SuccessionPriority, 0, v => edit(x => x with { SuccessionPriority = v }));
        Number(succession, "拥立优先级", p.ForceFounderPriority, 0, v => edit(x => x with { ForceFounderPriority = v }));
        var options = ContentEditorControls.ButtonGroup(page, "人物特性");
        ContentEditorControls.Toggle(options, "优先成为合并势力君主", p.PrefersConsolidationLeadership,
            v => edit(x => x with { PrefersConsolidationLeadership = v }));
        ContentEditorControls.Toggle(options, "允许自动投降", p.AutomaticSurrenderAllowed,
            v => edit(x => x with { AutomaticSurrenderAllowed = v }));
        ContentEditorControls.Toggle(options, "登用政治加成", p.HasRecruitmentPoliticsBonus,
            v => edit(x => x with { HasRecruitmentPoliticsBonus = v }));
        foreach (var flag in Enum.GetValues<DuelAdvantages>().Where(f => f != 0 && ((int)f & ((int)f - 1)) == 0))
        {
            string label = flag switch { DuelAdvantages.DominantParry => "单挑格挡优势",
                DuelAdvantages.DominantAttack => "单挑攻击优势", DuelAdvantages.DominantDoubleStrike => "单挑双击优势",
                _ => throw new ArgumentOutOfRangeException(nameof(flag)) };
            ContentEditorControls.Toggle(options, label, p.DuelAdvantages.HasFlag(flag), v => edit(x => x with
            { DuelAdvantages = v ? x.DuelAdvantages | flag : x.DuelAdvantages & ~flag }));
        }
    }
}
