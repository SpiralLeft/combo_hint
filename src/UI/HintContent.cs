using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;

namespace ComboHint;

public sealed record HintContent(string Text, IReadOnlyDictionary<string, IHoverTip> HoverTips)
{
    public IReadOnlyList<MatchedTrigger> VisibleTriggers { get; init; } = Array.Empty<MatchedTrigger>();
    public static HintContent Build(IEnumerable<MatchedCard> cards, HintRules rules, IEnumerable<CardModel> sourceCards,
        string scope = "")
    {
        var sources = sourceCards.GroupBy(c => SourceKey(HintText.Describe(c)))
            .ToDictionary(g => g.Key, g => g.First());
        var text = new List<string>();
        var tips = new Dictionary<string, IHoverTip>(StringComparer.Ordinal);
        var visible = new HashSet<MatchedTrigger>();
        MatchedTrigger current = default;
        bool lethalShown = false;
        void Add(string id, string title, string color, IHoverTip tip)
        {
            if (!ComboHintSettings.IsEffectEnabled(id)) return;
            visible.Add(current);
            string key = scope + id;
            if (!tips.TryAdd(key, tip)) return;
            text.Add($"[url={key}]" + HintText.Colored(title, color) + "[/url]");
        }
        foreach (var match in cards)
        {
            current = match.Trigger;
            if (match.Trigger.Category == HintCategory.Lethal)
            {
                visible.Add(current);
                if (!lethalShown) text.Add(HintText.Colored(HintText.KillTitle, match.Trigger.ColorHex));
                lethalShown = true;
                continue;
            }
            bool multiplayer = rules.Groups.Any(g => g.Key.Equals("multiplayer", StringComparison.OrdinalIgnoreCase)
                && g.TriggerModelIds.Contains(match.Card.ModelId));
            // Multiplayer cards intentionally show the actual card, even if they apply a power.
            if (multiplayer)
            {
                if (!ComboHintSettings.EnableMultiplayer) continue;
                var card = sources[SourceKey(match.Card)];
                Add("CARD." + card.Id.Entry, card.Title, match.Trigger.ColorHex, new CardHoverTip(card));
                continue;
            }
            bool strength = match.Trigger.Category == HintCategory.StrengthLoss;
            if (strength) Add("WEAKENING", HintText.WeakeningTitle, rules.EffectColor("WEAKENING", match.Trigger.ColorHex),
                CustomEffectTip("WEAKENING", HintText.WeakeningTitle,
                    HintText.IsChinese ? "使敌人失去力量" : "Causes enemies to lose Strength.", ModelDb.Power<StrengthPower>().Icon));
            bool expose = match.Card.ModelId.Equals("EXPOSE", StringComparison.OrdinalIgnoreCase);
            if (expose)
                Add("EXPOSE", HintText.ExposeTitle,
                    rules.EffectColor("EXPOSE"),
                    CustomEffectTip("EXPOSE", HintText.ExposeTitle,
                        HintText.IsChinese ? "去除敌人的所有人工制品" : "Removes all Artifact from the enemy.", ModelDb.Power<ArtifactPower>().Icon));
            if (match.Card.ModelId.Equals("WHISTLE", StringComparison.OrdinalIgnoreCase))
            {
                var stun = StunIntent.GetStaticHoverTip();
                Add("STUN", stun.Title ?? throw new InvalidOperationException("Stun tooltip has no title."), rules.EffectColor("STUN", match.Trigger.ColorHex), stun);
                continue;
            }
            var powers = PowersFor(match.Card, rules).ToArray();
            if (powers.Length == 0)
            {
                if (!strength && !expose)
                {
                    // Preserve custom, unmapped config entries without inventing an effect.
                    var card = sources[SourceKey(match.Card)];
                    Add("CARD." + card.Id.Entry, card.Title, match.Trigger.ColorHex, new CardHoverTip(card));
                }
                continue;
            }
            foreach (var power in powers)
            {
                string key = power.Id.Entry;
                if (!ComboHintSettings.IsEffectEnabled(key)) continue;
                visible.Add(current);
                string color = rules.EffectColor(key, match.Trigger.ColorHex);
                if (tips.ContainsKey(scope + key)) continue;
                // These descriptions contain a multiplier/duration/damage/percentage amount. Canonical
                // power defaults can be wrong (e.g. Knockdown defaults to 1x), so use the card.
                int? amount = null;
                string? variable = match.Card.ModelId.ToUpperInvariant() switch
                {
                    "KNOCKDOWN" => "KnockdownPower", "DEBILITATE" => "DebilitatePower",
                    "DISLIKE_YOU" => "DislikeYouPower", _ => null
                };
                if (variable != null) amount = sources[SourceKey(match.Card)].DynamicVars[variable].IntValue;
                if (amount.HasValue && power is DebilitatePower or DislikeYouPower)
                {
                    // Their generic descriptions contain literal base values. Use the same
                    // smart localization template as enemy icons, without binding a fake enemy.
                    var description = power.SmartDescription;
                    description.Add("Amount", amount.Value);
                    Add(key, power.Title.GetFormattedText(), color,
                        new HoverTip(power, description.GetFormattedText(), true));
                }
                else Add(key, power.Title.GetFormattedText(), color, HoverTipFactory.FromPower(power, amount));
            }
        }
        return new HintContent(string.Join(HintText.Separator, text), tips) { VisibleTriggers = visible.ToArray() };
    }

    private static (string, DynamicHintEffects) SourceKey(HintCard card) => (card.ModelId.ToUpperInvariant(), card.DynamicEffects);

    private static HoverTip CustomEffectTip(string id, string title, string description, Godot.Texture2D icon)
    {
        const string tableName = "static_hover_tips";
        string key = "ComboHint." + id;
        // Namespaced entries use the game's public localization API; no base strings are replaced.
        var table = LocManager.Instance.GetTable(tableName);
        if (!table.HasEntry(key + ".title") || table.GetRawText(key + ".title") != title)
            table.MergeWith(new Dictionary<string, string>
            {
                [key + ".title"] = title,
                [key + ".description"] = description
            });
        return new HoverTip(new LocString(tableName, key + ".title"), new LocString(tableName, key + ".description"),
            icon);
    }

    private static IEnumerable<PowerModel> PowersFor(HintCard card, HintRules rules)
    {
        string cardId = card.ModelId;
        if (cardId.Equals("MAD_SCIENCE", StringComparison.OrdinalIgnoreCase))
        {
            if (card.DynamicEffects.HasFlag(DynamicHintEffects.Weak)) yield return ModelDb.Power<WeakPower>();
            if (card.DynamicEffects.HasFlag(DynamicHintEffects.Vulnerable)) yield return ModelDb.Power<VulnerablePower>();
            yield break;
        }
        // Explicitly reviewed card -> enemy effect mappings, never inferred from card titles.
        switch (cardId.ToUpperInvariant())
        {
            case "FLANKING": yield return ModelDb.Power<FlankingPower>(); break;
            case "KNOCKDOWN": yield return ModelDb.Power<KnockdownPower>(); break;
            case "TAG_TEAM": yield return ModelDb.Power<TagTeamPower>(); break;
            case "DEBILITATE": yield return ModelDb.Power<DebilitatePower>(); break;
            case "DISLIKE_YOU": yield return ModelDb.Power<DislikeYouPower>(); break;
        }
        foreach (var group in rules.Groups)
        {
            if (!group.TriggerModelIds.Contains(cardId)) continue;
            if (group.Key == "specialweaken") yield return ModelDb.Power<WeakPower>();
            if (group.Key == "specialvulnerable") yield return ModelDb.Power<VulnerablePower>();
            if (group.Key == "poison") yield return ModelDb.Power<PoisonPower>();
            if (group.Key == "doom") yield return ModelDb.Power<DoomPower>();
        }
    }
}
