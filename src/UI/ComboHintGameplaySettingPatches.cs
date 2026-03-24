using HarmonyLib;
using Godot;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;

namespace ComboHint;

[HarmonyPatch(typeof(NSettingsScreen), "_Ready")]
public static class ComboHintSettingsScreenReadyPatch
{
    public static void Postfix(NSettingsScreen __instance)
    {
        try
        {
            ModEntry.LogInject("Patch.SettingsReady", $"screen={__instance.Name}, visible={__instance.Visible}");
            ComboHintSettingsInjector.TryInject(__instance);
        }
        catch (System.Exception ex)
        {
            ModEntry.LogInject("Patch.SettingsReady.Error", ex.ToString());
        }
    }
}

[HarmonyPatch(typeof(NSettingsScreen), nameof(NSettingsScreen.OnSubmenuOpened))]
public static class ComboHintSettingsScreenOpenedPatch
{
    public static void Postfix(NSettingsScreen __instance)
    {
        try
        {
            ModEntry.LogInject("Patch.SettingsOpened", $"screen={__instance.Name}, visible={__instance.Visible}");
            ComboHintSettingsInjector.TryInject(__instance);
        }
        catch (System.Exception ex)
        {
            ModEntry.LogInject("Patch.SettingsOpened.Error", ex.ToString());
        }
    }
}

[HarmonyPatch(typeof(NFastModeTickbox), nameof(NFastModeTickbox.SetFromSettings))]
public static class ComboHintFastModeTickboxSetFromSettingsPatch
{
    public static bool Prefix(NFastModeTickbox __instance)
    {
        if (__instance.Name == ComboHintSettingsInjector.ComboHintTickboxName)
        {
            __instance.IsTicked = ModEntry.EnabledByGameplaySetting;
            ModEntry.LogInject("ComboHintTickbox.SetFromSettings", $"isTicked={__instance.IsTicked}");
            return false;
        }

        if (__instance.Name == ComboHintSettingsInjector.SinglePlayerComboHintTickboxName)
        {
            __instance.IsTicked = ModEntry.EnableSinglePlayerHint;
            ModEntry.LogInject("SinglePlayerComboHintTickbox.SetFromSettings", $"isTicked={__instance.IsTicked}");
            return false;
        }

        if (__instance.Name == ComboHintSettingsInjector.BubbleHintTickboxName)
        {
            __instance.IsTicked = ModEntry.EnableBubbleHint;
            ModEntry.LogInject("BubbleHintTickbox.SetFromSettings", $"isTicked={__instance.IsTicked}");
            return false;
        }

        return true;
    }
}

[HarmonyPatch(typeof(NFastModeTickbox), "OnTick")]
public static class ComboHintFastModeTickboxOnTickPatch
{
    public static bool Prefix(NFastModeTickbox __instance)
    {
        if (__instance.Name == ComboHintSettingsInjector.ComboHintTickboxName)
        {
            ModEntry.LogInject("ComboHintTickbox.OnTick", "enabled=true");
            ModEntry.SetEnabledByGameplaySetting(true);
            ComboHintSettingsInjector.ShowSettingsToast(__instance, ComboHintSettingsInjector.ToastOverlayOnKey);
            ComboHintOverlay.RefreshImmediatelyForCurrentTurn("settings_toggle_on");
            return false;
        }

        if (__instance.Name == ComboHintSettingsInjector.SinglePlayerComboHintTickboxName)
        {
            ModEntry.LogInject("SinglePlayerComboHintTickbox.OnTick", "enabled=true");
            ModEntry.SetEnableSinglePlayerHintByGameplaySetting(true);
            ComboHintSettingsInjector.ShowSettingsToast(__instance, ComboHintSettingsInjector.ToastSinglePlayerOnKey);
            ComboHintOverlay.RefreshImmediatelyForCurrentTurn("single_player_setting_toggle_on");
            return false;
        }

        if (__instance.Name == ComboHintSettingsInjector.BubbleHintTickboxName)
        {
            ModEntry.LogInject("BubbleHintTickbox.OnTick", "enabled=true");
            ModEntry.SetBubbleHintByGameplaySetting(true);
            ComboHintSettingsInjector.ShowSettingsToast(__instance, ComboHintSettingsInjector.ToastBubbleOnKey);
            return false;
        }

        return true;
    }
}

[HarmonyPatch(typeof(NFastModeTickbox), "OnUntick")]
public static class ComboHintFastModeTickboxOnUntickPatch
{
    public static bool Prefix(NFastModeTickbox __instance)
    {
        if (__instance.Name == ComboHintSettingsInjector.ComboHintTickboxName)
        {
            ModEntry.LogInject("ComboHintTickbox.OnUntick", "enabled=false");
            ModEntry.SetEnabledByGameplaySetting(false);
            ComboHintSettingsInjector.ShowSettingsToast(__instance, ComboHintSettingsInjector.ToastOverlayOffKey);
            ComboHintOverlay.HideTransient("settings_toggle_off");
            return false;
        }

        if (__instance.Name == ComboHintSettingsInjector.SinglePlayerComboHintTickboxName)
        {
            ModEntry.LogInject("SinglePlayerComboHintTickbox.OnUntick", "enabled=false");
            ModEntry.SetEnableSinglePlayerHintByGameplaySetting(false);
            ComboHintSettingsInjector.ShowSettingsToast(__instance, ComboHintSettingsInjector.ToastSinglePlayerOffKey);
            ComboHintOverlay.HideTransient("single_player_setting_toggle_off");
            return false;
        }

        if (__instance.Name == ComboHintSettingsInjector.BubbleHintTickboxName)
        {
            ModEntry.LogInject("BubbleHintTickbox.OnUntick", "enabled=false");
            ModEntry.SetBubbleHintByGameplaySetting(false);
            ComboHintSettingsInjector.ShowSettingsToast(__instance, ComboHintSettingsInjector.ToastBubbleOffKey);
            return false;
        }

        return true;
    }
}

public static class ComboHintSettingsInjector
{
    private const string ComboHintHeaderRowName = "ComboHintHeader";
    private const string ComboHintRowName = "ComboHint";
    private const string SinglePlayerComboHintRowName = "SinglePlayerComboHint";
    private const string BubbleHintRowName = "BubbleHint";

    private const string ComboHintHeaderDividerName = "ComboHintHeaderDivider";
    private const string ComboHintDividerName = "ComboHintDivider";
    private const string SinglePlayerComboHintDividerName = "SinglePlayerComboHintDivider";
    private const string BubbleHintDividerName = "BubbleHintDivider";

    public const string ComboHintTickboxName = "ComboHintTickbox";
    public const string SinglePlayerComboHintTickboxName = "SinglePlayerComboHintTickbox";
    public const string BubbleHintTickboxName = "BubbleHintTickbox";

    public const string ToastOverlayOnKey = "TOAST_COMBO_HINT_OVERLAY_ON";
    public const string ToastOverlayOffKey = "TOAST_COMBO_HINT_OVERLAY_OFF";
    public const string ToastSinglePlayerOnKey = "TOAST_COMBO_HINT_SINGLE_PLAYER_ON";
    public const string ToastSinglePlayerOffKey = "TOAST_COMBO_HINT_SINGLE_PLAYER_OFF";
    public const string ToastBubbleOnKey = "TOAST_COMBO_HINT_BUBBLE_ON";
    public const string ToastBubbleOffKey = "TOAST_COMBO_HINT_BUBBLE_OFF";

    private const string HoverTipScenePath = "res://scenes/ui/hover_tip.tscn";

    private const string ComboHintHoverTipTitle = "连携提示框";
    private const string ComboHintHoverTipDescription = "启用右上角连携提示框。";
    private const string SinglePlayerComboHintHoverTipTitle = "单人连携";
    private const string SinglePlayerComboHintHoverTipDescription = "开启后游玩单人模式时启用ComboHint。";
    private const string BubbleHintHoverTipTitle = "气泡开关";
    private const string BubbleHintHoverTipDescription = "启用角色对话气泡连携提示。";

    private const string HoverTipBoundMetaKey = "combo_hint_hover_tip_bound";
    private const string HoverTipTitleMetaKey = "combo_hint_hover_tip_title";
    private const string HoverTipDescriptionMetaKey = "combo_hint_hover_tip_description";

    private const float ComboHintHoverTipWidth = 360f;
    private const int ComboHintLabelRightNudgePx = 1;
    private const int ComboHintTickboxLeftNudgePx = 1;
    private const string TickboxMaterialBoundMetaKey = "combo_hint_tickbox_material_bound";

    private static readonly Dictionary<Control, Control> ActiveHoverTipsByRow = new Dictionary<Control, Control>();
    private static bool _toastLocalizationRegistered;
    private static string _toastLocalizationLanguage = string.Empty;

    private static readonly Dictionary<string, string> ToastZh = new Dictionary<string, string>
    {
        { ToastOverlayOnKey, "战斗场景中右上方显示连携提示框" },
        { ToastOverlayOffKey, "已关闭右上方连携提示框" },
        { ToastSinglePlayerOnKey, "单人模式下启用连携提示" },
        { ToastSinglePlayerOffKey, "单人模式下不使用连携提示" },
        { ToastBubbleOnKey, "开启角色气泡连携提示" },
        { ToastBubbleOffKey, "关闭角色气泡连携提示" }
    };

    private static readonly Dictionary<string, string> ToastEn = new Dictionary<string, string>
    {
        { ToastOverlayOnKey, "Display combo hints in the top-right corner of battle scenes" },
        { ToastOverlayOffKey, "Combo hints in the top-right corner of battle scenes disabled" },
        { ToastSinglePlayerOnKey, "Enable combo hints in single-player mode" },
        { ToastSinglePlayerOffKey, "Disable combo hints in single-player mode" },
        { ToastBubbleOnKey, "Enable character bubble combo hints" },
        { ToastBubbleOffKey, "Disable character bubble combo hints" }
    };

    private static void EnsureToastLocalizationRegistered()
    {
        try
        {
            LocTable table = LocManager.Instance.GetTable("settings_ui");
            string language = LocManager.Instance.Language ?? "eng";
            bool hasOverlayOnKey = LocString.Exists("settings_ui", ToastOverlayOnKey);
            if (_toastLocalizationRegistered && _toastLocalizationLanguage == language && hasOverlayOnKey)
            {
                return;
            }

            Dictionary<string, string> selected = language == "eng" ? ToastEn : ToastZh;
            table.MergeWith(selected);
            _toastLocalizationRegistered = true;
            _toastLocalizationLanguage = language;
            ModEntry.LogUi("SettingsToast.Register", $"language={language}, keyCount={selected.Count}");
        }
        catch (System.Exception ex)
        {
            ModEntry.LogUi("SettingsToast.RegisterError", ex.Message);
        }
    }

    public static void ShowSettingsToast(NFastModeTickbox tickbox, string key)
    {
        EnsureToastLocalizationRegistered();

        NSettingsScreen? screen = tickbox.GetAncestorOfType<NSettingsScreen>();
        if (screen == null)
        {
            ModEntry.LogInject("Injector.Toast.Skip", $"tickbox={tickbox.Name}, reason=no_settings_screen, key={key}");
            ModEntry.LogUi("SettingsToast.Skip", $"tickbox={tickbox.Name}, reason=no_settings_screen, key={key}");
            return;
        }

        string resolvedKey = key;
        if (!LocString.Exists("settings_ui", resolvedKey))
        {
            resolvedKey = "TOAST_NOT_IMPLEMENTED";
            ModEntry.LogUi("SettingsToast.Fallback", $"missingKey={key}, fallback={resolvedKey}");
        }

        try
        {
            screen.ShowToast(new LocString("settings_ui", resolvedKey));
            ModEntry.LogUi("SettingsToast.Show", $"tickbox={tickbox.Name}, key={resolvedKey}");
        }
        catch (System.Exception ex)
        {
            ModEntry.LogUi("SettingsToast.Error", $"tickbox={tickbox.Name}, key={resolvedKey}, error={ex.Message}");
        }
    }

    public static void TryInject(NSettingsScreen screen)
    {
        try
        {
            ModEntry.LogInject("Injector.TryInject.Start", $"screen={screen.Name}");
            NSettingsPanel? panel = screen.GetNodeOrNull<NSettingsPanel>("%GeneralSettings");
            if (panel == null)
            {
                ModEntry.LogInject("Injector.TryInject.Skip", "general settings panel not found");
                return;
            }

            VBoxContainer content = panel.Content;
            ModEntry.LogInject("Injector.TryInject.Panel", $"contentNode={content?.Name}, childCount={content?.GetChildCount()}");
            LogDirectChildren(content);
            if (content == null)
            {
                ModEntry.LogInject("Injector.TryInject.Skip", "contentNull=true");
                return;
            }

            Node? fastModeNode = content.GetChildren().FirstOrDefault((Node n) => n.Name == "FastMode");
            if (fastModeNode is not Control fastModeRow)
            {
                ModEntry.LogInject("Injector.TryInject.Skip", "FastMode row not found by child-name scan");
                return;
            }
            ModEntry.LogInject("Injector.TryInject.FastMode", $"rowName={fastModeRow.Name}, fastModeIndex={fastModeRow.GetIndex()}, rowType={fastModeRow.GetType().Name}");

            NFastModeTickbox? fastModeTickbox = fastModeRow.FindChild("FastModeTickbox", true, false) as NFastModeTickbox;
            if (fastModeTickbox == null)
            {
                ModEntry.LogInject("Injector.TryInject.Skip", "FastMode tickbox not found in source row");
                return;
            }

            (Control Row, NFastModeTickbox Tickbox) comboSetting = EnsureSettingRow(
                content,
                fastModeRow,
                ComboHintRowName,
                ComboHintDividerName,
                ComboHintTickboxName,
                "连携提示框",
                ComboHintHoverTipTitle,
                ComboHintHoverTipDescription);

            Control comboHeaderRow = EnsureHeaderRow(
                content,
                fastModeRow,
                ComboHintHeaderRowName,
                ComboHintHeaderDividerName,
                "ComboHint设置");

            (Control Row, NFastModeTickbox Tickbox) singlePlayerSetting = EnsureSettingRow(
                content,
                fastModeRow,
                SinglePlayerComboHintRowName,
                SinglePlayerComboHintDividerName,
                SinglePlayerComboHintTickboxName,
                "单人连携",
                SinglePlayerComboHintHoverTipTitle,
                SinglePlayerComboHintHoverTipDescription);

            (Control Row, NFastModeTickbox Tickbox) bubbleSetting = EnsureSettingRow(
                content,
                fastModeRow,
                BubbleHintRowName,
                BubbleHintDividerName,
                BubbleHintTickboxName,
                "气泡开关",
                BubbleHintHoverTipTitle,
                BubbleHintHoverTipDescription);

            // Keep both settings rows anchored at the end of GeneralSettings.
            MoveRowWithDividerToBottom(content, fastModeRow, comboHeaderRow, ComboHintHeaderDividerName);
            MoveRowWithDividerToBottom(content, fastModeRow, comboSetting.Row, ComboHintDividerName);
            MoveRowWithDividerToBottom(content, fastModeRow, singlePlayerSetting.Row, SinglePlayerComboHintDividerName);
            MoveRowWithDividerToBottom(content, fastModeRow, bubbleSetting.Row, BubbleHintDividerName);

            comboSetting.Tickbox.CallDeferred(nameof(NFastModeTickbox.SetFromSettings));
            singlePlayerSetting.Tickbox.CallDeferred(nameof(NFastModeTickbox.SetFromSettings));
            bubbleSetting.Tickbox.CallDeferred(nameof(NFastModeTickbox.SetFromSettings));

            Control? showRunTimerTickbox = content.GetNodeOrNull<Control>("ShowRunTimer/SettingsTickbox");
            if (showRunTimerTickbox == null)
            {
                showRunTimerTickbox = FindNextTickboxBelow(content, bubbleSetting.Row);
            }

            fastModeTickbox.FocusNeighborBottom = comboSetting.Tickbox.GetPath();
            comboSetting.Tickbox.FocusNeighborTop = fastModeTickbox.GetPath();
            comboSetting.Tickbox.FocusNeighborBottom = singlePlayerSetting.Tickbox.GetPath();
            singlePlayerSetting.Tickbox.FocusNeighborTop = comboSetting.Tickbox.GetPath();
            singlePlayerSetting.Tickbox.FocusNeighborBottom = bubbleSetting.Tickbox.GetPath();
            bubbleSetting.Tickbox.FocusNeighborTop = singlePlayerSetting.Tickbox.GetPath();
            bubbleSetting.Tickbox.FocusNeighborBottom = showRunTimerTickbox?.GetPath() ?? bubbleSetting.Tickbox.GetPath();

            if (showRunTimerTickbox != null)
            {
                showRunTimerTickbox.FocusNeighborTop = bubbleSetting.Tickbox.GetPath();
            }

            RefreshPanelSizeAfterInjection(panel, content);

            ModEntry.LogInject("Injector.TryInject.Success", $"comboTickbox={comboSetting.Tickbox.GetPath()}, singlePlayerTickbox={singlePlayerSetting.Tickbox.GetPath()}, bubbleTickbox={bubbleSetting.Tickbox.GetPath()}, showRunTimerFound={showRunTimerTickbox != null}");
        }
        catch (System.Exception ex)
        {
            ModEntry.LogInject("Injector.TryInject.Error", ex.ToString());
        }
    }

    private static (Control Row, NFastModeTickbox Tickbox) EnsureSettingRow(
        VBoxContainer content,
        Control sourceRow,
        string rowName,
        string dividerName,
        string tickboxName,
        string labelText,
        string hoverTitle,
        string hoverDescription)
    {
        Control? existingRow = content.GetNodeOrNull<Control>(rowName);
        if (existingRow is MarginContainer existingMarginRow)
        {
            ApplyRowHorizontalNudges(existingMarginRow, sourceRow);
            EnsureHoverTipForRow(existingMarginRow, hoverTitle, hoverDescription);
            EnsureRowLabel(existingMarginRow, labelText);

            NFastModeTickbox? existingTickbox = existingMarginRow.FindChild(tickboxName, true, false) as NFastModeTickbox;
            if (existingTickbox == null)
            {
                existingTickbox = existingMarginRow.FindChild("FastModeTickbox", true, false) as NFastModeTickbox;
                if (existingTickbox != null)
                {
                    existingTickbox.Name = tickboxName;
                }
            }

            if (existingTickbox != null)
            {
                MakeTickboxVisualMaterialUnique(existingTickbox);
                return (existingMarginRow, existingTickbox);
            }

            RemoveTickboxesFromRow(existingMarginRow);
        }

        MarginContainer row = new MarginContainer();
        CopyControlLayout(sourceRow, row);
        ApplyRowHorizontalNudges(row, sourceRow);
        row.MouseFilter = sourceRow.MouseFilter;
        row.FocusMode = sourceRow.FocusMode;
        row.Name = rowName;

        Control? sourceLabel = sourceRow.FindChild("Label", true, false) as Control;
        if (sourceLabel == null)
        {
            throw new System.InvalidOperationException("FastMode label not found");
        }

        Node sourceLabelCopy = sourceLabel.Duplicate();
        row.AddChild(sourceLabelCopy);
        if (sourceLabelCopy is MegaRichTextLabel label)
        {
            label.Text = labelText;
        }

        NFastModeTickbox? sourceTickbox = sourceRow.FindChild("FastModeTickbox", true, false) as NFastModeTickbox;
        if (sourceTickbox == null)
        {
            throw new System.InvalidOperationException("FastMode tickbox not found in source row");
        }

        Node copiedTickboxNode = sourceTickbox.Duplicate();
        row.AddChild(copiedTickboxNode);
        if (copiedTickboxNode is not NFastModeTickbox tickbox)
        {
            throw new System.InvalidOperationException("copied FastMode tickbox cast failed");
        }

        tickbox.Name = tickboxName;
        MakeTickboxVisualMaterialUnique(tickbox);

        EnsureHoverTipForRow(row, hoverTitle, hoverDescription);

        ColorRect divider = GetOrCreateDivider(content, sourceRow, dividerName);
        if (divider.GetParent() != content)
        {
            content.AddChild(divider);
        }

        if (row.GetParent() != content)
        {
            content.AddChild(row);
        }

        ModEntry.LogInject("Injector.TryInject.Inserted", $"row={rowName}, dividerIndex={divider.GetIndex()}, rowIndex={row.GetIndex()}");
        return (row, tickbox);
    }

    private static Control EnsureHeaderRow(
        VBoxContainer content,
        Control sourceRow,
        string rowName,
        string dividerName,
        string labelText)
    {
        Control? existingRow = content.GetNodeOrNull<Control>(rowName);
        if (existingRow is MarginContainer existingMarginRow)
        {
            ApplyRowHorizontalNudges(existingMarginRow, sourceRow);
            EnsureRowLabel(existingMarginRow, labelText);
            RemoveTickboxesFromRow(existingMarginRow);
            return existingMarginRow;
        }

        MarginContainer row = new MarginContainer();
        CopyControlLayout(sourceRow, row);
        ApplyRowHorizontalNudges(row, sourceRow);
        row.MouseFilter = sourceRow.MouseFilter;
        row.FocusMode = sourceRow.FocusMode;
        row.Name = rowName;

        Control? sourceLabel = sourceRow.FindChild("Label", true, false) as Control;
        if (sourceLabel == null)
        {
            throw new System.InvalidOperationException("FastMode label not found");
        }

        Node sourceLabelCopy = sourceLabel.Duplicate();
        row.AddChild(sourceLabelCopy);
        if (sourceLabelCopy is MegaRichTextLabel label)
        {
            label.Text = labelText;
        }

        RemoveTickboxesFromRow(row);
        ColorRect divider = GetOrCreateDivider(content, sourceRow, dividerName);
        if (divider.GetParent() != content)
        {
            content.AddChild(divider);
        }

        if (row.GetParent() != content)
        {
            content.AddChild(row);
        }

        ModEntry.LogInject("Injector.TryInject.Inserted", $"row={rowName}, dividerIndex={divider.GetIndex()}, rowIndex={row.GetIndex()}");
        return row;
    }

    private static void MoveRowWithDividerToBottom(VBoxContainer content, Control sourceRow, Control row, string dividerName)
    {
        ColorRect divider = GetOrCreateDivider(content, sourceRow, dividerName);
        if (divider.GetParent() != content)
        {
            content.AddChild(divider);
        }

        if (row.GetParent() != content)
        {
            content.AddChild(row);
        }

        content.MoveChild(divider, content.GetChildCount() - 1);
        content.MoveChild(row, content.GetChildCount() - 1);
        ModEntry.LogInject("Injector.TryInject.BottomAnchor", $"row={row.Name}, divider={divider.Name}, rowIndex={row.GetIndex()}");
    }

    private static void RefreshPanelSizeAfterInjection(NSettingsPanel panel, VBoxContainer content)
    {
        Control? parent = panel.GetParent<Control>();
        if (parent == null)
        {
            return;
        }

        const float minPadding = 50f;
        Vector2 parentSize = parent.Size;
        Vector2 minimumSize = content.GetMinimumSize();
        float targetHeight = minimumSize.Y;
        if (minimumSize.Y + minPadding >= parentSize.Y)
        {
            targetHeight = minimumSize.Y + parentSize.Y * 0.4f;
        }

        panel.Size = new Vector2(content.Size.X, targetHeight);
        ModEntry.LogInject("Injector.TryInject.PanelResized", $"panelHeight={targetHeight}, contentMinY={minimumSize.Y}, parentY={parentSize.Y}");
    }

    private static ColorRect GetOrCreateDivider(VBoxContainer content, Control sourceRow, string dividerName)
    {
        if (content.GetNodeOrNull<ColorRect>(dividerName) is ColorRect existing)
        {
            return existing;
        }

        ColorRect divider = new ColorRect();
        Node? sourceDividerNode = sourceRow.GetIndex() > 0 ? content.GetChild(sourceRow.GetIndex() - 1) : null;
        if (sourceDividerNode is not ColorRect)
        {
            sourceDividerNode = content.GetNodeOrNull("CombatSpeedDivider");
        }

        if (sourceDividerNode is ColorRect sourceDivider)
        {
            Node? dividerCopy = sourceDivider.Duplicate();
            if (dividerCopy is ColorRect copied)
            {
                divider = copied;
            }
        }

        divider.Name = dividerName;
        return divider;
    }

    private static Control? FindNextTickboxBelow(VBoxContainer content, Control currentRow)
    {
        int rowIndex = currentRow.GetIndex();
        for (int i = rowIndex + 1; i < content.GetChildCount(); i++)
        {
            if (content.GetChild(i) is not Control row)
            {
                continue;
            }

            Control? next = row.GetChildren().OfType<Control>().FirstOrDefault((Control c) => c is NSettingsTickbox);
            if (next != null)
            {
                return next;
            }
        }

        return null;
    }

    private static void LogDirectChildren(VBoxContainer content)
    {
        List<string> names = new List<string>();
        foreach (Node child in content.GetChildren())
        {
            names.Add($"{child.Name}:{child.GetType().Name}");
        }

        ModEntry.LogInject("Injector.TryInject.Children", string.Join(" | ", names));
    }

    private static void CopyControlLayout(Control source, Control target)
    {
        target.CustomMinimumSize = source.CustomMinimumSize;
        target.SizeFlagsHorizontal = source.SizeFlagsHorizontal;
        target.SizeFlagsVertical = source.SizeFlagsVertical;
        target.AnchorLeft = source.AnchorLeft;
        target.AnchorTop = source.AnchorTop;
        target.AnchorRight = source.AnchorRight;
        target.AnchorBottom = source.AnchorBottom;
        target.OffsetLeft = source.OffsetLeft;
        target.OffsetTop = source.OffsetTop;
        target.OffsetRight = source.OffsetRight;
        target.OffsetBottom = source.OffsetBottom;
    }

    private static void ApplyRowHorizontalNudges(MarginContainer row, Control source)
    {
        int sourceLeft = TryGetMarginConstant(source, "margin_left", 2);
        int sourceRight = TryGetMarginConstant(source, "margin_right", 4);
        int sourceTop = TryGetMarginConstant(source, "margin_top", 0);
        int sourceBottom = TryGetMarginConstant(source, "margin_bottom", 0);

        int left = sourceLeft + ComboHintLabelRightNudgePx;
        int right = sourceRight + ComboHintTickboxLeftNudgePx;
        row.AddThemeConstantOverride("margin_left", left);
        row.AddThemeConstantOverride("margin_top", sourceTop);
        row.AddThemeConstantOverride("margin_right", right);
        row.AddThemeConstantOverride("margin_bottom", sourceBottom);
        ModEntry.LogInject("Injector.TryInject.Nudge", $"sourceType={source.GetType().Name}, margin_left={left}, margin_right={right}, margin_top={sourceTop}, margin_bottom={sourceBottom}");
    }

    private static void EnsureHoverTipForRow(Control row, string title, string description)
    {
        if (row.HasMeta(HoverTipBoundMetaKey))
        {
            row.SetMeta(HoverTipTitleMetaKey, title);
            row.SetMeta(HoverTipDescriptionMetaKey, description);
            return;
        }

        row.SetMeta(HoverTipBoundMetaKey, true);
        row.SetMeta(HoverTipTitleMetaKey, title);
        row.SetMeta(HoverTipDescriptionMetaKey, description);
        row.MouseEntered += () => ShowHoverTip(row);
        row.MouseExited += () => HideHoverTip(row);
        row.TreeExiting += () => HideHoverTip(row);
        ModEntry.LogInject("Injector.TryInject.HoverTip.Bound", $"row={row.Name}");
    }

    private static void EnsureRowLabel(Control row, string labelText)
    {
        Control? rowLabel = row.FindChild("Label", true, false) as Control;
        if (rowLabel is MegaRichTextLabel richLabel)
        {
            richLabel.Text = labelText;
        }
    }

    private static void RemoveTickboxesFromRow(Control row)
    {
        List<NFastModeTickbox> tickboxes = row.GetChildren().OfType<NFastModeTickbox>().ToList();
        foreach (NFastModeTickbox tickbox in tickboxes)
        {
            tickbox.QueueFree();
        }
    }

    private static void ShowHoverTip(Control row)
    {
        if (ActiveHoverTipsByRow.TryGetValue(row, out Control? existingTip) && GodotObject.IsInstanceValid(existingTip))
        {
            return;
        }

        if (NGame.Instance?.HoverTipsContainer == null)
        {
            ModEntry.LogInject("Injector.HoverTip.Skip", "hover tips container missing");
            return;
        }

        Control tip = PreloadManager.Cache.GetScene(HoverTipScenePath).Instantiate<Control>(PackedScene.GenEditState.Disabled);
        tip.Name = "ComboHintSettingHoverTip";
        tip.CustomMinimumSize = new Vector2(ComboHintHoverTipWidth, tip.CustomMinimumSize.Y);
        tip.Size = new Vector2(ComboHintHoverTipWidth, tip.Size.Y);

        string titleText = row.HasMeta(HoverTipTitleMetaKey) ? row.GetMeta(HoverTipTitleMetaKey).ToString() ?? ComboHintHoverTipTitle : ComboHintHoverTipTitle;
        string descriptionText = row.HasMeta(HoverTipDescriptionMetaKey) ? row.GetMeta(HoverTipDescriptionMetaKey).ToString() ?? ComboHintHoverTipDescription : ComboHintHoverTipDescription;

        MegaLabel? title = tip.GetNodeOrNull<MegaLabel>("%Title");
        if (title != null)
        {
            title.SetTextAutoSize(titleText);
        }

        MegaRichTextLabel? description = tip.GetNodeOrNull<MegaRichTextLabel>("%Description");
        if (description != null)
        {
            description.Text = descriptionText;
        }

        TextureRect? icon = tip.GetNodeOrNull<TextureRect>("%Icon");
        if (icon != null)
        {
            icon.Visible = false;
        }

        NGame.Instance.HoverTipsContainer.AddChild(tip);
        tip.GlobalPosition = row.GlobalPosition + NSettingsScreen.settingTipsOffset;
        ActiveHoverTipsByRow[row] = tip;
        ModEntry.LogInject("Injector.HoverTip.Show", $"row={row.Name}, pos={tip.GlobalPosition}");
    }

    private static void HideHoverTip(Control row)
    {
        if (ActiveHoverTipsByRow.TryGetValue(row, out Control? tip))
        {
            if (GodotObject.IsInstanceValid(tip))
            {
                tip.QueueFree();
            }
            ActiveHoverTipsByRow.Remove(row);
            ModEntry.LogInject("Injector.HoverTip.Hide", $"row={row.Name}");
        }
    }

    private static int TryGetMarginConstant(Control source, string key, int fallback)
    {
        try
        {
            return source.GetThemeConstant(key, "MarginContainer");
        }
        catch
        {
            return fallback;
        }
    }

    private static void MakeTickboxVisualMaterialUnique(NFastModeTickbox tickbox)
    {
        Control? visuals = tickbox.GetNodeOrNull<Control>("%TickboxVisuals");
        if (visuals == null)
        {
            return;
        }

        if (visuals.HasMeta(TickboxMaterialBoundMetaKey))
        {
            return;
        }

        if (visuals.Material is ShaderMaterial material)
        {
            if (material.Duplicate() is ShaderMaterial uniqueMaterial)
            {
                uniqueMaterial.ResourceLocalToScene = true;
                visuals.Material = uniqueMaterial;
                visuals.SetMeta(TickboxMaterialBoundMetaKey, true);
            }

            return;
        }

        ModEntry.LogUi("TickboxMaterial.Skip", $"tickbox={tickbox.Name}, reason=non_shader_material");
    }

}
