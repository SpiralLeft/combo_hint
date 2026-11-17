using System.Reflection;
using System.Text.Json;
using ComboHint;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace ComboHintScenarioTests;

public static class BubbleEffectsTest
{
    private static NHoverTipSet? _previousTip;
    private static MegaRichTextLabel? _previousLabel;
    private static CardModel Card(string id) => (CardModel)ModelDb.AllCards.Single(c => c.Id.Entry == id).MutableClone();
    private static HintContent Content(params CardModel[] cards) => HintContent.Build(
        new HintMatcher(ModEntry.Rules).MatchCards(cards.Select(HintText.Describe)), ModEntry.Rules, cards);
    public static void VerifyAndExport()
    {
        bool poison = ComboHintSettings.EnablePoison, doom = ComboHintSettings.EnableDoom;
        try
        {
            ComboHintSettings.EnablePoison = ComboHintSettings.EnableDoom = true;
            VerifyEnabledAndExport();
        }
        finally { ComboHintSettings.EnablePoison = poison; ComboHintSettings.EnableDoom = doom; }
    }
    private static void VerifyEnabledAndExport()
    {
        var matcher = new HintMatcher(ModEntry.Rules);
        var records = new List<object>();
        foreach (string excluded in new[] { "STRANGLE", "MONARCHS_GAZE" })
        {
            var source = Card(excluded);
            foreach (bool upgrade in new[] { false, true })
            {
                if (upgrade) { source.UpgradeInternal(); source.FinalizeUpgradeInternal(); }
                var hidden = Content(source);
                ScenarioTestMod.Require(hidden.Text.Length == 0 && hidden.HoverTips.Count == 0 && hidden.VisibleTriggers.Count == 0,
                    "Caster-only source must not enter either hint surface: " + excluded);
            }
        }
        foreach (var (cardId, powers) in new (string, PowerModel[])[]
        {
            ("DEBILITATE", new[] { ModelDb.Power<DebilitatePower>() }),
            ("DISLIKE_YOU", new[] { ModelDb.Power<DislikeYouPower>() }),
            ("SHOCKWAVE", new PowerModel[] { ModelDb.Power<WeakPower>(), ModelDb.Power<VulnerablePower>() }),
            ("HAZE", new PowerModel[] { ModelDb.Power<PoisonPower>(), ModelDb.Power<WeakPower>() }),
            ("DEATHBRINGER", new PowerModel[] { ModelDb.Power<DoomPower>(), ModelDb.Power<WeakPower>() })
        })
        {
            var source = (CardModel)ModelDb.AllCards.Single(c => c.Id.Entry == cardId).MutableClone();
            var content = Content(source);
            ScenarioTestMod.Require(content.HoverTips.Count == powers.Length && !content.Text.Contains("SOURCE CARD"), "Wrong effect mapping: " + cardId);
            foreach (var power in powers)
            {
                string? variable = cardId switch { "KNOCKDOWN" => "KnockdownPower", "DEBILITATE" => "DebilitatePower", "DISLIKE_YOU" => "DislikeYouPower", _ => null };
                var expected = (HoverTip)HoverTipFactory.FromPower(power, variable == null ? null : source.DynamicVars[variable].IntValue);
                if (power is DebilitatePower or DislikeYouPower)
                {
                    var description = power.SmartDescription;
                    description.Add("Amount", source.DynamicVars[variable!].IntValue);
                    expected = new HoverTip(power, description.GetFormattedText(), true);
                }
                var tip = (HoverTip)content.HoverTips[power.Id.Entry];
                ScenarioTestMod.Require(tip.Title == expected.Title && tip.Description == expected.Description
                    && !string.IsNullOrWhiteSpace(tip.Description) && tip.Icon != null, "Native effect data mismatch: " + power.Id);
                ScenarioTestMod.Require(content.Text.Contains("[url=" + power.Id.Entry + "]"), "Missing effect link.");
                records.Add(new { card = cardId, power = power.Id.Entry, tip.Title, tip.Description });
            }
            if (cardId is "KNOCKDOWN" or "DEBILITATE" or "DISLIKE_YOU")
            {
                source.UpgradeInternal(); source.FinalizeUpgradeInternal();
                var upgraded = Content(source);
                var tip = (HoverTip)upgraded.HoverTips.Single().Value;
                ScenarioTestMod.Require(!tip.Description.Equals(((HoverTip)content.HoverTips.Single().Value).Description)
                    && tip.Description.Contains(cardId == "DISLIKE_YOU" ? "75" : "3"), "Upgrade-specific effect description was lost: " + cardId
                    + " raw=" + powers.Single().Description.GetRawText() + " smart=" + powers.Single().SmartDescription.GetRawText()
                    + " actual=" + tip.Description);
                records.Add(new { card = cardId + "+", power = powers.Single().Id.Entry, tip.Title, tip.Description });
            }
        }
        var duplicates = Content(Card("SHOCKWAVE"), Card("BASH"), Card("NEUTRALIZE"));
        ScenarioTestMod.Require(duplicates.HoverTips.Count == 2 && duplicates.Text.Split("[url=").Length == 3, "Shared powers should appear once.");
        var lethal = Content(ModEntry.Rules.KillGroup.TriggerModelIds.Select(Card).ToArray());
        ScenarioTestMod.Require(lethal.HoverTips.Count == 0 && !lethal.Text.Contains("[url=")
            && lethal.Text == HintText.Colored(HintText.KillTitle, ModEntry.Rules.KillTitleColor),
            "All lethal sources must merge into one unchanged-color label without hover metadata.");
        var mixed = Content(Card("FEED"), Card("SHOCKWAVE"), Card("ECHOING_SLASH"));
        ScenarioTestMod.Require(mixed.HoverTips.Count == 2 && mixed.Text.Contains(HintText.KillTitle)
            && !mixed.HoverTips.Values.Any(t => t is CardHoverTip), "Lethal must coexist with normal effects without a card preview.");
        foreach (string id in ModEntry.Rules.Groups.Single(g => g.Key == "multiplayer").TriggerModelIds)
        {
            var card = Card(id);
            foreach (bool upgrade in new[] { false, true })
            {
                if (upgrade) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
                var content = Content(card);
                ScenarioTestMod.Require(content.HoverTips.Count == 1
                    && content.HoverTips.Single().Value is CardHoverTip tip && ReferenceEquals(tip.Card, card)
                    && content.Text.Contains(HintText.Escape(card.Title)), "Multiplayer card preview mismatch: " + id);
            }
        }
        var malaise = Content(Card("MALAISE"));
        var weakening = (HoverTip)malaise.HoverTips["WEAKENING"];
        ScenarioTestMod.Require(malaise.HoverTips.Count == 2 && malaise.HoverTips.ContainsKey("WEAK_POWER")
            && weakening.Title == HintText.WeakeningTitle
            && weakening.Description == (HintText.IsChinese ? "使敌人失去力量" : "Causes enemies to lose Strength.")
            && weakening.Icon == ModelDb.Power<StrengthPower>().Icon
            && malaise.Text.Contains("#E6A23C") && malaise.Text.Contains("#6EC1FF"), "Weakening must be inline, distinct and use the Strength icon.");
        var expose = Content(Card("EXPOSE"), Card("BASH"));
        var exposed = (HoverTip)expose.HoverTips["EXPOSE"];
        ScenarioTestMod.Require(expose.HoverTips.Count == 2 && expose.HoverTips.ContainsKey("VULNERABLE_POWER")
            && exposed.Title == HintText.ExposeTitle
            && exposed.Description == (HintText.IsChinese ? "去除敌人的所有人工制品" : "Removes all Artifact from the enemy.")
            && exposed.Icon == ModelDb.Power<ArtifactPower>().Icon
            && expose.Text.Contains("#C9A6FF") && expose.Text.Contains("#FF6B6B"),
            "Expose must use a purple custom effect with Artifact icon, alongside deduplicated Vulnerable.");
        var exposeOnlyRules = ModEntry.Rules with { Groups = ModEntry.Rules.Groups.Where(g => g.Key == "other").ToArray() };
        var exposeCard = Card("EXPOSE");
        var exposeOnly = HintContent.Build(new HintMatcher(exposeOnlyRules).MatchCards(new[] { HintText.Describe(exposeCard) }),
            exposeOnlyRules, new[] { exposeCard });
        ScenarioTestMod.Require(exposeOnly.HoverTips.Count == 1 && exposeOnly.HoverTips.ContainsKey("EXPOSE"),
            "Disabling the Vulnerable source group must not add a duplicate card-name fallback for Expose.");
        var stun = Content(Card("WHISTLE"));
        ScenarioTestMod.Require(stun.HoverTips.Single().Value is HoverTip && stun.HoverTips.ContainsKey("STUN"), "Whistle should show native Stun.");
        var first = Card("DEBILITATE"); var second = Card("DEBILITATE");
        second.UpgradeInternal(); second.FinalizeUpgradeInternal();
        var p0 = HintContent.Build(matcher.MatchCards(new[] { HintText.Describe(first) }), ModEntry.Rules, new[] { first }, "p0.");
        var p1 = HintContent.Build(matcher.MatchCards(new[] { HintText.Describe(second) }), ModEntry.Rules, new[] { second }, "p1.");
        ScenarioTestMod.Require(!p0.HoverTips.Keys.Intersect(p1.HoverTips.Keys).Any()
            && ((HoverTip)p0.HoverTips.Single().Value).Description != ((HoverTip)p1.HoverTips.Single().Value).Description,
            "Different players' effect descriptions must not collide.");
        records.Add(new { card = "MALAISE", power = "WEAKENING", weakening.Title, weakening.Description });
        records.Add(new { card = "EXPOSE", power = "EXPOSE", exposed.Title, exposed.Description });
        string path = ProjectSettings.GlobalizePath("user://bubble-effects.json");
        File.WriteAllText(path, JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }

    public static void Hover(string surface, string key, bool card)
    {
        Node owner = surface == "overlay" ? ScenarioTestMod.Overlay()
            : ScenarioTestMod.LastBubbleNode ?? throw new Exception("No bubble.");
        var label = surface == "overlay" ? ScenarioTestMod.Field<MegaRichTextLabel>(owner, "_description")
            : owner.GetNode<MegaRichTextLabel>("%Text");
        var helper = owner.GetChildren().OfType<HintHover>().Single();
        var field = typeof(HintHover).GetField("_hoverTip", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var active = typeof(HintHover).GetField("_activeKey", BindingFlags.NonPublic | BindingFlags.Instance)!;
        // Real viewport mouse events exercise the RichTextLabel hit test and meta-hover signals.
        var rect = label.GetGlobalRect();
        for (float y = rect.Position.Y + 3; y < rect.End.Y; y += 6)
        for (float x = rect.Position.X + 3; x < rect.End.X; x += 5)
        {
            Move(label, new Vector2(x, y));
            if ((string?)active.GetValue(helper) != key) continue;
            if (field.GetValue(helper) is not NHoverTipSet tip) continue;
            ScenarioTestMod.Require(tip.IsInsideTree(), "Native hover tip was not added to the scene.");
            ScenarioTestMod.Require(Descendants(tip).OfType<NCard>().Any() == card,
                "Expected " + (card ? "a card preview" : "only an effect tooltip") + " for " + key);
            if (key.Split('.').Last() is "WEAKENING" or "EXPOSE")
            {
                var backgrounds = Descendants(tip).OfType<CanvasItem>().Where(n => n.Name == "Bg").ToArray();
                ScenarioTestMod.Require(backgrounds.Length == 1
                    && backgrounds[0].Material == MegaCrit.Sts2.Core.Assets.PreloadManager.Cache.GetMaterial("res://materials/ui/hover_tip_debuff.tres"),
                    "Custom effect must use the native red-brown debuff background: " + key);
            }
            _previousTip = tip;
            _previousLabel = label;
            return;
        }
        throw new Exception("Real mouse motion never opened " + surface + " " + key + ". Rect=" + rect);
    }

    private static IEnumerable<Node> Descendants(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    public static void Unhover()
    {
        var label = _previousLabel!;
        Move(label, new Vector2(5, 5));
        ScenarioTestMod.Require(_previousTip != null && (!GodotObject.IsInstanceValid(_previousTip)
            || _previousTip.IsQueuedForDeletion() || !_previousTip.IsInsideTree()), "Hover tip survived mouse exit.");
    }

    public static void RequireGone() => ScenarioTestMod.Require(_previousTip != null
        && !GodotObject.IsInstanceValid(_previousTip), "Native tooltip leaked after bubble/mouse exit.");

    public static void NoHover(string surface)
    {
        Node owner = surface == "overlay" ? ScenarioTestMod.Overlay()
            : ScenarioTestMod.LastBubbleNode ?? throw new Exception("No bubble.");
        var label = surface == "overlay" ? ScenarioTestMod.Field<MegaRichTextLabel>(owner, "_description")
            : owner.GetNode<MegaRichTextLabel>("%Text");
        ScenarioTestMod.Require(label.Text.Contains(HintText.KillTitle) && !label.Text.Contains("[url="),
            "The lethal-only label must have no hover links.");
        if (surface == "overlay") ScenarioTestMod.Require(!label.Text.Contains('\n'), "Lethal must stay inline in the player's effect row.");
        var helper = owner.GetChildren().OfType<HintHover>().Single();
        var field = typeof(HintHover).GetField("_hoverTip", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var rect = label.GetGlobalRect();
        for (float y = rect.Position.Y + 3; y < rect.End.Y; y += 6)
        for (float x = rect.Position.X + 3; x < rect.End.X; x += 5)
        {
            Move(label, new Vector2(x, y));
            ScenarioTestMod.Require(field.GetValue(helper) == null, "Lethal unexpectedly opened a hover tip on " + surface);
        }
    }

    private static void Move(Control label, Vector2 position) => label.GetViewport().PushInput(
        new InputEventMouseMotion { Position = position, GlobalPosition = position }, true);
}
