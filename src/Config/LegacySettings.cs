using System.Text.Json;

namespace ComboHint;

public sealed record LegacySettings(bool? Overlay, bool? SinglePlayer, bool? Bubbles)
{
    public static LegacySettings Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return new LegacySettings(Read(root, "comboHintEnabled"), Read(root, "enableSinglePlayerHint"), Read(root, "bubbleHintEnabled"));
    }

    private static bool? Read(JsonElement root, string key) =>
        root.TryGetProperty(key, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean() : null;
}
