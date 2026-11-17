using System;
using System.Collections.Generic;
using System.Linq;

namespace ComboHint;

[Flags]
public enum DynamicHintEffects { None = 0, Weak = 1, Vulnerable = 2, Strangle = 4 }
public readonly record struct HintCard(string ModelId, string Title, DynamicHintEffects DynamicEffects = DynamicHintEffects.None);
public enum HintCategory { Card, StrengthLoss, Lethal }
public readonly record struct MatchedTrigger(string Text, string ColorHex, HintCategory Category = HintCategory.Card, string? Variant = null);
public readonly record struct MatchedCard(HintCard Card, MatchedTrigger Trigger);

// Retain source cards for presentation; Lethal has one shared identity for bubble debounce.
public sealed class HintMatcher(HintRules rules)
{
    public List<MatchedTrigger> Match(IEnumerable<HintCard> cards) => MatchCards(cards).Select(m => m.Trigger).ToList();
    public List<MatchedCard> MatchCards(IEnumerable<HintCard> cards) =>
        MatchCards(cards, new[] { rules.KillGroup }.Concat(rules.Groups), rules.KillTitleColor);

    private static List<MatchedCard> MatchCards(IEnumerable<HintCard> cards, IEnumerable<TriggerGroup> groups, string killColor)
    {
        var matches = new List<MatchedCard>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var card in cards)
        {
            if (string.IsNullOrWhiteSpace(card.ModelId)) continue;
            // These callbacks only react to the caster's own actions. Exclude them even
            // when a legacy config still lists them as shared enemy effects.
            if (card.ModelId.Equals("STRANGLE", StringComparison.OrdinalIgnoreCase)
                || card.ModelId.Equals("MONARCHS_GAZE", StringComparison.OrdinalIgnoreCase)) continue;
            if (card.ModelId.Equals("MAD_SCIENCE", StringComparison.OrdinalIgnoreCase)
                && (card.DynamicEffects & (DynamicHintEffects.Weak | DynamicHintEffects.Vulnerable)) == DynamicHintEffects.None) continue;
            if (card.ModelId.Equals("MAD_SCIENCE", StringComparison.OrdinalIgnoreCase)
                && card.DynamicEffects != DynamicHintEffects.None)
            {
                if (!seen.Add(card.ModelId + ":" + card.DynamicEffects)) continue;
                string key = card.DynamicEffects.HasFlag(DynamicHintEffects.Weak) ? "specialweaken"
                    : card.DynamicEffects.HasFlag(DynamicHintEffects.Vulnerable) ? "specialvulnerable" : "other";
                string color = groups.FirstOrDefault(g => g.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.ColorHex ?? "#C9A6FF";
                matches.Add(new MatchedCard(card, new MatchedTrigger(card.Title, color, Variant: card.DynamicEffects.ToString())));
                continue;
            }
            TriggerGroup? selected = null;
            foreach (var group in groups)
            {
                if (!group.TriggerModelIds.Contains(card.ModelId)) continue;
                selected ??= group;
                if (group.Key.Equals("overlayKill", StringComparison.OrdinalIgnoreCase)) { selected = group; break; }
                // Strength loss owns cards such as Malaise, even when another group matches first.
                if (group.Key.Equals("weakenattack", StringComparison.OrdinalIgnoreCase)) { selected = group; break; }
            }
            if (selected == null || !seen.Add(card.ModelId)) continue;
            if (selected.Key.Equals("overlayKill", StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(new MatchedCard(card, new MatchedTrigger("Lethal", killColor, HintCategory.Lethal)));
                continue;
            }
            var category = selected.Key.Equals("weakenattack", StringComparison.OrdinalIgnoreCase)
                ? HintCategory.StrengthLoss : HintCategory.Card;
            matches.Add(new MatchedCard(card, new MatchedTrigger(card.Title, selected.ColorHex, category)));
        }
        return matches;
    }
}
