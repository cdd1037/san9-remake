using Godot;
using San9.Client.Godot.Settings;
using San9.Core.Domain;

namespace San9.Client.Godot;

public partial class WorkflowLifecycleVerification
{
    private void CheckCompanionDraft()
    {
        CompanionConfiguration? saved = null;
        int writes = 0, closes = 0;
        bool failSave = false;
        var view = new CompanionSettingsView
        {
            SaveAndApply = settings =>
            {
                if (failSave) throw new IOException("injected save failure");
                saved = settings;
                writes++;
            },
            Closed = () => closes++,
        };
        AddChild(view);
        try
        {
            var buttons = view.FindChildren("*", "Button", true, false).OfType<Button>().ToArray();
            var numbers = view.FindChildren("*", "SpinBox", true, false).OfType<SpinBox>().ToArray();
            var revenue = numbers.Single(n => n.TooltipText == "普通电脑势力的季度资金收入倍率。");
            var harvest = numbers.Single(n => n.TooltipText == "普通电脑势力的七月兵粮收获倍率。");
            var battle = numbers.Single(n => n.TooltipText.StartsWith("攻城交战造成的民心", StringComparison.Ordinal));
            var preset = buttons.Single(b => b.Text == "预设配置");
            var reset = buttons.Single(b => b.Text == "原版配置");
            var toggle = buttons.Single(b => b.TooltipText.StartsWith("玩家可向子军团下令", StringComparison.Ordinal));

            view.Back();
            Check(writes == 0 && closes == 1, "Unchanged companion settings were saved.");
            revenue.GetLineEdit().Text = "175";
            harvest.GetLineEdit().Text = "225";
            toggle.ButtonPressed = true;
            failSave = true;
            view.Back();
            Check(writes == 0 && closes == 1, "A failed companion save closed the draft.");
            failSave = false;
            view.Back();
            Check(saved?.Rules is { ComputerRevenuePercent: 175, ComputerHarvestPercent: 225, AllowDelegatedCorpsOrders: true },
                "Editing another control or applying numeric fields lost pending companion input.");

            battle.GetLineEdit().Text = "75";
            preset.EmitSignal(BaseButton.SignalName.Pressed);
            view.Back();
            Check(saved?.Rules is { ComputerRevenuePercent: 200, ComputerHarvestPercent: 200, OfficerAbilityCap: 125,
                NoRetreatCaptureBonus: 0, CityBattleDamagePercent: 75, DisableComputerPrisonerExecution: true,
                DisablePlayerPrisonerExecution: true, AllowDelegatedCorpsOrders: true,
                ConfigureTacticsOnFormationSelection: true, BattleDeathProtectionThreshold: 125,
                AllowCommanderBattleDeaths: true, DuelDeathCoefficient: 0, InfiniteLifespan: true, RedistributeFormationTacticBonuses: false } &&
                saved.Rules.ComputerStrategyPermissions is { Alienate: false, Burn: false, FalseReport: true } &&
                saved.Rules.PlayerDelegatedStrategyPermissions == saved.Rules.ComputerStrategyPermissions &&
                saved.Rules.ComputerDiplomacyPermissions is { Gift: false, RequestAttack: false, ReturnPrisoner: true, Surrender: false } &&
                saved.Rules.PlayerDelegatedDiplomacyPermissions == saved.Rules.ComputerDiplomacyPermissions,
                $"The companion preset discarded a retained numeric draft or applied incorrect policies: {saved}.");
            Check(saved?.FormationAppearanceEnabled == true, "The companion preset did not enable formation appearance.");

            reset.EmitSignal(BaseButton.SignalName.Pressed);
            view.Back();
            Check(writes == 2 && closes == 4, "Restoring the original settings wrote an unchanged draft.");
            var appearance = (Button)view.FindChild("FormationAppearance", true, false);
            Check(!appearance.ButtonPressed, "Original settings did not reset formation appearance.");
            appearance.ButtonPressed = true;
            view.Back();
            Check(writes == 3 && closes == 5 && saved == new CompanionConfiguration(CompanionSettings.Default, true),
                "A visual-only draft must save without changing simulation settings.");
            var computerGift = buttons.Single(b => b.TooltipText.StartsWith("电脑势力：自动向其他势力赠送", StringComparison.Ordinal));
            var delegatedReturn = buttons.Single(b => b.TooltipText.StartsWith("玩家势力AI：自动派遣使者请求交还", StringComparison.Ordinal));
            computerGift.ButtonPressed = false;
            delegatedReturn.ButtonPressed = false;
            view.Back();
            Check(saved?.Rules.ComputerDiplomacyPermissions is { Gift: false, RequestAttack: true, ReturnPrisoner: true, Surrender: true } &&
                saved.Rules.PlayerDelegatedDiplomacyPermissions is { Gift: true, RequestAttack: true, ReturnPrisoner: false, Surrender: true },
                "Diplomacy editors changed the wrong command or control scope.");
            GD.Print("COMPANION_DRAFT_OK pending-input,preset,reset,save-failure");
        }
        finally { view.Free(); }
    }
}
