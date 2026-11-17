using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Assets;

namespace ComboHint;

// Shared by our overlay and bubbles. Each label owns and removes its native popup.
public partial class HintHover : Node
{
    private MegaRichTextLabel _label = null!;
    private IReadOnlyDictionary<string, IHoverTip> _tips = null!;
    private NHoverTipSet? _hoverTip;
    private string? _activeKey;
    private HoverTipAlignment _alignment;

    public static HintHover Attach(Node owner, MegaRichTextLabel label, IReadOnlyDictionary<string, IHoverTip> tips,
        HoverTipAlignment alignment = HoverTipAlignment.Right)
    {
        var hover = new HintHover
        {
            Name = "ComboHintHover", _label = label, _tips = tips, _alignment = alignment
        };
        owner.AddChild(hover);
        return hover;
    }

    public void SetTips(IReadOnlyDictionary<string, IHoverTip> tips) { Clear(); _tips = tips; }

    public override void _Ready()
    {
        _label.MouseFilter = Control.MouseFilterEnum.Pass;
        _label.MetaUnderlined = true;
        _label.MetaHoverStarted += OnHover;
        _label.MetaHoverEnded += OnHoverEnded;
        _label.MouseExited += Clear;
    }

    private void OnHover(Variant meta)
    {
        Clear();
        if (!_tips.TryGetValue(meta.AsString(), out var tip)) return;
        try
        {
            _hoverTip = NHoverTipSet.CreateAndShow(_label, tip, _alignment);
            _activeKey = meta.AsString();
            // LocString-based HoverTip constructors are neutral. Apply the same native
            // background material NHoverTipSet uses for debuffs, only to our custom tips.
            string effect = _activeKey.Substring(_activeKey.LastIndexOf('.') + 1);
            if (_hoverTip != null && effect is "WEAKENING" or "EXPOSE") ApplyDebuffBackground(_hoverTip);
        }
        catch (Exception ex) { ModEntry.Logger.Error($"Could not show hint description: {ex}"); }
    }

    private static void ApplyDebuffBackground(Node node)
    {
        if (node is CanvasItem background && node.Name == "Bg")
            background.Material = PreloadManager.Cache.GetMaterial("res://materials/ui/hover_tip_debuff.tres");
        foreach (var child in node.GetChildren()) ApplyDebuffBackground(child);
    }

    private void OnHoverEnded(Variant _) => Clear();

    public void Clear()
    {
        _activeKey = null;
        if (_hoverTip == null) return;
        NHoverTipSet.Remove(_label);
        _hoverTip = null;
    }

    public override void _ExitTree()
    {
        Clear();
        _label.MetaHoverStarted -= OnHover;
        _label.MetaHoverEnded -= OnHoverEnded;
        _label.MouseExited -= Clear;
    }
}
