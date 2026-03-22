using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace ComboHint;

[ModInitializer("Initialize")]
public static class ModEntry
{
    private const string HarmonyId = "local.combo_hint";

    private const string ModId = "ComboHint";

    private const string ConfigFileName = "combo_hint.config.json";

    public static IReadOnlyList<TriggerGroup> TriggerGroups { get; private set; } = new List<TriggerGroup>();

    public static void Initialize()
    {
        LoadConfig();
        new Harmony(HarmonyId).PatchAll();
        int totalTriggerCount = TriggerGroups.Sum((TriggerGroup g) => g.TriggerTexts.Count);
        Log.Info($"[ComboHint] initialized, trigger groups: {TriggerGroups.Count}, trigger texts: {totalTriggerCount}");
    }

    private static void LoadConfig()
    {
        try
        {
            Mod? me = ModManager.AllMods.FirstOrDefault((Mod m) => m.manifest?.id == ModId);
            if (me == null)
            {
                Log.Warn("[ComboHint] could not find self in ModManager.AllMods, trigger groups will be empty.");
                TriggerGroups = new List<TriggerGroup>();
                return;
            }

            string configPath = Path.Combine(me.path, ConfigFileName);
            if (!File.Exists(configPath))
            {
                Log.Warn($"[ComboHint] config file not found at {configPath}, trigger groups will be empty.");
                TriggerGroups = new List<TriggerGroup>();
                return;
            }

            string json = File.ReadAllText(configPath);
            ModConfig? config = JsonSerializer.Deserialize<ModConfig>(json);
            TriggerGroups = BuildTriggerGroups(config);
            if (TriggerGroups.Count == 0)
            {
                Log.Warn("[ComboHint] no valid trigger groups in config, trigger groups will be empty.");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[ComboHint] failed to load config: {ex}");
            TriggerGroups = new List<TriggerGroup>();
        }
    }

    private static List<TriggerGroup> BuildTriggerGroups(ModConfig? config)
    {
        if (config == null)
        {
            return new List<TriggerGroup>();
        }

        List<TriggerGroup> groups = new List<TriggerGroup>
        {
            TriggerGroup.From(config.triggerTexts_weakendefence, config.color_weakendefence),
            TriggerGroup.From(config.triggerTexts_weakenattack, config.color_weakenattack),
            TriggerGroup.From(config.triggerTexts_other, config.color_other)
        };

        return groups.Where((TriggerGroup g) => g.TriggerTexts.Count > 0).ToList();
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardDrawn))]
public static class AfterCardDrawnPatch
{
    public static void Postfix(CardModel card)
    {
        try
        {
            if (card == null || card.Owner?.Creature == null)
            {
                return;
            }

            string title = card.Title ?? string.Empty;
            string description = card.GetDescriptionForPile(card.Pile?.Type ?? PileType.None);
            List<MatchedTrigger> matchedTexts = FindMatchedTexts(title, description);
            if (matchedTexts.Count == 0)
            {
                return;
            }

            string bubbleText = "\u6211\u6709" + string.Join("\u3001", matchedTexts.Select((MatchedTrigger m) => FormatColoredText(m.Text, m.ColorHex)));
            NSpeechBubbleVfx? bubble = NSpeechBubbleVfx.Create(bubbleText, card.Owner.Creature, 1.8);
            if (bubble != null)
            {
                NCombatRoom.Instance?.CombatVfxContainer.AddChild(bubble);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[ComboHint] failed in draw hook: {ex}");
        }
    }

    private static List<MatchedTrigger> FindMatchedTexts(string title, string description)
    {
        List<MatchedTrigger> matches = new List<MatchedTrigger>();
        HashSet<string> dedup = new HashSet<string>(StringComparer.Ordinal);
        foreach (TriggerGroup group in ModEntry.TriggerGroups)
        {
            foreach (string triggerText in group.TriggerTexts)
            {
                if (((!string.IsNullOrEmpty(title) && title.Contains(triggerText, StringComparison.Ordinal)) || (!string.IsNullOrEmpty(description) && description.Contains(triggerText, StringComparison.Ordinal))) && dedup.Add(triggerText))
                {
                    matches.Add(new MatchedTrigger(triggerText, group.ColorHex));
                }
            }
        }
        return matches;
    }

    private static string FormatColoredText(string text, string colorHex)
    {
        string safeText = text.Replace("[", "\\[").Replace("]", "\\]");
        return $"[color={colorHex}]{safeText}[/color]";
    }
}

public sealed class ModConfig
{
    public List<string>? triggerTexts_weakendefence { get; set; }

    public List<string>? triggerTexts_weakenattack { get; set; }

    public List<string>? triggerTexts_other { get; set; }

    public string? color_weakendefence { get; set; }

    public string? color_weakenattack { get; set; }

    public string? color_other { get; set; }
}

public sealed class TriggerGroup
{
    public IReadOnlyList<string> TriggerTexts { get; }

    public string ColorHex { get; }

    private TriggerGroup(IReadOnlyList<string> triggerTexts, string colorHex)
    {
        TriggerTexts = triggerTexts;
        ColorHex = colorHex;
    }

    public static TriggerGroup From(List<string>? rawTexts, string? rawColor)
    {
        List<string> triggerTexts = rawTexts?.Where((string s) => !string.IsNullOrWhiteSpace(s)).Select((string s) => s.Trim()).Distinct(StringComparer.Ordinal).ToList() ?? new List<string>();
        string colorHex = NormalizeColor(rawColor);
        return new TriggerGroup(triggerTexts, colorHex);
    }

    private static string NormalizeColor(string? rawColor)
    {
        if (string.IsNullOrWhiteSpace(rawColor))
        {
            return "#FFFFFF";
        }

        string trimmed = rawColor.Trim();
        if (trimmed.StartsWith("#", StringComparison.Ordinal))
        {
            return trimmed;
        }

        return "#" + trimmed;
    }
}

public readonly record struct MatchedTrigger(string Text, string ColorHex);
