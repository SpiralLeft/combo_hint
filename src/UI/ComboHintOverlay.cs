using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.HoverTips;

namespace ComboHint;

// A view only: combat state, matching and bubble scheduling belong to its controller.
public partial class ComboHintOverlayNode : PanelContainer
{
    private CombatHintController? _controller;
    private Control? _box;
    private MegaLabel? _title;
    private MegaRichTextLabel? _description;
    private HintHover? _hover;

    public void Initialize(CombatHintController controller) => _controller = controller;

    public override void _Ready()
    {
        AnchorLeft = AnchorRight = 1;
        AnchorTop = AnchorBottom = 0;
        OffsetLeft = -460;
        OffsetRight = -16;
        OffsetTop = 110;
        OffsetBottom = 304;
        MouseFilter = MouseFilterEnum.Ignore;
        ProcessMode = ProcessModeEnum.Always;
        ZIndex = -1;
        AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        try
        {
            _box = PreloadManager.Cache.GetScene("res://scenes/ui/hover_tip.tscn").Instantiate<Control>();
            _box.MouseFilter = MouseFilterEnum.Pass;
            _box.Visible = false;
            AddChild(_box);
            _title = _box.GetNode<MegaLabel>("%Title");
            _description = _box.GetNode<MegaRichTextLabel>("%Description");
            _title.MouseFilter = MouseFilterEnum.Ignore;
            _description.MouseFilter = MouseFilterEnum.Ignore;
            _description.BbcodeEnabled = true;
            _description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _hover = HintHover.Attach(this, _description, new Dictionary<string, IHoverTip>(), HoverTipAlignment.Left);
            var icon = _box.GetNodeOrNull<TextureRect>("%Icon");
            if (icon != null) icon.Visible = false;
            _controller?.Attach(this);
        }
        catch (Exception ex)
        {
            ModEntry.Logger.Error($"Could not initialize overlay: {ex}");
            QueueFree();
        }
    }

    public override void _Process(double delta)
    {
        try
        {
            _controller?.Tick(delta);
            if (_box is { Visible: true })
            {
                var color = _box.Modulate;
                color.A = _box.GetGlobalRect().HasPoint(GetGlobalMousePosition()) ? 1f : 0.5f;
                _box.Modulate = color;
            }
        }
        catch (Exception ex)
        {
            HideContent();
            ModEntry.Logger.Error($"Could not update hints: {ex}");
            _controller?.FrameFailed();
        }
    }

    public void ShowContent(string title, HintContent content)
    {
        if (_box == null || _title == null || _description == null) return;
        _title.SetTextAutoSize(title);
        _hover?.SetTips(content.HoverTips);
        _description.Text = content.Text;
        _box.Visible = true;
    }

    public void HideContent()
    {
        _hover?.Clear();
        if (_box != null && GodotObject.IsInstanceValid(_box)) _box.Visible = false;
    }

    public override void _ExitTree()
    {
        _controller?.Detach(this);
        _controller = null;
    }
}
