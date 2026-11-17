using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace ComboHintScenarioTests;

public static class CardCatalog
{
    // Read the installed game's public model/localization APIs, never scraped card names.
    public static string Export()
    {
        var cards = ModelDb.AllCards.Where(c => c.GetType().Assembly == typeof(CardModel).Assembly)
            .OrderBy(c => c.Id.Entry, StringComparer.Ordinal).Select(c =>
            {
                var basic = (CardModel)c.MutableClone();
                var upgraded = (CardModel)c.MutableClone();
                string? upgradeError = null;
                if (upgraded.MaxUpgradeLevel > 0)
                {
                    try { upgraded.UpgradeInternal(); upgraded.FinalizeUpgradeInternal(); }
                    catch (Exception ex) { upgradeError = ex.Message; }
                }
                return new
                {
                    model_id = c.Id.ToString(), entry = c.Id.Entry, type = c.GetType().FullName,
                    target = c.TargetType.ToString(), rarity = c.Rarity.ToString(),
                    in_library = c.ShouldShowInCardLibrary,
                    title = c.Title, raw_description = c.Description.GetRawText(),
                    basic = Describe(basic), upgraded = Describe(upgraded), upgrade_error = upgradeError
                };
            }).ToArray();
        ScenarioTestMod.Require(cards.Length > 0 && cards.All(c => !string.IsNullOrEmpty(c.entry))
            && cards.Any(c => c.entry == "BASH" && !string.IsNullOrEmpty(c.raw_description)),
            "Game must provide both Model IDs and descriptions for real cards.");
        string path = ProjectSettings.GlobalizePath("user://card-catalog.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            language = LocManager.Instance.Language, count = cards.Length, cards
        }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        return path;
    }

    private static object Describe(CardModel card)
    {
        string? description = null, error = null;
        try { description = card.GetDescriptionForPile(PileType.None, null); }
        catch (Exception ex) { error = ex.Message; }
        return new { title = card.Title, level = card.CurrentUpgradeLevel, description, error };
    }
}
