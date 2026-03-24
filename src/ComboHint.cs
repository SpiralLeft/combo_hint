using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization;
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
    private const string ManifestFileName = "combo_hint.json";

    private const string UiLogFileName = "combo_hint.ui.log";
    private const string InjectLogFileName = "combo_hint.inject.log";

    private const string ConfigEnabledKey = "comboHintEnabled";
    private const string EnableSinglePlayerHintKey = "enableSinglePlayerHint";
    private const string BubbleEnabledKey = "bubbleHintEnabled";
    private static readonly JsonSerializerOptions PrettyReadableJsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private const double DefaultBubbleDurationSeconds = 1.8;

    private const string DefaultOverlayKillTitleColorHex = "#FF3B30";

    public static IReadOnlyList<TriggerGroup> TriggerGroups { get; private set; } = new List<TriggerGroup>();

    public static TriggerGroup OverlayKillTriggerGroup { get; private set; } = TriggerGroup.From("overlayKill", null, "#FF7F50");

    public static string OverlayKillTitleColorHex { get; private set; } = DefaultOverlayKillTitleColorHex;

    public static double BubbleDurationSeconds { get; private set; } = DefaultBubbleDurationSeconds;

    public static bool EnableSinglePlayerHint { get; private set; } = true;

    public static bool EnableBubbleHint { get; private set; } = true;

    public static bool EnabledByGameplaySetting { get; private set; } = true;

    public static string ModRootPath { get; private set; } = string.Empty;

    private static readonly object EnglishCardTitlesLock = new object();

    private static Dictionary<string, string>? _englishCardTitlesById;

    private static bool _englishCardTitlesLoaded;

    public static void Initialize()
    {
        LoadConfig();
        LoadManifestSettings();
        ResetUiLog();
        ResetInjectLog();

        new Harmony(HarmonyId).PatchAll();
        int totalTriggerCount = TriggerGroups.Sum((TriggerGroup g) => g.TriggerModelIds.Count);
        Log.Info($"[ComboHint] initialized, trigger groups: {TriggerGroups.Count}, trigger model ids: {totalTriggerCount}, enableSinglePlayerHint: {EnableSinglePlayerHint}");
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

    public static bool IsChineseLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return false;
        }

        return language.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            || language.Equals("zhs", StringComparison.OrdinalIgnoreCase)
            || language.Equals("zht", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsChineseUiLanguage()
    {
        string language = LocManager.Instance?.Language ?? "eng";
        return IsChineseLanguage(language);
    }

    public static string GetLocalizedComboHintTitle()
    {
        return IsChineseUiLanguage() ? "连携提示" : "Combo Hints";
    }

    public static string GetLocalizedNoMatchText()
    {
        return IsChineseUiLanguage() ? "当前没有连携效果" : "No combo effects available";
    }

    public static string GetLocalizedKillTitle()
    {
        return IsChineseUiLanguage() ? "斩杀" : "Lethal";
    }

    public static string GetLocalizedHasConnector()
    {
        return IsChineseUiLanguage() ? "有" : " has ";
    }

    public static string GetLocalizedListSeparator()
    {
        return IsChineseUiLanguage() ? "、" : ", ";
    }

    public static string GetLocalizedBubblePrefix()
    {
        return IsChineseUiLanguage() ? "我有" : "I have ";
    }

    public static string GetLocalizedWeakText()
    {
        return IsChineseUiLanguage() ? "虚弱" : "Weak";
    }

    public static string GetLocalizedVulnerableText()
    {
        return IsChineseUiLanguage() ? "易伤" : "Vulnerable";
    }

    public static string GetDisplayCardTitleWithEnglish(CardModel card)
    {
        string modelId = card.Id.Entry;
        string localizedTitle = card.Title ?? modelId;

        string language = LocManager.Instance?.Language ?? "eng";
        if (IsChineseLanguage(language))
        {
            return localizedTitle;
        }

        string? englishTitle = TryGetEnglishCardTitle(modelId);
        if (string.IsNullOrWhiteSpace(englishTitle))
        {
            return localizedTitle;
        }

        return englishTitle;
    }

    private static string? TryGetEnglishCardTitle(string modelId)
    {
        EnsureEnglishCardTitlesLoaded();
        if (_englishCardTitlesById == null)
        {
            return null;
        }

        return _englishCardTitlesById.TryGetValue(modelId, out string? title) ? title : null;
    }

    private static void EnsureEnglishCardTitlesLoaded()
    {
        if (_englishCardTitlesLoaded)
        {
            return;
        }

        lock (EnglishCardTitlesLock)
        {
            if (_englishCardTitlesLoaded)
            {
                return;
            }

            _englishCardTitlesById = LoadEnglishCardTitlesById();
            _englishCardTitlesLoaded = true;
            LogUi("CardTitle.EnglishLoaded", $"count={_englishCardTitlesById.Count}");
        }
    }

    private static Dictionary<string, string> LoadEnglishCardTitlesById()
    {
        List<string> candidates = new List<string>
        {
            Path.Combine(Directory.GetCurrentDirectory(), "localization", "eng", "cards.json"),
            Path.Combine(ResolveModRoot(), "..", "..", "localization", "eng", "cards.json")
        };

        foreach (string candidate in candidates)
        {
            try
            {
                string fullPath = Path.GetFullPath(candidate);
                if (!File.Exists(fullPath))
                {
                    continue;
                }

                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(fullPath));
                Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (JsonProperty property in doc.RootElement.EnumerateObject())
                {
                    if (!property.Name.EndsWith(".title", StringComparison.Ordinal) || property.Value.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    string id = property.Name.Substring(0, property.Name.Length - ".title".Length);
                    string? title = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(title))
                    {
                        map[id] = Regex.Replace(title.Trim(), "\\s+", " ");
                    }
                }

                if (map.Count > 0)
                {
                    return map;
                }
            }
            catch (Exception ex)
            {
                LogUi("CardTitle.EnglishLoadError", ex.Message);
            }
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
                        if (doc.RootElement.TryGetProperty(ConfigEnabledKey, out JsonElement comboHintEnabledElement))
                        {
                            if (comboHintEnabledElement.ValueKind == JsonValueKind.True)
                            {
                                EnabledByGameplaySetting = true;
                            }
                            else if (comboHintEnabledElement.ValueKind == JsonValueKind.False)
                            {
                                EnabledByGameplaySetting = false;
                            }
                        }

                        if (doc.RootElement.TryGetProperty(EnableSinglePlayerHintKey, out JsonElement el))
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

                        if (doc.RootElement.TryGetProperty(BubbleEnabledKey, out JsonElement bubbleEnabledElement))
                        {
                            if (bubbleEnabledElement.ValueKind == JsonValueKind.True)
                            {
                                EnableBubbleHint = true;
                            }
                            else if (bubbleEnabledElement.ValueKind == JsonValueKind.False)
                            {
                                EnableBubbleHint = false;
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
        if (!EnableBubbleHint)
        {
            return false;
        }

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
        SaveManifestBool(ConfigEnabledKey, EnabledByGameplaySetting);
        LogUi("GameplaySetting.ComboHint", $"enabled={enabled}");
    }

    public static void SetEnableSinglePlayerHintByGameplaySetting(bool enabled)
    {
        if (EnableSinglePlayerHint == enabled)
        {
            return;
        }

        EnableSinglePlayerHint = enabled;
        SaveManifestBool(EnableSinglePlayerHintKey, EnableSinglePlayerHint);
        LogUi("GameplaySetting.SinglePlayerComboHint", $"enabled={enabled}");
    }

    public static void SetBubbleHintByGameplaySetting(bool enabled)
    {
        if (EnableBubbleHint == enabled)
        {
            return;
        }

        EnableBubbleHint = enabled;
        SaveManifestBool(BubbleEnabledKey, EnableBubbleHint);
        LogUi("GameplaySetting.BubbleHint", $"enabled={enabled}");
    }

    private static void SaveManifestBool(string key, bool value)
    {
        try
        {
            string configPath = Path.Combine(ResolveModRoot(), ManifestFileName);
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

            rootObject[key] = value;
            string output = rootObject.ToJsonString(PrettyReadableJsonOptions);
            File.WriteAllText(configPath, output);
        }
        catch (Exception ex)
        {
            Log.Warn($"[ComboHint] failed to write {ManifestFileName}: {ex.Message}");
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
            TriggerGroups = BuildTriggerGroups(doc.RootElement);
            OverlayKillTriggerGroup = ReadOverlayKillTriggerGroup(doc.RootElement);
            OverlayKillTitleColorHex = ReadOverlayKillTitleColor(doc.RootElement);
            BubbleDurationSeconds = ReadBubbleDurationSeconds(doc.RootElement);
            RewriteConfigWithReadableChinese(configPath, doc.RootElement);
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
            OverlayKillTriggerGroup = TriggerGroup.From("overlayKill", null, "#FF7F50");
            OverlayKillTitleColorHex = DefaultOverlayKillTitleColorHex;
            BubbleDurationSeconds = DefaultBubbleDurationSeconds;
        }
    }

    private static void RewriteConfigWithReadableChinese(string configPath, JsonElement root)
    {
        try
        {
            JsonNode? node = JsonNode.Parse(root.GetRawText());
            if (node is JsonObject obj)
            {
                obj.Remove(ConfigEnabledKey);
                obj.Remove(BubbleEnabledKey);
                string output = obj.ToJsonString(PrettyReadableJsonOptions);
                File.WriteAllText(configPath, output);
            }
        }
        catch
        {
        }
    }

    private static TriggerGroup ReadOverlayKillTriggerGroup(JsonElement root)
    {
        List<string>? triggerModelIds = null;
        if (root.TryGetProperty("overlayKillTriggerModelIds", out JsonElement triggerModelIdsElement))
        {
            triggerModelIds = ReadStringArray(triggerModelIdsElement);
        }
        else if (root.TryGetProperty("overlayKillTriggerTexts", out JsonElement legacyTriggerTextsElement))
        {
            triggerModelIds = ReadStringArray(legacyTriggerTextsElement);
        }

        string? color = null;
        if (root.TryGetProperty("overlayKillColor", out JsonElement colorElement) && colorElement.ValueKind == JsonValueKind.String)
        {
            color = colorElement.GetString();
        }

        return TriggerGroup.From("overlayKill", triggerModelIds, color);
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
            if (!property.Name.StartsWith("triggerModelIds_", StringComparison.Ordinal))
            {
                continue;
            }

            List<string>? triggerModelIds = ReadStringArray(property.Value);
            string suffix = property.Name.Substring("triggerModelIds_".Length);
            string colorKey = "color_" + suffix;
            string? color = null;
            if (root.TryGetProperty(colorKey, out JsonElement colorElement) && colorElement.ValueKind == JsonValueKind.String)
            {
                color = colorElement.GetString();
            }

            TriggerGroup group = TriggerGroup.From(suffix, triggerModelIds, color);
            if (group.TriggerModelIds.Count > 0)
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

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardChangedPiles))]
public static class AfterCardDrawnPatch
{
    private const double HandEntryDebounceSeconds = 0.3;
    private const string SpecialWeakenGroupKey = "specialweaken";
    private const string SpecialVulnerableGroupKey = "specialvulnerable";

    private static readonly Dictionary<ulong, int> PendingDrawVersionByPlayer = new Dictionary<ulong, int>();

    private static readonly Dictionary<ulong, BubbleWindowSnapshot> ActiveWindowSnapshotByPlayer = new Dictionary<ulong, BubbleWindowSnapshot>();

    private static ulong _lastCombatCreatedMsec;

    private readonly record struct BubbleWindowSnapshot(int WindowStartVersion, string BaselineSignature);

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

        lock (ActiveWindowSnapshotByPlayer)
        {
            ActiveWindowSnapshotByPlayer.Clear();
        }

        _lastCombatCreatedMsec = createdMsec;
    }

    public static void Postfix(CardModel card, PileType oldPile)
    {
        try
        {
            if (card == null || card.Owner?.Creature == null)
            {
                ModEntry.LogUi("BubbleGate.Skip", "reason=card_or_owner_null");
                return;
            }

            bool bubbleEnabled = ModEntry.IsBubbleEnabledInCurrentRun();
            bool overlayEnabled = ModEntry.IsOverlayEnabledInCurrentRun();
            if (!bubbleEnabled && !overlayEnabled)
            {
                ModEntry.LogUi("BubbleGate.Skip", $"reason=all_disabled, card={card.Id.Entry}, oldPile={oldPile}, newPile={card.Pile?.Type}");
                return;
            }

            EnsureCombatStateFresh();

            bool enteredHand = card.Pile?.Type == PileType.Hand && oldPile != PileType.Hand;
            ModEntry.LogUi("BubbleGate.Event", $"card={card.Id.Entry}, oldPile={oldPile}, newPile={card.Pile?.Type}, enteredHand={enteredHand}, bubbleEnabled={bubbleEnabled}, overlayEnabled={overlayEnabled}");
            if (!enteredHand)
            {
                ModEntry.LogUi("BubbleGate.Skip", $"reason=not_entered_hand, card={card.Id.Entry}");
                return;
            }

            if (bubbleEnabled)
            {
                OnCardEnteredHand(card);
            }

            if (overlayEnabled)
            {
                ComboHintOverlay.EnsureAttached();
                ComboHintOverlay.Refresh();
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[ComboHint] failed in hand-entry hook: {ex}");
            ModEntry.LogErrorToFile("AfterCardDrawnPatch.Postfix", ex);
        }
    }

    private static void OnCardEnteredHand(CardModel card)
    {
        Creature ownerCreature = card.Owner!.Creature;
        ulong netId = ownerCreature.Player?.NetId ?? 0UL;

        bool hasActiveWindow;
        lock (PendingDrawVersionByPlayer)
        {
            hasActiveWindow = PendingDrawVersionByPlayer.ContainsKey(netId);
        }

        ModEntry.LogUi("BubbleGate.HandEntry", $"card={card.Id.Entry}, netId={netId}, hasActiveWindow={hasActiveWindow}");

        bool cardMatched = DoesCardMatchAnyTrigger(card);
        if (!hasActiveWindow && !cardMatched)
        {
            ModEntry.LogUi("BubbleGate.Skip", $"reason=first_card_not_matched, card={card.Id.Entry}, netId={netId}");
            return;
        }

        if (!hasActiveWindow && cardMatched)
        {
            ModEntry.LogUi("BubbleGate.WindowStart", $"card={card.Id.Entry}, netId={netId}");
        }
        else if (hasActiveWindow)
        {
            ModEntry.LogUi("BubbleGate.WindowRefresh", $"card={card.Id.Entry}, netId={netId}");
        }

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

        if (!hasActiveWindow && cardMatched)
        {
            IReadOnlyList<CardModel> currentHand = ownerCreature.Player?.PlayerCombatState?.Hand?.Cards ?? Array.Empty<CardModel>();
            List<MatchedTrigger> baselineMatches = FindMatchedTextsInHandExcludingCard(currentHand, card);
            string baselineSignature = BuildSignature(baselineMatches);
            lock (ActiveWindowSnapshotByPlayer)
            {
                ActiveWindowSnapshotByPlayer[netId] = new BubbleWindowSnapshot(version, baselineSignature);
            }
            ModEntry.LogUi("BubbleGate.WindowBaseline", $"netId={netId}, startVersion={version}, baseline={baselineSignature}");
        }

        ModEntry.LogUi("BubbleGate.WindowVersion", $"netId={netId}, version={version}");

        _ = EmitSingleBubbleAfterWindow(ownerCreature, netId, version);
    }

    private static bool DoesCardMatchAnyTrigger(CardModel card)
    {
        int matchCount = FindMatchedTriggersForCard(card).Count;
        ModEntry.LogUi("BubbleGate.CardMatch", $"card={card.Id.Entry}, matchCount={matchCount}");
        return matchCount > 0;
    }

    private static async Task EmitSingleBubbleAfterWindow(Creature ownerCreature, ulong netId, int expectedVersion)
    {
        SceneTreeTimer timer = ((SceneTree)Engine.GetMainLoop()).CreateTimer(HandEntryDebounceSeconds);
        await timer.ToSignal(timer, SceneTreeTimer.SignalName.Timeout);

        lock (PendingDrawVersionByPlayer)
        {
            if (!PendingDrawVersionByPlayer.TryGetValue(netId, out int latestVersion) || latestVersion != expectedVersion)
            {
                ModEntry.LogUi("BubbleGate.WindowCancel", $"netId={netId}, expectedVersion={expectedVersion}");
                return;
            }

            PendingDrawVersionByPlayer.Remove(netId);
        }

        ModEntry.LogUi("BubbleGate.WindowCommit", $"netId={netId}, version={expectedVersion}");

        IReadOnlyList<CardModel> handCards = ownerCreature.Player?.PlayerCombatState?.Hand?.Cards ?? Array.Empty<CardModel>();
        List<MatchedTrigger> handMatches = FindMatchedTextsInHand(handCards);
        string currentSignature = BuildSignature(handMatches);

        BubbleWindowSnapshot? snapshot = null;
        lock (ActiveWindowSnapshotByPlayer)
        {
            if (ActiveWindowSnapshotByPlayer.TryGetValue(netId, out BubbleWindowSnapshot value))
            {
                snapshot = value;
            }

            ActiveWindowSnapshotByPlayer.Remove(netId);
        }

        ModEntry.LogUi("BubbleGate.FinalScan", $"netId={netId}, handCount={handCards.Count}, matchCount={handMatches.Count}, signature={currentSignature}, baseline={(snapshot?.BaselineSignature ?? "<none>")}");

        if (snapshot.HasValue && currentSignature == snapshot.Value.BaselineSignature)
        {
            ModEntry.LogUi("BubbleGate.Skip", $"reason=window_no_change, netId={netId}, startVersion={snapshot.Value.WindowStartVersion}");
            return;
        }

        if (handMatches.Count == 0)
        {
            ModEntry.LogUi("BubbleGate.Skip", $"reason=no_matches_after_window, netId={netId}");
            return;
        }

        string bubbleText = ModEntry.GetLocalizedBubblePrefix() + string.Join(ModEntry.GetLocalizedListSeparator(), handMatches.Select((MatchedTrigger m) => FormatColoredText(m.Text, m.ColorHex)));
        NSpeechBubbleVfx? bubble = NSpeechBubbleVfx.Create(bubbleText, ownerCreature, ModEntry.BubbleDurationSeconds);
        if (bubble != null)
        {
            NCombatRoom.Instance?.CombatVfxContainer.AddChild(bubble);
            ModEntry.LogUi("BubbleGate.Emit", $"netId={netId}, text={bubbleText}");
        }
        else
        {
            ModEntry.LogUi("BubbleGate.Skip", $"reason=bubble_create_failed, netId={netId}");
        }
    }

    private static List<MatchedTrigger> FindMatchedTextsInHand(IEnumerable<CardModel> handCards)
    {
        List<MatchedTrigger> matches = new List<MatchedTrigger>();
        HashSet<string> dedup = new HashSet<string>(StringComparer.Ordinal);

        foreach (CardModel handCard in handCards)
        {
            foreach (MatchedTrigger matched in FindMatchedTriggersForCard(handCard))
            {
                if (dedup.Add(BuildMatchDedupKey(matched)))
                {
                    matches.Add(matched);
                }
            }
        }

        return matches;
    }

    private static List<MatchedTrigger> FindMatchedTextsInHandExcludingCard(IEnumerable<CardModel> handCards, CardModel excludedCard)
    {
        List<MatchedTrigger> matches = new List<MatchedTrigger>();
        HashSet<string> dedup = new HashSet<string>(StringComparer.Ordinal);

        foreach (CardModel handCard in handCards)
        {
            if (ReferenceEquals(handCard, excludedCard))
            {
                continue;
            }

            foreach (MatchedTrigger matched in FindMatchedTriggersForCard(handCard))
            {
                if (dedup.Add(BuildMatchDedupKey(matched)))
                {
                    matches.Add(matched);
                }
            }
        }

        return matches;
    }

    private static string BuildSignature(IEnumerable<MatchedTrigger> matches)
    {
        return string.Join("|", matches.Select((MatchedTrigger m) => $"{m.ColorHex}:{m.Text}"));
    }

    private static List<MatchedTrigger> FindMatchedTriggersForCard(CardModel card)
    {
        List<MatchedTrigger> matches = new List<MatchedTrigger>();
        string modelId = card.Id.Entry;
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return matches;
        }

        string title = ModEntry.GetDisplayCardTitleWithEnglish(card);
        HashSet<string> dedup = new HashSet<string>(StringComparer.Ordinal);
        foreach (TriggerGroup group in ModEntry.TriggerGroups)
        {
            if (!group.TriggerModelIds.Contains(modelId))
            {
                continue;
            }

            string displayText;
            if (group.Key.Equals(SpecialWeakenGroupKey, StringComparison.OrdinalIgnoreCase))
            {
                displayText = ModEntry.GetLocalizedWeakText();
            }
            else if (group.Key.Equals(SpecialVulnerableGroupKey, StringComparison.OrdinalIgnoreCase))
            {
                displayText = ModEntry.GetLocalizedVulnerableText();
            }
            else
            {
                displayText = title;
            }

            string dedupKey = group.ColorHex + "|" + displayText;
            if (dedup.Add(dedupKey))
            {
                matches.Add(new MatchedTrigger(displayText, group.ColorHex));
            }
        }
        return matches;
    }

    private static string BuildMatchDedupKey(MatchedTrigger match)
    {
        return match.ColorHex + "|" + match.Text;
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
    public string Key { get; }

    public IReadOnlySet<string> TriggerModelIds { get; }

    public string ColorHex { get; }

    private TriggerGroup(string key, IReadOnlySet<string> triggerModelIds, string colorHex)
    {
        Key = key;
        TriggerModelIds = triggerModelIds;
        ColorHex = colorHex;
    }

    public static TriggerGroup From(string key, List<string>? rawModelIds, string? rawColor)
    {
        HashSet<string> triggerModelIds = rawModelIds?.Where((string s) => !string.IsNullOrWhiteSpace(s)).Select((string s) => s.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string colorHex = NormalizeColor(rawColor);
        return new TriggerGroup(key, triggerModelIds, colorHex);
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
