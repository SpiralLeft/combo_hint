using System.Reflection;
using BaseLib.Config;
using BaseLib.Config.UI;
using ComboHint;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace ComboHintScenarioTests;

// Show BaseLib's actual submenu in the isolated combat scene for screenshot review.
public static class ConfigMenuTest
{
    private static Control? _root;
    private static NModConfigSubmenu? _menu;
    public static void Show()
    {
        _root = new Control { Name = "ComboHintConfigTest" };
        NCombatRoom.Instance!.Ui.AddChild(_root);
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var backdrop = new ColorRect { Color = new Color(0.06f, 0.08f, 0.1f, 0.98f) };
        _root.AddChild(backdrop);
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _menu = new NModConfigSubmenu();
        _root.AddChild(_menu);
        _menu.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _menu.Show();
        typeof(NModConfigSubmenu).GetMethod("LoadModConfig", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(_menu, new object[] { ModConfigRegistry.Get<ComboHintSettings>()! });
        ScenarioTestMod.Require(!ScenarioTestMod.Field<bool>(_menu, "_modLoadFailed"), "BaseLib config page failed to load.");
    }
    public static void Expand() => ScenarioTestMod.Field<VBoxContainer>(_menu!, "_optionContainer")
        .GetChildren().OfType<NConfigCollapsibleSection>().Single().IsExpanded = true;
    public static void ScrollToEffects()
    {
        var options = ScenarioTestMod.Field<VBoxContainer>(_menu!, "_optionContainer");
        var group = options.GetChildren().OfType<NConfigCollapsibleSection>().Single();
        group.ContentContainer.GetChildren().OfType<NConfigOptionRow>().Last().SettingControl.GrabFocus();
        ScenarioTestMod.Field<NNativeScrollableContainer>(_menu!, "_rightScrollArea").ScrollToFocusedControl(true);
    }
    public static void HoverLastEffect()
    {
        var row = ScenarioTestMod.Field<VBoxContainer>(_menu!, "_optionContainer")
            .GetChildren().OfType<NConfigCollapsibleSection>().Single().ContentContainer
            .GetChildren().OfType<NConfigOptionRow>().Last();
        row.GetViewport().PushInput(new InputEventMouseMotion { Position = row.GetGlobalRect().GetCenter(), GlobalPosition = row.GetGlobalRect().GetCenter() }, true);
    }
    public static void RequireHover()
    {
        var row = ScenarioTestMod.Field<VBoxContainer>(_menu!, "_optionContainer")
            .GetChildren().OfType<NConfigCollapsibleSection>().Single().ContentContainer
            .GetChildren().OfType<NConfigOptionRow>().Last();
        var tip = ScenarioTestMod.Field<MegaCrit.Sts2.Core.HoverTips.HoverTip>(row, "_hoverTip");
        ScenarioTestMod.Require(ScenarioTestMod.Field<bool>(row, "_hoverTipVisible")
            && tip.Title == (ComboHint.HintText.IsChinese ? "多人卡牌" : "Multiplayer Cards"),
            "The actual config tooltip should display a plain multiplayer title.");
    }
    public static void Close()
    {
        typeof(NModConfigSubmenu).GetMethod("SaveAndClearCurrentMod", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(_menu, null);
        _root!.QueueFree();
        _root = null;
        _menu = null;
    }
}
