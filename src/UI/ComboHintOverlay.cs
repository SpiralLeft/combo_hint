using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace ComboHint;

public static class ComboHintOverlay
{
    private const string OverlayNodeName = "ComboHintOverlay";

    private static ComboHintOverlayNode? _node;

    private static ulong _attachedCombatCreatedMsec;

    public static void EnsureAttached()
    {
        try
        {
            NCombatRoom? room = NCombatRoom.Instance;
            if (room == null)
            {
                if (_node != null && GodotObject.IsInstanceValid(_node))
                {
                    _node.QueueFree();
                }
                if (NGame.Instance != null)
                {
                    foreach (ComboHintOverlayNode stale in NGame.Instance.GetChildren().OfType<ComboHintOverlayNode>().ToList())
                    {
                        stale.QueueFree();
                    }
                }
                _node = null;
                _attachedCombatCreatedMsec = 0UL;
                return;
            }

            if (_attachedCombatCreatedMsec != room.CreatedMsec)
            {
                _node = null;
                _attachedCombatCreatedMsec = room.CreatedMsec;
            }

            if (_node != null && GodotObject.IsInstanceValid(_node))
            {
                return;
            }

            Node uiParent = room.Ui;
            ComboHintOverlayNode? existing = uiParent.GetChildren().OfType<ComboHintOverlayNode>().FirstOrDefault();
            if (existing != null)
            {
                existing.SetLowerLayerOrder();
                _node = existing;
                return;
            }

            ComboHintOverlayNode created = new ComboHintOverlayNode();
            created.Name = OverlayNodeName;
            uiParent.CallDeferred(Node.MethodName.AddChild, created);
            _node = created;
            ModEntry.LogInfoToFile("ComboHintOverlay.Attach", $"attached parent={uiParent.Name}, type={uiParent.GetType().Name}, combatCreated={room.CreatedMsec}");
            created.CallDeferred(nameof(ComboHintOverlayNode.ForceSetupAndRefresh));
        }
        catch (Exception ex)
        {
            ModEntry.LogErrorToFile("ComboHintOverlay.EnsureAttached", ex);
        }
    }

    public static void Refresh()
    {
        EnsureAttached();
        if (_node != null && GodotObject.IsInstanceValid(_node))
        {
            _node.RefreshContent();
        }
    }

    public static void RefreshImmediatelyForCurrentTurn(string reason)
    {
        EnsureAttached();
        if (_node == null || !GodotObject.IsInstanceValid(_node))
        {
            return;
        }

        CombatState? state = CombatManager.Instance?.DebugOnlyGetState();
        if (state?.CurrentSide == CombatSide.Player)
        {
            _node.ResetVisibilityForTurn(CombatSide.Player);
            ModEntry.LogUi("Overlay.RefreshImmediate", $"reason={reason}, side=Player");
            return;
        }

        _node.RefreshContent();
        ModEntry.LogUi("Overlay.RefreshImmediate", $"reason={reason}, side={state?.CurrentSide}");
    }

    public static void HideTransient(string reason)
    {
        if (_node != null && GodotObject.IsInstanceValid(_node))
        {
            _node.HideOverlay(reason);
        }
    }

    public static void DisposeActiveNode(string reason)
    {
        if (_node != null && GodotObject.IsInstanceValid(_node))
        {
            ModEntry.LogInfoToFile("ComboHintOverlay.Dispose", reason);
            _node.QueueFree();
        }

        _node = null;
        _attachedCombatCreatedMsec = 0UL;
    }

    public static void OnSideTurnStart(CombatSide side)
    {
        if (_node != null && GodotObject.IsInstanceValid(_node))
        {
            _node.ResetVisibilityForTurn(side);
        }
    }
}

public partial class ComboHintOverlayNode : PanelContainer
{
    private const string HoverTipScenePath = "res://scenes/ui/hover_tip.tscn";
    private const float HoveredAlpha = 1.0f;
    private const float IdleAlpha = 0.5f;
    private const string SpecialWeakenGroupKey = "specialweaken";
    private const string SpecialVulnerableGroupKey = "specialvulnerable";

    private Control _hoverTipBox = null!;
    private MegaLabel _titleLabel = null!;
    private MegaRichTextLabel _descriptionLabel = null!;

    private bool _isVisible;
    private bool _isReady;
    private bool _isHovering;
    private ulong _lastHoverAlphaLogFrame;
    private string _lastStateSignature = string.Empty;

    public override void _EnterTree()
    {
        ModEntry.LogUi("Overlay.EnterTree", $"parent={GetParent()?.Name}, type={GetParent()?.GetType().Name}");
    }

    private void EnsureInitialized()
    {
        if (_isReady && _descriptionLabel != null && GodotObject.IsInstanceValid(_descriptionLabel))
        {
            return;
        }

        AnchorLeft = 1f;
        AnchorTop = 0f;
        AnchorRight = 1f;
        AnchorBottom = 0f;
        OffsetLeft = -460f;
        OffsetTop = 110f;
        OffsetRight = -16f;
        OffsetBottom = 304f;
        MouseFilter = MouseFilterEnum.Pass;
        ZIndex = -1;
        TopLevel = false;
        AddThemeStyleboxOverride("panel", new StyleBoxEmpty());

        if (_hoverTipBox == null || !GodotObject.IsInstanceValid(_hoverTipBox))
        {
            _hoverTipBox = PreloadManager.Cache.GetScene(HoverTipScenePath).Instantiate<Control>(PackedScene.GenEditState.Disabled);
            _hoverTipBox.Name = "HoverTipBox";
            _hoverTipBox.MouseFilter = MouseFilterEnum.Pass;
            AddChild(_hoverTipBox);

            _hoverTipBox.MouseEntered += OnHoverTipMouseEntered;
            _hoverTipBox.MouseExited += OnHoverTipMouseExited;

            _titleLabel = _hoverTipBox.GetNode<MegaLabel>("%Title");
            _descriptionLabel = _hoverTipBox.GetNode<MegaRichTextLabel>("%Description");
            _descriptionLabel.BbcodeEnabled = true;
            _descriptionLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _descriptionLabel.MouseFilter = MouseFilterEnum.Ignore;

            TextureRect? icon = _hoverTipBox.GetNodeOrNull<TextureRect>("%Icon");
            if (icon != null)
            {
                icon.Visible = false;
            }

            ModEntry.LogUi("Overlay.BoxCreated", $"box={_hoverTipBox.Name}");
        }

        _isReady = true;
    }

    public void ForceSetupAndRefresh()
    {
        EnsureInitialized();
        SetLowerLayerOrder();
        RefreshContent();
    }

    public void SetLowerLayerOrder()
    {
        ZIndex = -1;
        Node? parent = GetParent();
        if (parent != null)
        {
            parent.MoveChild(this, 0);
        }
    }

    public override void _Ready()
    {
        EnsureInitialized();
        ModEntry.LogInfoToFile("ComboHintOverlayNode.Ready", $"parent={GetParent()?.Name}, type={GetParent()?.GetType().Name}, topLevel={TopLevel}, zIndex={ZIndex}, offsets=({OffsetLeft},{OffsetTop},{OffsetRight},{OffsetBottom})");
        SetProcess(true);
        _isVisible = false;
        RefreshContent();
    }

    public override void _ExitTree()
    {
        if (_hoverTipBox != null && GodotObject.IsInstanceValid(_hoverTipBox))
        {
            _hoverTipBox.MouseEntered -= OnHoverTipMouseEntered;
            _hoverTipBox.MouseExited -= OnHoverTipMouseExited;
        }

        HideOverlay("exit_tree");
    }

    public override void _Process(double delta)
    {
        NCombatRoom? room = NCombatRoom.Instance;
        bool isCombatActive = room != null && room.Mode == CombatRoomMode.ActiveCombat;
        bool isCombatCurrentScreen = room != null && ActiveScreenContext.Instance.IsCurrent(room);
        if (!isCombatActive || !isCombatCurrentScreen)
        {
            ModEntry.LogUi("Overlay.Dispose", $"isCombatActive={isCombatActive}, isCombatCurrentScreen={isCombatCurrentScreen}, mode={room?.Mode}");
            if (GodotObject.IsInstanceValid(this))
            {
                if (_hoverTipBox != null && GodotObject.IsInstanceValid(_hoverTipBox))
                {
                    _hoverTipBox.Visible = false;
                }
                QueueFree();
            }
            return;
        }

        UpdateHoverOpacity();
        LogHoverAlphaWhileHovering();
    }

    private void UpdateHoverOpacity()
    {
        if (_hoverTipBox == null || !GodotObject.IsInstanceValid(_hoverTipBox) || !_hoverTipBox.Visible)
        {
            return;
        }

        Rect2 hoverRect = _hoverTipBox.GetGlobalRect();
        Vector2 mousePosition = GetGlobalMousePosition();
        bool isHoveringNow = hoverRect.HasPoint(mousePosition);
        SetOverlayOpacity(isHoveringNow);
    }

    private void LogHoverAlphaWhileHovering()
    {
        if (_hoverTipBox == null || !GodotObject.IsInstanceValid(_hoverTipBox) || !_hoverTipBox.Visible || !_isHovering)
        {
            return;
        }

        ulong frame = Engine.GetProcessFrames();
        if (_lastHoverAlphaLogFrame != 0UL && frame - _lastHoverAlphaLogFrame < 5UL)
        {
            return;
        }

        _lastHoverAlphaLogFrame = frame;
        Log.Info($"[ComboHint] overlay alpha while hovering: {_hoverTipBox.Modulate.A:0.00}, frame={frame}");
    }

    private void OnHoverTipMouseEntered()
    {
        SetOverlayOpacity(true, forceApplyAlpha: true);
    }

    private void OnHoverTipMouseExited()
    {
        SetOverlayOpacity(false, forceApplyAlpha: true);
    }

    public void HideOverlay(string reason)
    {
        if (!GodotObject.IsInstanceValid(this))
        {
            return;
        }

        _isVisible = false;
        if (_hoverTipBox != null && GodotObject.IsInstanceValid(_hoverTipBox))
        {
            _hoverTipBox.Visible = false;
            _isHovering = false;
        }
        ModEntry.LogUi("Overlay.Hide", $"reason={reason}, panelVisible={_hoverTipBox?.Visible}");
        LogStateIfChanged(reason, 0);
    }

    public void ResetVisibilityForTurn(CombatSide side)
    {
        EnsureInitialized();

        if (side == CombatSide.Player)
        {
            _isVisible = true;
            RefreshContent();
            ApplyIdleOpacity();
            ModEntry.LogUi("Overlay.TurnStartReset", $"side=Player, panelVisible={_hoverTipBox.Visible}");
        }
        else
        {
            HideOverlay($"side_turn_start_{side}");
            ModEntry.LogUi("Overlay.TurnStartReset", $"side={side}, panelVisible={_hoverTipBox?.Visible}");
        }
    }

    public void RefreshContent()
    {
        try
        {
            EnsureInitialized();
            if (!_isReady || _descriptionLabel == null || !GodotObject.IsInstanceValid(_descriptionLabel))
            {
                LogStateIfChanged("not_ready", 0);
                return;
            }

            if (!ModEntry.IsOverlayEnabledInCurrentRun())
            {
                LogStateIfChanged("disabled_by_singleplayer_setting", 0);
                _hoverTipBox.Visible = false;
                return;
            }

            if (!_isVisible)
            {
                LogStateIfChanged("hidden_by_turn_state", 0);
                _hoverTipBox.Visible = false;
                return;
            }

            NCombatRoom? room = NCombatRoom.Instance;
            if (room == null)
            {
                LogStateIfChanged("no_combat_room", 0);
                _hoverTipBox.Visible = false;
                return;
            }

            List<string> lines = new List<string>();
            List<Creature> players = room.CreatureNodes.Select((NCreature n) => n.Entity).Where((Creature c) => c != null && c.IsPlayer).ToList();
            foreach (Creature playerCreature in players)
            {
                IReadOnlyList<CardModel> handCards = playerCreature.Player?.PlayerCombatState?.Hand?.Cards ?? Array.Empty<CardModel>();
                List<MatchedTrigger> matches = FindMatchedTextsInHand(handCards);
                if (matches.Count == 0)
                {
                    continue;
                }

                string playerName = GetSafePlayerName(playerCreature);
                string joinedHits = string.Join(ModEntry.GetLocalizedListSeparator(), matches.Select((MatchedTrigger m) => FormatColoredText(m.Text, m.ColorHex)));
                lines.Add(playerName + ModEntry.GetLocalizedHasConnector() + joinedHits);
            }

            List<string> overlayKillLines = new List<string>();
            if (ModEntry.OverlayKillTriggerGroup.TriggerModelIds.Count > 0)
            {
                foreach (Creature playerCreature in players)
                {
                    IReadOnlyList<CardModel> handCards = playerCreature.Player?.PlayerCombatState?.Hand?.Cards ?? Array.Empty<CardModel>();
                    List<MatchedTrigger> matches = FindMatchedTextsInHandForGroup(handCards, ModEntry.OverlayKillTriggerGroup);
                    if (matches.Count == 0)
                    {
                        continue;
                    }

                    string playerName = GetSafePlayerName(playerCreature);
                    string joinedHits = string.Join(ModEntry.GetLocalizedListSeparator(), matches.Select((MatchedTrigger m) => FormatColoredText(m.Text, m.ColorHex)));
                    overlayKillLines.Add(playerName + ModEntry.GetLocalizedHasConnector() + joinedHits);
                }
            }

            if (overlayKillLines.Count > 0)
            {
                lines.Add($"[color={ModEntry.OverlayKillTitleColorHex}]{ModEntry.GetLocalizedKillTitle()}[/color]");
                lines.AddRange(overlayKillLines);
            }

            if (lines.Count == 0)
            {
                _titleLabel.SetTextAutoSize(ModEntry.GetLocalizedComboHintTitle());
                _descriptionLabel.Text = ModEntry.GetLocalizedNoMatchText();
                _hoverTipBox.Visible = true;
                ApplyIdleOpacity();
                LogStateIfChanged("no_matches_default_text", 1);
                return;
            }

            _titleLabel.SetTextAutoSize(ModEntry.GetLocalizedComboHintTitle());
            _descriptionLabel.Text = string.Join("\n", lines);
            _hoverTipBox.Visible = true;
            ApplyIdleOpacity();
            LogStateIfChanged("visible", lines.Count);
        }
        catch (Exception ex)
        {
            Log.Warn($"[ComboHint] overlay refresh failed: {ex}");
            ModEntry.LogErrorToFile("ComboHintOverlayNode.RefreshContent", ex);
            if (_hoverTipBox != null && GodotObject.IsInstanceValid(_hoverTipBox))
            {
                _hoverTipBox.Visible = false;
            }
        }
    }

    private void ApplyIdleOpacity()
    {
        if (_hoverTipBox == null || !GodotObject.IsInstanceValid(_hoverTipBox))
        {
            return;
        }

        // First-show correctness: determine hover state immediately to avoid a stale 100% alpha frame.
        if (_hoverTipBox.Visible)
        {
            Rect2 hoverRect = _hoverTipBox.GetGlobalRect();
            Vector2 mousePosition = GetGlobalMousePosition();
            bool isHoveringNow = hoverRect.HasPoint(mousePosition);
            SetOverlayOpacity(isHoveringNow, forceApplyAlpha: true);
            return;
        }

        _isHovering = false;
        Color color = _hoverTipBox.Modulate;
        color.A = IdleAlpha;
        _hoverTipBox.Modulate = color;
    }

    private void SetOverlayOpacity(bool isHoveringNow, bool forceApplyAlpha = false)
    {
        if (_hoverTipBox == null || !GodotObject.IsInstanceValid(_hoverTipBox))
        {
            return;
        }

        float targetAlpha = isHoveringNow ? HoveredAlpha : IdleAlpha;
        float currentAlpha = _hoverTipBox.Modulate.A;
        bool alphaAlreadySet = Math.Abs(currentAlpha - targetAlpha) < 0.001f;
        if (!forceApplyAlpha && isHoveringNow == _isHovering && alphaAlreadySet)
        {
            return;
        }

        _isHovering = isHoveringNow;
        Color color = _hoverTipBox.Modulate;
        color.A = targetAlpha;
        _hoverTipBox.Modulate = color;
    }

    private void LogStateIfChanged(string reason, int lineCount)
    {
        string parentName = GetParent()?.Name ?? "null";
        string parentType = GetParent()?.GetType().Name ?? "null";
        bool panelVisible = _hoverTipBox != null && GodotObject.IsInstanceValid(_hoverTipBox) && _hoverTipBox.Visible;
        string signature = $"reason={reason}|lineCount={lineCount}|panelVisible={panelVisible}|isReady={_isReady}|isVisible={_isVisible}|parent={parentName}|parentType={parentType}|topLevel={TopLevel}|z={ZIndex}|offsets={OffsetLeft},{OffsetTop},{OffsetRight},{OffsetBottom}";
        if (signature == _lastStateSignature)
        {
            return;
        }

        _lastStateSignature = signature;
        ModEntry.LogInfoToFile("ComboHintOverlayNode.State", signature);
    }

    private static string GetSafePlayerName(Creature playerCreature)
    {
        try
        {
            if (RunManager.Instance?.IsSinglePlayerOrFakeMultiplayer ?? true)
            {
                return playerCreature.Player?.Character?.Title.GetFormattedText() ?? "玩家";
            }

            ulong id = playerCreature.Player?.NetId ?? 0UL;
            if (id == 0UL)
            {
                return playerCreature.Player?.Character?.Title.GetFormattedText() ?? "玩家";
            }

            return PlatformUtil.GetPlayerName(RunManager.Instance.NetService.Platform, id);
        }
        catch
        {
            return playerCreature.Player?.Character?.Title.GetFormattedText() ?? "玩家";
        }
    }

    private static List<MatchedTrigger> FindMatchedTextsInHand(IEnumerable<CardModel> handCards)
    {
        List<MatchedTrigger> matches = new List<MatchedTrigger>();
        HashSet<string> dedup = new HashSet<string>(StringComparer.Ordinal);

        foreach (CardModel handCard in handCards)
        {
            foreach (MatchedTrigger matched in FindMatchedTexts(handCard))
            {
                if (dedup.Add(BuildMatchDedupKey(matched)))
                {
                    matches.Add(matched);
                }
            }
        }

        return matches;
    }

    private static List<MatchedTrigger> FindMatchedTextsInHandForGroup(IEnumerable<CardModel> handCards, TriggerGroup group)
    {
        List<MatchedTrigger> matches = new List<MatchedTrigger>();
        HashSet<string> dedup = new HashSet<string>(StringComparer.Ordinal);

        foreach (CardModel handCard in handCards)
        {
            string modelId = handCard.Id.Entry;
            if (string.IsNullOrWhiteSpace(modelId) || !group.TriggerModelIds.Contains(modelId))
            {
                continue;
            }

            string displayText = ModEntry.GetDisplayCardTitleWithEnglish(handCard);
            string dedupKey = group.ColorHex + "|" + displayText;
            if (dedup.Add(dedupKey))
            {
                matches.Add(new MatchedTrigger(displayText, group.ColorHex));
            }
        }

        return matches;
    }

    private static List<MatchedTrigger> FindMatchedTexts(CardModel card)
    {
        List<MatchedTrigger> matches = new List<MatchedTrigger>();
        HashSet<string> dedup = new HashSet<string>(StringComparer.Ordinal);
        string modelId = card.Id.Entry;
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return matches;
        }

        string title = ModEntry.GetDisplayCardTitleWithEnglish(card);
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
