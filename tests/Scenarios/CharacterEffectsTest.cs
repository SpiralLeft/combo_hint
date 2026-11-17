using ComboHint;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace ComboHintScenarioTests;

public static class CharacterEffectsTest
{
    private static CardModel Card(string id) => (CardModel)ModelDb.AllCards.Single(c => c.Id.Entry == id).MutableClone();
    private static HintContent Content(params CardModel[] cards) => HintContent.Build(
        new HintMatcher(ModEntry.Rules).MatchCards(cards.Select(HintText.Describe)), ModEntry.Rules, cards);

    public static void Verify()
    {
        ScenarioTestMod.Require(!ComboHintSettings.EnablePoison && !ComboHintSettings.EnableDoom
            && ComboHintSettings.EnableDislikeYou, "New defaults must opt out of Poison/Doom and enable Dislike You.");
        foreach (var (group, property, power) in new (string, string, PowerModel)[]
        {
            ("poison", "EnablePoison", ModelDb.Power<PoisonPower>()),
            ("doom", "EnableDoom", ModelDb.Power<DoomPower>())
        })
        {
            var setting = typeof(ComboHintSettings).GetProperty(property)!;
            bool original = (bool)setting.GetValue(null)!;
            string key = power.Id.Entry;
            var ids = ModEntry.Rules.Groups.Single(g => g.Key == group).TriggerModelIds.ToArray();
            try
            {
                foreach (var id in ids)
                {
                    var source = Card(id);
                    foreach (bool upgrade in new[] { false, true })
                    {
                        if (upgrade) { source.UpgradeInternal(); source.FinalizeUpgradeInternal(); }
                        setting.SetValue(null, false);
                        var hidden = Content(source);
                        bool weak = id is "HAZE" or "DEATHBRINGER";
                        ScenarioTestMod.Require(!hidden.HoverTips.ContainsKey(key)
                            && hidden.HoverTips.Count == (weak ? 1 : 0)
                            && hidden.VisibleTriggers.Count == (weak ? 1 : 0)
                            && !hidden.HoverTips.Values.Any(t => t is CardHoverTip),
                            "Disabled source leaked a card fallback or suppressed another effect: " + id);
                        setting.SetValue(null, true);
                        var shown = Content(source);
                        var tip = (HoverTip)shown.HoverTips[key];
                        var expected = (HoverTip)HoverTipFactory.FromPower(power);
                        ScenarioTestMod.Require(shown.HoverTips.Count == hidden.HoverTips.Count + 1
                            && tip.Title == expected.Title && tip.Description == expected.Description && tip.Icon != null
                            && shown.Text.Contains(HintText.Colored(tip.Title!, ModEntry.Rules.EffectColor(key))),
                            "Wrong native effect or color for " + id);
                    }
                }
                var merged = Content(ids.Select(Card).ToArray());
                ScenarioTestMod.Require(merged.HoverTips.Count == 2 && merged.HoverTips.ContainsKey(key)
                    && merged.HoverTips.ContainsKey("WEAK_POWER"), "Duplicate character effects must merge, retaining Weak.");
            }
            finally { setting.SetValue(null, original); }
        }
        foreach (string id in new[] { "ACCELERANT", "MIRAGE", "DEATHS_DOOR", "NEUROSURGE", "SHROUD", "TIMES_UP" })
            ScenarioTestMod.Require(Content(Card(id)).Text.Length == 0, "Consumer or self-debuff is not an enemy application: " + id);
        foreach (string id in new[] { "CONCOCT", "UNDERWORLD" })
            ScenarioTestMod.Require(Content(Card(id)).HoverTips.Single().Value is CardHoverTip,
                "Multiplayer cards must keep their card preview: " + id);
        var dislike = Card("DISLIKE_YOU");
        var basic = (HoverTip)Content(dislike).HoverTips["DISLIKE_YOU_POWER"];
        dislike.UpgradeInternal(); dislike.FinalizeUpgradeInternal();
        var upgradedTip = (HoverTip)Content(dislike).HoverTips["DISLIKE_YOU_POWER"];
        ScenarioTestMod.Require(basic.Description.Contains("50") && upgradedTip.Description.Contains("75")
            && basic.Title == ModelDb.Power<DislikeYouPower>().Title.GetFormattedText()
            && basic.Icon != null && upgradedTip.Icon != null, "Dislike You must show native 50% / 75% effect descriptions.");
        ScenarioTestMod.Require(Content(Card("DEBILITATE"), dislike).HoverTips.Count == 2,
            "Dislike You and Debilitate must retain separate effect identities.");
    }
}
