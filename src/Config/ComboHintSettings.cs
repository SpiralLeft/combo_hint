using System;
using System.Collections.Generic;
using System.IO;
using BaseLib.Config;
using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;

namespace ComboHint;

[ConfigHoverTipsByDefault]
public sealed class ComboHintSettings : SimpleModConfig
{
    public static bool EnableOverlay { get; set; } = true;
    public static bool EnableSinglePlayer { get; set; } = true;
    public static bool EnableBubbles { get; set; } = true;
    private static double _bubbleDurationSeconds = 3;
    [ConfigSlider(0.3, 10, 0.1, Format = "{0:0.0}")]
    public static double BubbleDurationSeconds
    {
        get => _bubbleDurationSeconds;
        set => _bubbleDurationSeconds = double.IsFinite(value) ? Math.Clamp(value, 0.3, 10) : 3;
    }
    [ConfigSection("DebuffHints", CollapsedByDefault = true)]
    public static bool EnableWeak { get; set; } = true;
    public static bool EnableVulnerable { get; set; } = true;
    public static bool EnableWeakening { get; set; } = true;
    public static bool EnableExpose { get; set; } = true;
    public static bool EnableDebilitate { get; set; } = true;
    public static bool EnableDislikeYou { get; set; } = true;
    public static bool EnablePoison { get; set; } = false;
    public static bool EnableDoom { get; set; } = false;
    [ConfigHideInUI]
    public static bool EnableStrangle { get; set; } = true; // Retained only for old config compatibility.
    public static bool EnableStun { get; set; } = true;
    public static bool EnableMultiplayer { get; set; } = true;
    [ConfigHideInUI, ConfigIgnoreRestoreDefaults]
    public static bool LegacySettingsMigrated { get; set; }
    [ConfigHideInUI, ConfigIgnoreRestoreDefaults]
    public static bool BubbleDurationMigrated { get; set; }

    public void MigrateLegacySettings(string modRoot)
    {
        try
        {
            if (!BubbleDurationMigrated)
            {
                BubbleDurationSeconds = ModEntry.Rules.BubbleDurationSeconds;
                BubbleDurationMigrated = true;
                Save();
            }
            if (LegacySettingsMigrated) return;
            string path = Path.Combine(modRoot, "combo_hint.json");
            if (!File.Exists(path)) path = Path.Combine(modRoot, "ComboHint.json");
            if (File.Exists(path))
            {
                var legacy = LegacySettings.Parse(File.ReadAllText(path));
                EnableOverlay = legacy.Overlay ?? EnableOverlay;
                EnableSinglePlayer = legacy.SinglePlayer ?? EnableSinglePlayer;
                EnableBubbles = legacy.Bubbles ?? EnableBubbles;
            }
            LegacySettingsMigrated = true;
            Save();
        }
        catch (Exception ex)
        {
            ModEntry.Logger.Warn($"Could not migrate legacy settings; will retry next launch: {ex.Message}");
        }
    }

    public override void SetupConfigUI(Control optionContainer)
    {
        // Register DLL-local strings again when reopening the page after a language change.
        var table = LocManager.Instance.GetTable("settings_ui");
        var text = new Dictionary<string, string>();
        Add(text, "ENABLE_OVERLAY", "连携提示框", "Combo Hint Overlay", "启用右上角连携提示框。", "Show the combo hint overlay in combat.");
        Add(text, "ENABLE_SINGLE_PLAYER", "单人连携", "Single-Player Hints", "在单人模式中启用提示框和气泡。", "Enable the overlay and bubbles in single-player mode.");
        Add(text, "ENABLE_BUBBLES", "气泡提示", "Speech Bubbles", "手牌新增连携效果时显示角色气泡。", "Show a speech bubble when new combo effects enter the hand.");
        Add(text, "BUBBLE_DURATION_SECONDS", "气泡显示时间", "Bubble Duration", "调整气泡显示时间，范围为 0.3～10 秒。", "Set how long speech bubbles remain visible, from 0.3 to 10 seconds.");
        text[ModPrefix + "BUBBLE_DURATION_SECONDS.sliderFormat"] = HintText.IsChinese ? "{0:0.0} 秒" : "{0:0.0} s";
        text[ModPrefix + "DEBUFF_HINTS.title"] = HintText.IsChinese ? "效果提示" : "Effect Hints";
        foreach (var (key, id, zh, en) in new[]
        {
            ("WEAK", "WEAK_POWER", "虚弱", "Weak"), ("VULNERABLE", "VULNERABLE_POWER", "易伤", "Vulnerable"),
            ("WEAKENING", "WEAKENING", "弱化", "Enfeeblement"), ("EXPOSE", "EXPOSE", "暴露", "Expose"),
            ("DEBILITATE", "DEBILITATE_POWER", "摧残", "Debilitate"),
            ("DISLIKE_YOU", "DISLIKE_YOU_POWER", "不喜欢你", "Dislike You"),
            ("POISON", "POISON_POWER", "中毒", "Poison"), ("DOOM", "DOOM_POWER", "灾厄", "Doom"),
            ("STUN", "STUN", "击晕", "Stun")
        })
        {
            string title = id switch
            {
                "WEAK_POWER" => ModelDb.Power<WeakPower>().Title.GetFormattedText(),
                "VULNERABLE_POWER" => ModelDb.Power<VulnerablePower>().Title.GetFormattedText(),
                "DEBILITATE_POWER" => ModelDb.Power<DebilitatePower>().Title.GetFormattedText(),
                "DISLIKE_YOU_POWER" => ModelDb.Power<DislikeYouPower>().Title.GetFormattedText(),
                "POISON_POWER" => ModelDb.Power<PoisonPower>().Title.GetFormattedText(),
                "DOOM_POWER" => ModelDb.Power<DoomPower>().Title.GetFormattedText(),
                "STUN" => StunIntent.GetStaticHoverTip().Title!,
                _ => HintText.IsChinese ? zh : en
            };
            Add(text, "ENABLE_" + key, title, title,
                "在提示框和气泡中显示“" + title + "”。", "Show " + title + " in the overlay and speech bubbles.");
        }
        Add(text, "ENABLE_MULTIPLAYER", "多人卡牌", "Multiplayer Cards",
            "在提示框和气泡中显示多人协同卡牌，悬停查看卡牌。", "Show multiplayer synergy cards in the overlay and speech bubbles, with card previews on hover.");
        table.MergeWith(text);
        base.SetupConfigUI(optionContainer);
    }

    public static bool IsEffectEnabled(string id) => id switch
    {
        "WEAK_POWER" => EnableWeak, "VULNERABLE_POWER" => EnableVulnerable,
        "WEAKENING" => EnableWeakening, "EXPOSE" => EnableExpose,
        "DEBILITATE_POWER" => EnableDebilitate, "STRANGLE_POWER" => false,
        "DISLIKE_YOU_POWER" => EnableDislikeYou,
        "POISON_POWER" => EnablePoison, "DOOM_POWER" => EnableDoom,
        "STUN" => EnableStun,
        _ => true
    };

    private void Add(Dictionary<string, string> entries, string key, string zh, string en, string zhDescription, string enDescription)
    {
        entries[ModPrefix + key + ".title"] = HintText.IsChinese ? zh : en;
        entries[ModPrefix + key + ".hover.desc"] = HintText.IsChinese ? zhDescription : enDescription;
    }
}
