using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace ComboHint;

// Explicit reviewed Model IDs; runtime matching does not infer effects from card text.
public sealed record HintRules(IReadOnlyList<TriggerGroup> Groups, TriggerGroup KillGroup,
    string KillTitleColor, double BubbleDurationSeconds)
{
    public static HintRules Empty { get; } = new(
        Array.Empty<TriggerGroup>(), TriggerGroup.From("overlayKill", null, "#FF7F50"), "#FF3B30", 1.8);

    public string EffectColor(string id, string? fallback = null)
    {
        var (group, defaultColor) = id switch
        {
            "WEAK_POWER" => ("specialweaken", "#6EC1FF"),
            "VULNERABLE_POWER" => ("specialvulnerable", "#FF6B6B"),
            "WEAKENING" => ("weakenattack", "#E6A23C"),
            "POISON_POWER" => ("poison", "#78C850"),
            "DOOM_POWER" => ("doom", "#B780E6"),
            _ => ("other", "#C9A6FF")
        };
        return Groups.FirstOrDefault(g => g.Key.Equals(group, StringComparison.OrdinalIgnoreCase))?.ColorHex
            ?? fallback ?? defaultColor;
    }

    public static HintRules Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var groups = new List<TriggerGroup>();
        foreach (var property in root.EnumerateObject())
        {
            if (!property.Name.StartsWith("triggerModelIds_", StringComparison.Ordinal)) continue;
            var key = property.Name.Substring("triggerModelIds_".Length);
            var group = TriggerGroup.From(key, ReadStrings(property.Value), ReadString(root, "color_" + key));
            if (group.TriggerModelIds.Count > 0) groups.Add(group);
        }

        List<string>? killIds = null;
        if (root.TryGetProperty("overlayKillTriggerModelIds", out var ids)) killIds = ReadStrings(ids);
        else if (root.TryGetProperty("overlayKillTriggerTexts", out var legacy)) killIds = ReadStrings(legacy);
        var titleColor = ReadString(root, "overlayKillTitleColor");
        titleColor = string.IsNullOrWhiteSpace(titleColor) ? Empty.KillTitleColor : titleColor.Trim();
        if (!titleColor.StartsWith("#", StringComparison.Ordinal)) titleColor = "#" + titleColor;
        double duration = Empty.BubbleDurationSeconds;
        if (root.TryGetProperty("bubbleDurationSeconds", out var value)
            && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double seconds)
            && double.IsFinite(seconds)) duration = Math.Clamp(seconds, 0.3, 10.0);
        return new HintRules(groups,
            TriggerGroup.From("overlayKill", killIds, ReadString(root, "overlayKillColor")), titleColor, duration);
    }

    private static string? ReadString(JsonElement root, string key) =>
        root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static List<string>? ReadStrings(JsonElement element) => element.ValueKind != JsonValueKind.Array
        ? null : element.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String)
            .Select(v => v.GetString()).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).ToList();
}

public sealed class TriggerGroup
{
    public string Key { get; }
    public IReadOnlySet<string> TriggerModelIds { get; }
    public string ColorHex { get; }

    private TriggerGroup(string key, IReadOnlySet<string> modelIds, string colorHex)
    {
        Key = key;
        TriggerModelIds = modelIds;
        ColorHex = colorHex;
    }

    public static TriggerGroup From(string key, List<string>? rawModelIds, string? rawColor) =>
        new(key, new HashSet<string>((rawModelIds ?? new List<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase),
            NormalizeColor(rawColor));

    private static string NormalizeColor(string? rawColor)
    {
        if (string.IsNullOrWhiteSpace(rawColor)) return "#FFFFFF";
        string value = rawColor.Trim();
        return value.StartsWith("#", StringComparison.Ordinal) ? value : "#" + value;
    }
}
