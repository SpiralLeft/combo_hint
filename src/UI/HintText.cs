using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;

namespace ComboHint;

public static class HintText
{
    public static bool IsChinese => (LocManager.Instance?.Language ?? "eng").StartsWith("zh", StringComparison.OrdinalIgnoreCase);
    public static string Title => IsChinese ? "连携提示" : "Combo Hints";
    public static string NoMatches => IsChinese ? "当前没有连携效果" : "No combo effects available";
    public static string KillTitle => IsChinese ? "斩杀" : "Lethal";
    public static string WeakeningTitle => IsChinese ? "弱化" : "Enfeeblement";
    public static string ExposeTitle => IsChinese ? "暴露" : "Expose";
    public static string Has => IsChinese ? "有" : " has ";
    public static string Separator => IsChinese ? "、" : ", ";
    public static string BubblePrefix => IsChinese ? "我有" : "I have ";
    public static HintCard Describe(CardModel card)
    {
        var effects = DynamicHintEffects.None;
        if (card is MadScience mad && mad.TinkerTimeType == CardType.Attack && mad.TargetType == TargetType.AnyEnemy)
        {
            if (mad.TinkerTimeRider == TinkerTime.RiderEffect.Sapping)
            {
                if (mad.DynamicVars["SappingWeak"].BaseValue > 0) effects |= DynamicHintEffects.Weak;
                if (mad.DynamicVars["SappingVulnerable"].BaseValue > 0) effects |= DynamicHintEffects.Vulnerable;
            }
        }
        return new(card.Id.Entry, card.Title ?? card.Id.Entry, effects);
    }
    public static string Escape(string value) => string.Concat(value.Select(c => c switch
        { '[' => "[lb]", ']' => "[rb]", _ => c.ToString() }));
    public static string Colored(string text, string color) => $"[color={color}]{Escape(text)}[/color]";
    public static string JoinNames(IEnumerable<MatchedTrigger> matches) => string.Join(Separator, matches.Select(m => Colored(m.Text, m.ColorHex)));
    public static string Join(IEnumerable<MatchedTrigger> matches)
    {
        var cards = matches.ToArray();
        string normal = JoinNames(cards.Where(m => m.Category == HintCategory.Card));
        string strength = JoinNames(cards.Where(m => m.Category == HintCategory.StrengthLoss));
        if (strength.Length == 0) return normal;
        string grouped = WeakeningTitle + (IsChinese ? "：" : ": ") + strength;
        return normal.Length == 0 ? grouped : normal + (IsChinese ? "；" : "; ") + grouped;
    }

    public static string PlayerName(Creature creature)
    {
        string fallback = creature.Player?.Character?.Title.GetFormattedText() ?? (IsChinese ? "玩家" : "Player");
        try
        {
            var run = RunManager.Instance;
            ulong id = creature.Player?.NetId ?? 0;
            return run == null || run.IsSingleplayerOrFakeMultiplayer || id == 0
                ? fallback : PlatformUtil.GetPlayerName(run.NetService.Platform, id);
        }
        catch { return fallback; }
    }
}
