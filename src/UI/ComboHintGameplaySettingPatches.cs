using HarmonyLib;
using Godot;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.addons.mega_text;
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
        if (__instance.Name != ComboHintSettingsInjector.ComboHintTickboxName)
        {
            return true;
        }

        __instance.IsTicked = ModEntry.EnabledByGameplaySetting;
        ModEntry.LogInject("ComboHintTickbox.SetFromSettings", $"isTicked={__instance.IsTicked}");
        return false;
    }
}

[HarmonyPatch(typeof(NFastModeTickbox), "OnTick")]
public static class ComboHintFastModeTickboxOnTickPatch
{
    public static bool Prefix(NFastModeTickbox __instance)
    {
        if (__instance.Name != ComboHintSettingsInjector.ComboHintTickboxName)
        {
            return true;
        }

        ModEntry.LogInject("ComboHintTickbox.OnTick", "enabled=true");
        ModEntry.SetEnabledByGameplaySetting(true);
        ComboHintOverlay.RefreshImmediatelyForCurrentTurn("settings_toggle_on");
        return false;
    }
}

[HarmonyPatch(typeof(NFastModeTickbox), "OnUntick")]
public static class ComboHintFastModeTickboxOnUntickPatch
{
    public static bool Prefix(NFastModeTickbox __instance)
    {
        if (__instance.Name != ComboHintSettingsInjector.ComboHintTickboxName)
        {
            return true;
        }

        ModEntry.LogInject("ComboHintTickbox.OnUntick", "enabled=false");
        ModEntry.SetEnabledByGameplaySetting(false);
        ComboHintOverlay.HideTransient("settings_toggle_off");
        return false;
    }
}

public static class ComboHintSettingsInjector
{
    private const string ComboHintRowName = "ComboHint";
    public const string ComboHintTickboxName = "ComboHintTickbox";

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
            if (content == null || content.GetNodeOrNull<Control>(ComboHintRowName) != null)
            {
                bool alreadyExists = content != null && content.GetNodeOrNull<Control>(ComboHintRowName) != null;
                ModEntry.LogInject("Injector.TryInject.Skip", $"contentNull={content == null}, alreadyExists={alreadyExists}");
                return;
            }

            Node? fastModeNode = content.GetChildren().FirstOrDefault((Node n) => n.Name == "FastMode");
            if (fastModeNode is not Control fastModeRow)
            {
                ModEntry.LogInject("Injector.TryInject.Skip", "FastMode row not found by child-name scan");
                return;
            }
            ModEntry.LogInject("Injector.TryInject.FastMode", $"rowName={fastModeRow.Name}, fastModeIndex={fastModeRow.GetIndex()}, rowType={fastModeRow.GetType().Name}");

            MarginContainer comboHintRow = new MarginContainer();
            CopyControlLayout(fastModeRow, comboHintRow);
            if (fastModeRow is MarginContainer fastModeMargin)
            {
                CopyMarginTheme(fastModeMargin, comboHintRow);
            }
            comboHintRow.MouseFilter = fastModeRow.MouseFilter;
            comboHintRow.FocusMode = fastModeRow.FocusMode;
            comboHintRow.Name = ComboHintRowName;

            // Do not copy the FastMode row script (NFastModeHoverTip).
            // Its lifecycle assumptions can dispose this temporary row before children are attached.

            Control? sourceLabel = fastModeRow.FindChild("Label", true, false) as Control;
            if (sourceLabel == null)
            {
                ModEntry.LogInject("Injector.TryInject.Skip", "FastMode label not found");
                return;
            }

            Node sourceLabelCopy = sourceLabel.Duplicate();
            comboHintRow.AddChild(sourceLabelCopy);
                if (sourceLabelCopy is MegaRichTextLabel label)
                {
                    label.Text = "连携提示";
            }

            NFastModeTickbox? sourceTickbox = fastModeRow.FindChild("FastModeTickbox", true, false) as NFastModeTickbox;
            if (sourceTickbox == null)
            {
                ModEntry.LogInject("Injector.TryInject.Skip", "FastMode tickbox not found in source row");
                return;
            }

            Node copiedTickboxNode = sourceTickbox.Duplicate();
            comboHintRow.AddChild(copiedTickboxNode);

            NFastModeTickbox? comboTickbox = copiedTickboxNode as NFastModeTickbox;
            if (comboTickbox == null)
            {
                ModEntry.LogInject("Injector.TryInject.Skip", "copied FastMode tickbox cast failed");
                return;
            }
            comboTickbox.Name = ComboHintTickboxName;
            MakeTickboxVisualMaterialUnique(comboTickbox);

            ColorRect divider = new ColorRect();
            Node? sourceDividerNode = fastModeRow.GetIndex() > 0 ? content.GetChild(fastModeRow.GetIndex() - 1) : null;
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

            divider.Name = "ComboHintDivider";

            int fastModeIndex = fastModeRow.GetIndex();
            content.AddChild(divider);
            content.MoveChild(divider, fastModeIndex + 1);
            content.AddChild(comboHintRow);
            content.MoveChild(comboHintRow, fastModeIndex + 2);
            ModEntry.LogInject("Injector.TryInject.Inserted", $"dividerIndex={divider.GetIndex()}, rowIndex={comboHintRow.GetIndex()}");

            // NTickbox internals are initialized during _Ready; apply state after node enters tree.
            comboTickbox.CallDeferred(nameof(NFastModeTickbox.SetFromSettings));

            Control? fastModeTickboxControl = fastModeRow.FindChild("FastModeTickbox", true, false) as Control;
            Control? showRunTimerTickbox = content.GetNodeOrNull<Control>("ShowRunTimer/SettingsTickbox");
            if (showRunTimerTickbox == null)
            {
                showRunTimerTickbox = FindNextTickboxBelow(content, comboHintRow);
            }

            if (fastModeTickboxControl != null)
            {
                fastModeTickboxControl.FocusNeighborBottom = comboTickbox.GetPath();
            }

            comboTickbox.FocusNeighborTop = fastModeTickboxControl?.GetPath() ?? comboTickbox.GetPath();
            comboTickbox.FocusNeighborBottom = showRunTimerTickbox?.GetPath() ?? comboTickbox.GetPath();

            if (showRunTimerTickbox != null)
            {
                showRunTimerTickbox.FocusNeighborTop = comboTickbox.GetPath();
            }

            ModEntry.LogInject("Injector.TryInject.Success", $"tickboxPath={comboTickbox.GetPath()}, showRunTimerFound={showRunTimerTickbox != null}");
        }
        catch (System.Exception ex)
        {
            ModEntry.LogInject("Injector.TryInject.Error", ex.ToString());
        }
    }

    private static Control? FindNextTickboxBelow(VBoxContainer content, MarginContainer currentRow)
    {
        int rowIndex = currentRow.GetIndex();
        for (int i = rowIndex + 1; i < content.GetChildCount(); i++)
        {
            if (content.GetChild(i) is not MarginContainer row)
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

    private static void CopyMarginTheme(MarginContainer source, MarginContainer target)
    {
        target.AddThemeConstantOverride("margin_left", source.GetThemeConstant("margin_left"));
        target.AddThemeConstantOverride("margin_top", source.GetThemeConstant("margin_top"));
        target.AddThemeConstantOverride("margin_right", source.GetThemeConstant("margin_right"));
        target.AddThemeConstantOverride("margin_bottom", source.GetThemeConstant("margin_bottom"));
    }

    private static void MakeTickboxVisualMaterialUnique(NFastModeTickbox tickbox)
    {
        Control? visuals = tickbox.GetNodeOrNull<Control>("%TickboxVisuals");
        if (visuals?.Material is ShaderMaterial material)
        {
            if (material.Duplicate() is ShaderMaterial uniqueMaterial)
            {
                visuals.Material = uniqueMaterial;
            }
        }
    }

}
