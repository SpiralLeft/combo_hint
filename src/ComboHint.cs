using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Runs;

namespace ComboHint;

[ModInitializer("Initialize")]
public static class ModEntry
{
    private const string HarmonyId = "local.combo_hint";

    private const string ModId = "ComboHint";

    private const string ConfigFileName = "combo_hint.config.json";

    private const string UiLogFileName = "combo_hint.ui.log";
    private const string InjectLogFileName = "combo_hint.inject.log";

    private const string ConfigEnabledKey = "comboHintEnabled";

    private const double DefaultBubbleDurationSeconds = 1.8;

    private const string DefaultOverlayKillTitleColorHex = "#FF3B30";

    public static IReadOnlyList<TriggerGroup> TriggerGroups { get; private set; } = new List<TriggerGroup>();

    public static TriggerGroup OverlayKillTriggerGroup { get; private set; } = TriggerGroup.From(null, "#FF7F50");

    public static string OverlayKillTitleColorHex { get; private set; } = DefaultOverlayKillTitleColorHex;

    public static double BubbleDurationSeconds { get; private set; } = DefaultBubbleDurationSeconds;

    public static bool EnableSinglePlayerHint { get; private set; } = true;

    public static bool EnabledByGameplaySetting { get; private set; } = true;

    public static string ModRootPath { get; private set; } = string.Empty;

    public static void Initialize()
    {
        LoadConfig();
        LoadManifestSettings();
        ResetUiLog();
        ResetInjectLog();

        new Harmony(HarmonyId).PatchAll();
        int totalTriggerCount = TriggerGroups.Sum((TriggerGroup g) => g.TriggerTexts.Count);
        Log.Info($"[ComboHint] initialized, trigger groups: {TriggerGroups.Count}, trigger texts: {totalTriggerCount}, enableSinglePlayerHint: {EnableSinglePlayerHint}");
    }

    public static void ResetUiLog()
    {
        try
        {
            string root = ResolveModRoot();
            string path = Path.Combine(root, UiLogFileName);
            string banner = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] ComboHintUiLog.Reset: start new session{System.Environment.NewLine}";
            File.WriteAllText(path, banner);
        }
        catch
        {
        }
    }

    public static void LogUi(string tag, string message)
    {
        try
        {
            string root = ResolveModRoot();
            string path = Path.Combine(root, UiLogFileName);
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {tag}: {message}{System.Environment.NewLine}";
            File.AppendAllText(path, line);
        }
        catch
        {
        }
    }

    public static void ResetInjectLog()
    {
        try
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), InjectLogFileName);
            string banner = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] ComboHintInjectLog.Reset: start new session{System.Environment.NewLine}";
            File.WriteAllText(path, banner);
        }
        catch
        {
        }
    }

    public static void LogInject(string tag, string message)
    {
        try
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), InjectLogFileName);
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {tag}: {message}{System.Environment.NewLine}";
            File.AppendAllText(path, line);
        }
        catch
        {
        }
    }

    private static string ResolveModRoot()
    {
        string root = ModRootPath;
        if (string.IsNullOrWhiteSpace(root))
        {
            Mod? me = ModManager.AllMods.FirstOrDefault((Mod m) => m.manifest?.id == ModId);
            root = me?.path ?? Directory.GetCurrentDirectory();
        }

        return root;
    }

    public static void LogErrorToFile(string context, Exception ex)
    {
        // Legacy logger disabled temporarily; route concise information to UI log.
        LogUi("LegacyError", $"{context}: {ex.Message}");
    }

    public static void LogInfoToFile(string context, string message)
    {
        // Legacy logger disabled temporarily; route concise information to UI log.
        LogUi($"LegacyInfo/{context}", message);
    }

    private static void LoadManifestSettings()
    {
        EnableSinglePlayerHint = true;
        try
        {
            Mod? me = ModManager.AllMods.FirstOrDefault((Mod m) => m.manifest?.id == ModId);
            if (me != null)
            {
                string manifestPath = Path.Combine(me.path, "combo_hint.json");
                if (File.Exists(manifestPath))
                {
                    try
                    {
                        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
                        if (doc.RootElement.TryGetProperty("enableSinglePlayerHint", out JsonElement el))
                        {
                            if (el.ValueKind == JsonValueKind.True)
                            {
                                EnableSinglePlayerHint = true;
                            }
                            else if (el.ValueKind == JsonValueKind.False)
                            {
                                EnableSinglePlayerHint = false;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"[ComboHint] failed to parse manifest to read enableSinglePlayerHint: {ex}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[ComboHint] error while checking single-player setting: {ex}");
        }
    }

    public static bool IsOverlayEnabledInCurrentRun()
    {
        if (!EnabledByGameplaySetting)
        {
            return false;
        }

        if (!EnableSinglePlayerHint && (RunManager.Instance?.IsSinglePlayerOrFakeMultiplayer ?? false))
        {
            return false;
        }

        return true;
    }

    public static bool IsBubbleEnabledInCurrentRun()
    {
        if (!EnableSinglePlayerHint && (RunManager.Instance?.IsSinglePlayerOrFakeMultiplayer ?? false))
        {
            return false;
        }

        return true;
    }

    public static void SetEnabledByGameplaySetting(bool enabled)
    {
        if (EnabledByGameplaySetting == enabled)
        {
            return;
        }

        EnabledByGameplaySetting = enabled;
        SaveEnabledSettingToConfig();
        LogUi("GameplaySetting.ComboHint", $"enabled={enabled}");
    }

    private static void SaveEnabledSettingToConfig()
    {
        try
        {
            string configPath = Path.Combine(ResolveModRoot(), ConfigFileName);
            JsonObject rootObject;
            if (File.Exists(configPath))
            {
                JsonNode? parsed = JsonNode.Parse(File.ReadAllText(configPath));
                rootObject = parsed as JsonObject ?? new JsonObject();
            }
            else
            {
                rootObject = new JsonObject();
            }

            rootObject[ConfigEnabledKey] = EnabledByGameplaySetting;
            string output = rootObject.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configPath, output);
        }
        catch (Exception ex)
        {
            Log.Warn($"[ComboHint] failed to write {ConfigFileName}: {ex.Message}");
        }
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

            ModRootPath = me.path;

            string configPath = Path.Combine(me.path, ConfigFileName);
            if (!File.Exists(configPath))
            {
                Log.Warn($"[ComboHint] config file not found at {configPath}, trigger groups will be empty.");
                TriggerGroups = new List<TriggerGroup>();
                BubbleDurationSeconds = DefaultBubbleDurationSeconds;
                return;
            }

            string json = File.ReadAllText(configPath);
            using JsonDocument doc = JsonDocument.Parse(json);
            EnabledByGameplaySetting = ReadEnabledSetting(doc.RootElement);
            TriggerGroups = BuildTriggerGroups(doc.RootElement);
            OverlayKillTriggerGroup = ReadOverlayKillTriggerGroup(doc.RootElement);
            OverlayKillTitleColorHex = ReadOverlayKillTitleColor(doc.RootElement);
            BubbleDurationSeconds = ReadBubbleDurationSeconds(doc.RootElement);
            if (TriggerGroups.Count == 0)
            {
                Log.Warn("[ComboHint] no valid trigger groups in config, trigger groups will be empty.");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[ComboHint] failed to load config: {ex}");
            LogErrorToFile("LoadConfig", ex);
            TriggerGroups = new List<TriggerGroup>();
            EnabledByGameplaySetting = true;
            OverlayKillTriggerGroup = TriggerGroup.From(null, "#FF7F50");
            OverlayKillTitleColorHex = DefaultOverlayKillTitleColorHex;
            BubbleDurationSeconds = DefaultBubbleDurationSeconds;
        }
    }

    private static bool ReadEnabledSetting(JsonElement root)
    {
        if (root.TryGetProperty(ConfigEnabledKey, out JsonElement enabledElement))
        {
            if (enabledElement.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (enabledElement.ValueKind == JsonValueKind.False)
            {
                return false;
            }
        }

        return true;
    }

    private static TriggerGroup ReadOverlayKillTriggerGroup(JsonElement root)
    {
        List<string>? triggerTexts = null;
        if (root.TryGetProperty("overlayKillTriggerTexts", out JsonElement triggerTextsElement))
        {
            triggerTexts = ReadStringArray(triggerTextsElement);
        }

        string? color = null;
        if (root.TryGetProperty("overlayKillColor", out JsonElement colorElement) && colorElement.ValueKind == JsonValueKind.String)
        {
            color = colorElement.GetString();
        }

        return TriggerGroup.From(triggerTexts, color);
    }

    private static string ReadOverlayKillTitleColor(JsonElement root)
    {
        if (root.TryGetProperty("overlayKillTitleColor", out JsonElement titleColorElement) && titleColorElement.ValueKind == JsonValueKind.String)
        {
            string? raw = titleColorElement.GetString();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                string trimmed = raw.Trim();
                return trimmed.StartsWith("#", StringComparison.Ordinal) ? trimmed : "#" + trimmed;
            }
        }

        return DefaultOverlayKillTitleColorHex;
    }

    private static List<TriggerGroup> BuildTriggerGroups(JsonElement root)
    {
        List<TriggerGroup> groups = new List<TriggerGroup>();
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (!property.Name.StartsWith("triggerTexts_", StringComparison.Ordinal))
            {
                continue;
            }

            List<string>? triggerTexts = ReadStringArray(property.Value);
            string suffix = property.Name.Substring("triggerTexts_".Length);
            string colorKey = "color_" + suffix;
            string? color = null;
            if (root.TryGetProperty(colorKey, out JsonElement colorElement) && colorElement.ValueKind == JsonValueKind.String)
            {
                color = colorElement.GetString();
            }

            TriggerGroup group = TriggerGroup.From(triggerTexts, color);
            if (group.TriggerTexts.Count > 0)
            {
                groups.Add(group);
            }
        }

        return groups;
    }

    private static List<string>? ReadStringArray(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        List<string> values = new List<string>();
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                string? value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    values.Add(value);
                }
            }
        }

        return values;
    }

    private static double ReadBubbleDurationSeconds(JsonElement root)
    {
        if (!root.TryGetProperty("bubbleDurationSeconds", out JsonElement durationElement))
        {
            return DefaultBubbleDurationSeconds;
        }

        double parsed;
        if (durationElement.ValueKind == JsonValueKind.Number && durationElement.TryGetDouble(out parsed))
        {
            return Math.Clamp(parsed, 0.3, 10.0);
        }

        return DefaultBubbleDurationSeconds;
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardDrawn))]
public static class AfterCardDrawnPatch
{
    private const double DrawBurstDebounceSeconds = 0.3;

    private static readonly Dictionary<ulong, int> PendingDrawVersionByPlayer = new Dictionary<ulong, int>();

    private static readonly Dictionary<ulong, string> LastShownSignatureByPlayer = new Dictionary<ulong, string>();

    private static ulong _lastCombatCreatedMsec;

    public static void EnsureCombatStateFresh()
    {
        ulong createdMsec = NCombatRoom.Instance?.CreatedMsec ?? 0UL;
        if (createdMsec == 0UL || createdMsec == _lastCombatCreatedMsec)
        {
            return;
        }

        lock (PendingDrawVersionByPlayer)
        {
            PendingDrawVersionByPlayer.Clear();
        }

        lock (LastShownSignatureByPlayer)
        {
            LastShownSignatureByPlayer.Clear();
        }

        _lastCombatCreatedMsec = createdMsec;
    }

    public static void Postfix(CardModel card)
    {
        try
        {
            if (card == null || card.Owner?.Creature == null)
            {
                return;
            }

            bool bubbleEnabled = ModEntry.IsBubbleEnabledInCurrentRun();
            bool overlayEnabled = ModEntry.IsOverlayEnabledInCurrentRun();
            if (!bubbleEnabled && !overlayEnabled)
            {
                return;
            }

            EnsureCombatStateFresh();
            if (bubbleEnabled)
            {
                ScheduleHandCheck(card.Owner.Creature);
            }

            if (overlayEnabled)
            {
                ComboHintOverlay.EnsureAttached();
                ComboHintOverlay.Refresh();
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[ComboHint] failed in draw hook: {ex}");
            ModEntry.LogErrorToFile("AfterCardDrawnPatch.Postfix", ex);
        }
    }

    private static void ScheduleHandCheck(Creature ownerCreature)
    {
        ulong netId = ownerCreature.Player?.NetId ?? 0UL;
        int version;
        lock (PendingDrawVersionByPlayer)
        {
            if (!PendingDrawVersionByPlayer.TryGetValue(netId, out int current))
            {
                current = 0;
            }

            version = current + 1;
            PendingDrawVersionByPlayer[netId] = version;
        }

        _ = EmitSingleBubbleAfterDrawBurst(ownerCreature, netId, version);
    }

    private static async Task EmitSingleBubbleAfterDrawBurst(Creature ownerCreature, ulong netId, int expectedVersion)
    {
        SceneTreeTimer timer = ((SceneTree)Engine.GetMainLoop()).CreateTimer(DrawBurstDebounceSeconds);
        await timer.ToSignal(timer, SceneTreeTimer.SignalName.Timeout);

        lock (PendingDrawVersionByPlayer)
        {
            if (!PendingDrawVersionByPlayer.TryGetValue(netId, out int latestVersion) || latestVersion != expectedVersion)
            {
                return;
            }
        }

        IReadOnlyList<CardModel> handCards = ownerCreature.Player?.PlayerCombatState?.Hand?.Cards ?? Array.Empty<CardModel>();
        List<MatchedTrigger> handMatches = FindMatchedTextsInHand(handCards);
        string currentSignature = string.Join("|", handMatches.Select((MatchedTrigger m) => $"{m.ColorHex}:{m.Text}"));

        lock (LastShownSignatureByPlayer)
        {
            if (!LastShownSignatureByPlayer.TryGetValue(netId, out string? lastShown))
            {
                lastShown = string.Empty;
            }

            if (currentSignature == lastShown)
            {
                return;
            }

            LastShownSignatureByPlayer[netId] = currentSignature;
        }

        if (handMatches.Count == 0)
        {
            return;
        }

        string bubbleText = "\u6211\u6709" + string.Join("\u3001", handMatches.Select((MatchedTrigger m) => FormatColoredText(m.Text, m.ColorHex)));
        NSpeechBubbleVfx? bubble = NSpeechBubbleVfx.Create(bubbleText, ownerCreature, ModEntry.BubbleDurationSeconds);
        if (bubble != null)
        {
            NCombatRoom.Instance?.CombatVfxContainer.AddChild(bubble);
        }
    }

    private static List<MatchedTrigger> FindMatchedTextsInHand(IEnumerable<CardModel> handCards)
    {
        List<MatchedTrigger> matches = new List<MatchedTrigger>();
        HashSet<string> dedup = new HashSet<string>(StringComparer.Ordinal);

        foreach (CardModel handCard in handCards)
        {
            string title = handCard.Title ?? string.Empty;
            string description = handCard.GetDescriptionForPile(handCard.Pile?.Type ?? PileType.None);
            foreach (MatchedTrigger matched in FindMatchedTexts(title, description))
            {
                if (dedup.Add(matched.Text))
                {
                    matches.Add(matched);
                }
            }
        }

        return matches;
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

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardPlayed))]
public static class AfterCardPlayedPatch
{
    public static void Postfix(CardPlay cardPlay)
    {
        try
        {
            if (!ModEntry.IsOverlayEnabledInCurrentRun())
            {
                return;
            }

            AfterCardDrawnPatch.EnsureCombatStateFresh();
            ComboHintOverlay.EnsureAttached();
            ComboHintOverlay.Refresh();
        }
        catch (Exception ex)
        {
            Log.Error($"[ComboHint] failed in after card played hook: {ex}");
            ModEntry.LogErrorToFile("AfterCardPlayedPatch.Postfix", ex);
        }
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPotionUsed))]
public static class AfterPotionUsedPatch
{
    public static void Postfix(PotionModel potion)
    {
        try
        {
            if (!ModEntry.IsOverlayEnabledInCurrentRun())
            {
                return;
            }

            AfterCardDrawnPatch.EnsureCombatStateFresh();
            ComboHintOverlay.EnsureAttached();
            ComboHintOverlay.Refresh();
        }
        catch (Exception ex)
        {
            Log.Error($"[ComboHint] failed in after potion used hook: {ex}");
            ModEntry.LogErrorToFile("AfterPotionUsedPatch.Postfix", ex);
        }
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterTurnEnd))]
public static class AfterTurnEndPatch
{
    public static void Postfix()
    {
        try
        {
            ComboHintOverlay.HideTransient("after_turn_end");
        }
        catch (Exception ex)
        {
            ModEntry.LogErrorToFile("AfterTurnEndPatch.Postfix", ex);
        }
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCombatVictory))]
public static class AfterCombatVictoryPatch
{
    public static void Postfix()
    {
        try
        {
            ComboHintOverlay.DisposeActiveNode("after_combat_victory");
        }
        catch (Exception ex)
        {
            ModEntry.LogErrorToFile("AfterCombatVictoryPatch.Postfix", ex);
        }
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCombatEnd))]
public static class AfterCombatEndPatch
{
    public static void Postfix()
    {
        try
        {
            ComboHintOverlay.DisposeActiveNode("after_combat_end");
        }
        catch (Exception ex)
        {
            ModEntry.LogErrorToFile("AfterCombatEndPatch.Postfix", ex);
        }
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterSideTurnStart))]
public static class AfterSideTurnStartPatch
{
    public static void Postfix(CombatState combatState, CombatSide side)
    {
        try
        {
            ComboHintOverlay.OnSideTurnStart(side);
        }
        catch (Exception ex)
        {
            ModEntry.LogUi("AfterSideTurnStartPatch.Error", ex.Message);
        }
    }
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
