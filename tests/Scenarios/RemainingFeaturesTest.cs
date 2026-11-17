using BaseLib.Config;
using BaseLib.Config.UI;
using ComboHint;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Events;

namespace ComboHintScenarioTests;

public static class RemainingFeaturesTest
{
    private static HintContent Content(params CardModel[] cards) => HintContent.Build(
        new HintMatcher(ModEntry.Rules).MatchCards(cards.Select(HintText.Describe)), ModEntry.Rules, cards);
    private static CardModel Card(string id) => (CardModel)ModelDb.AllCards.Single(c => c.Id.Entry == id).MutableClone();
    private static MadScience Mad(TinkerTime.RiderEffect rider, CardType type = CardType.Attack)
    {
        var card = (MadScience)ModelDb.Card<MadScience>().MutableClone();
        card.TinkerTimeType = type;
        card.TinkerTimeRider = rider;
        return card;
    }
    public static void Verify()
    {
        CharacterEffectsTest.Verify();
        VerifyMultiplayerToggle();
        foreach (var (property, source, key) in new[]
        {
            ("EnableWeak", "SHOCKWAVE", "WEAK_POWER"), ("EnableVulnerable", "SHOCKWAVE", "VULNERABLE_POWER"),
            ("EnableWeakening", "MALAISE", "WEAKENING"), ("EnableExpose", "EXPOSE", "EXPOSE"),
            ("EnableDebilitate", "DEBILITATE", "DEBILITATE_POWER"),
            ("EnableDislikeYou", "DISLIKE_YOU", "DISLIKE_YOU_POWER"),
            ("EnablePoison", "HAZE", "POISON_POWER"), ("EnableDoom", "DEATHBRINGER", "DOOM_POWER"),
            ("EnableStun", "WHISTLE", "STUN")
        })
        {
            var setting = typeof(ComboHintSettings).GetProperty(property)!;
            bool original = (bool)setting.GetValue(null)!;
            try
            {
                setting.SetValue(null, true);
                var before = Content(Card(source));
                ScenarioTestMod.Require(before.HoverTips.ContainsKey(key), "Missing enabled effect " + key);
                setting.SetValue(null, false);
                var after = Content(Card(source));
                ScenarioTestMod.Require(!after.HoverTips.ContainsKey(key)
                    && before.HoverTips.Count == after.HoverTips.Count + 1,
                    "Independent effect toggle removed wrong effects: " + property);
                ScenarioTestMod.Require(after.VisibleTriggers.Count == (after.Text.Length > 0 ? 1 : 0),
                    "Hidden effects must not contribute bubble triggers: " + property);
                ScenarioTestMod.Require(Content(Card("LIFT")).HoverTips.Count == 1 && Content(Card("FEED")).Text.Length > 0,
                    "Effect toggle changed multiplayer or lethal hints.");
            }
            finally { setting.SetValue(null, original); }
        }
        var sapping = Mad(TinkerTime.RiderEffect.Sapping);
        var choking = Mad(TinkerTime.RiderEffect.Choking);
        var sap = Content(sapping);
        ScenarioTestMod.Require(sap.HoverTips.Keys.Order().SequenceEqual(new[] { "VULNERABLE_POWER", "WEAK_POWER" }),
            "Sapping must apply Weak and Vulnerable.");
        var choke = Content(choking);
        ScenarioTestMod.Require(choke.Text.Length == 0 && choke.VisibleTriggers.Count == 0,
            "Choking only responds to the caster and must not produce shared hints.");
        foreach (var cards in new[] { new CardModel[] { sapping, choking }, new CardModel[] { choking, sapping } })
            ScenarioTestMod.Require(Content(cards).HoverTips.Count == 2 && Content(cards).VisibleTriggers.Count == 1,
                "Different same-ID variants must coexist in either hand order.");
        foreach (var rider in Enum.GetValues<TinkerTime.RiderEffect>())
        foreach (var type in new[] { CardType.Attack, CardType.Skill, CardType.Power })
        {
            var card = Mad(rider, type);
            bool expected = type == CardType.Attack && rider == TinkerTime.RiderEffect.Sapping;
            ScenarioTestMod.Require((Content(card).Text.Length > 0) == expected, "Unexpected dynamic match " + type + "/" + rider);
            card.UpgradeInternal(); card.FinalizeUpgradeInternal();
            ScenarioTestMod.Require((Content(card).Text.Length > 0) == expected, "Upgrade changed dynamic identification.");
        }
        ScenarioTestMod.Require(Content(Card("STRANGLE"), choking).HoverTips.Count == 0
            && Content(sapping, Card("SHOCKWAVE")).HoverTips.Count == 2, "Static and dynamic effects must deduplicate.");
        // Use BaseLib's real persistence and UI generation in the isolated user directory.
        var config = ModConfigRegistry.Get<ComboHintSettings>()!;
        bool weak = ComboHintSettings.EnableWeak, multiplayer = ComboHintSettings.EnableMultiplayer;
        bool poison = ComboHintSettings.EnablePoison, doom = ComboHintSettings.EnableDoom, dislike = ComboHintSettings.EnableDislikeYou;
        double duration = ComboHintSettings.BubbleDurationSeconds;
        bool migrated = ComboHintSettings.LegacySettingsMigrated;
        bool durationMigrated = ComboHintSettings.BubbleDurationMigrated;
        try
        {
            ComboHintSettings.EnableWeak = false;
            ComboHintSettings.BubbleDurationSeconds = 4.5;
            ComboHintSettings.EnablePoison = ComboHintSettings.EnableDoom = true;
            ComboHintSettings.EnableDislikeYou = false;
            ComboHintSettings.EnableMultiplayer = false;
            config.Save();
            ComboHintSettings.EnablePoison = ComboHintSettings.EnableDoom = false;
            ComboHintSettings.EnableDislikeYou = true;
            ComboHintSettings.EnableMultiplayer = true;
            ComboHintSettings.EnableWeak = true;
            ComboHintSettings.BubbleDurationSeconds = 1;
            config.Load();
            ScenarioTestMod.Require(!ComboHintSettings.EnableWeak && ComboHintSettings.EnablePoison && ComboHintSettings.EnableDoom
                && !ComboHintSettings.EnableDislikeYou && !ComboHintSettings.EnableMultiplayer,
                "BaseLib did not persist independent effect toggles.");
            ScenarioTestMod.Require(ComboHintSettings.BubbleDurationSeconds == 4.5, "BaseLib did not persist bubble duration.");
            using var container = new VBoxContainer();
            container.Visible = false;
            MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance!.Ui.AddChild(container);
            try
            {
                config.SetupConfigUI(container);
                var topRows = container.GetChildren().OfType<NConfigOptionRow>().ToArray();
                ScenarioTestMod.Require(topRows.Select(r => r.Name.ToString()).SequenceEqual(new[]
                    { "EnableOverlay", "EnableSinglePlayer", "EnableBubbles", "BubbleDurationSeconds" }),
                    "Only general settings and duration should be top-level rows.");
                var group = container.GetChildren().OfType<NConfigCollapsibleSection>().Single();
                ScenarioTestMod.Require(!group.IsExpanded, "Effect settings should start collapsed.");
                var effectRows = group.ContentContainer.GetChildren().OfType<NConfigOptionRow>().ToArray();
                ScenarioTestMod.Require(effectRows.Select(r => r.Name.ToString()).SequenceEqual(new[]
                    { "EnableWeak", "EnableVulnerable", "EnableWeakening", "EnableExpose", "EnableDebilitate", "EnableDislikeYou", "EnablePoison", "EnableDoom", "EnableStun", "EnableMultiplayer" }),
                    "Only implemented effects belong inside the effect section.");
                foreach (var row in effectRows)
                {
                    var label = row.GetChildren().OfType<RichTextLabel>().Single();
                    var tip = ScenarioTestMod.Field<HoverTip>(row, "_hoverTip");
                    ScenarioTestMod.Require(!label.Text.Contains('[') && tip.Title == label.Text
                        && !tip.Description.Contains("[color", StringComparison.OrdinalIgnoreCase),
                        "Setting label/tooltip must not contain color tags: " + row.Name);
                }
                var multiplayerLabel = effectRows.Single(r => r.Name == "EnableMultiplayer").GetChildren().OfType<RichTextLabel>().Single();
                ScenarioTestMod.Require(multiplayerLabel.Text == (HintText.IsChinese ? "多人卡牌" : "Multiplayer Cards"),
                    "Missing localized multiplayer setting.");
                foreach (var (property, id, source) in new[]
                {
                    ("EnableWeak", "WEAK_POWER", "SHOCKWAVE"),
                    ("EnableVulnerable", "VULNERABLE_POWER", "SHOCKWAVE"),
                    ("EnableWeakening", "WEAKENING", "MALAISE"),
                    ("EnableExpose", "EXPOSE", "EXPOSE"),
                    ("EnableDebilitate", "DEBILITATE_POWER", "DEBILITATE"),
                    ("EnableDislikeYou", "DISLIKE_YOU_POWER", "DISLIKE_YOU"),
                    ("EnablePoison", "POISON_POWER", "HAZE"), ("EnableDoom", "DOOM_POWER", "DEATHBRINGER"),
                    ("EnableStun", "STUN", "WHISTLE")
                })
                {
                    var label = effectRows.Single(r => r.Name == property).GetChildren().OfType<RichTextLabel>().Single();
                    // Enable this effect temporarily so its menu label can be compared with combat.
                    var setting = typeof(ComboHintSettings).GetProperty(property)!;
                    bool enabled = (bool)setting.GetValue(null)!;
                    setting.SetValue(null, true);
                    try
                    {
                        var content = Content(Card(source));
                        string title = ((HoverTip)content.HoverTips[id]).Title!;
                        ScenarioTestMod.Require(label.Text == title && content.Text.Contains(HintText.Colored(title, ModEntry.Rules.EffectColor(id))),
                            "Menu name must be plain text while combat retains its color: " + property);
                    }
                    finally { setting.SetValue(null, enabled); }
                }
                var slider = (NConfigSlider)topRows.Single(r => r.Name == "BubbleDurationSeconds").SettingControl;
                slider.GetNode<Godot.Range>("Slider").Value = 5.2; // BaseLib's slider uses a proxy offset by MinValue (0.3).
                ScenarioTestMod.Require(ComboHintSettings.BubbleDurationSeconds == 5.5, "Actual slider did not update duration.");
                ComboHintSettings.BubbleDurationSeconds = double.NaN;
                ScenarioTestMod.Require(ComboHintSettings.BubbleDurationSeconds == 3, "Invalid duration must recover to default.");
                ComboHintSettings.BubbleDurationSeconds = 100;
                ScenarioTestMod.Require(ComboHintSettings.BubbleDurationSeconds == 10, "Duration must respect its upper bound.");
                ComboHintSettings.BubbleDurationSeconds = -1;
                ScenarioTestMod.Require(ComboHintSettings.BubbleDurationSeconds == 0.3, "Duration must respect its lower bound.");
            }
            finally { container.Free(); }
            typeof(SimpleModConfig).GetMethod("RestoreDefaultsNoConfirm", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(config, null);
            ScenarioTestMod.Require(ComboHintSettings.EnableWeak && ComboHintSettings.LegacySettingsMigrated == migrated
                && ComboHintSettings.BubbleDurationSeconds == 3 && ComboHintSettings.BubbleDurationMigrated == durationMigrated
                && !ComboHintSettings.EnablePoison && !ComboHintSettings.EnableDoom && ComboHintSettings.EnableDislikeYou
                && ComboHintSettings.EnableMultiplayer,
                "Restore defaults must keep opt-in effects disabled without reimporting legacy settings.");
        }
        finally
        {
            ComboHintSettings.EnableWeak = weak; ComboHintSettings.EnableMultiplayer = multiplayer;
            ComboHintSettings.BubbleDurationSeconds = duration;
            ComboHintSettings.EnablePoison = poison; ComboHintSettings.EnableDoom = doom; ComboHintSettings.EnableDislikeYou = dislike;
            config.Save();
        }
    }
    private static void VerifyMultiplayerToggle()
    {
        bool original = ComboHintSettings.EnableMultiplayer;
        ScenarioTestMod.Require(original, "Multiplayer hints should default to enabled.");
        try
        {
            foreach (string id in ModEntry.Rules.Groups.Single(g => g.Key == "multiplayer").TriggerModelIds)
            {
                var source = Card(id);
                foreach (bool upgrade in new[] { false, true })
                {
                    if (upgrade) { source.UpgradeInternal(); source.FinalizeUpgradeInternal(); }
                    ComboHintSettings.EnableMultiplayer = false;
                    var hidden = Content(source);
                    ScenarioTestMod.Require(hidden.Text.Length == 0 && hidden.HoverTips.Count == 0 && hidden.VisibleTriggers.Count == 0,
                        "Disabled multiplayer card leaked an effect or bubble trigger: " + id);
                    var mixed = Content(source, Card("BASH"), Card("FEED"));
                    ScenarioTestMod.Require(mixed.HoverTips.Keys.SequenceEqual(new[] { "VULNERABLE_POWER" })
                        && mixed.Text.Contains(HintText.KillTitle), "Multiplayer toggle suppressed unrelated effects: " + id);
                    ComboHintSettings.EnableMultiplayer = true;
                    var shown = Content(source);
                    ScenarioTestMod.Require(shown.HoverTips.Single().Value is CardHoverTip preview && ReferenceEquals(preview.Card, source)
                        && shown.VisibleTriggers.Count == 1, "Enabled multiplayer card lost its preview: " + id);
                }
            }
        }
        finally { ComboHintSettings.EnableMultiplayer = original; }
    }

}
