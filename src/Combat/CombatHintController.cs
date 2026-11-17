using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.addons.mega_text;

namespace ComboHint;

public sealed class CombatHintController
{
    private readonly CombatState _state;
    private readonly HintRules _rules;
    private readonly HintMatcher _matcher;
    private readonly BubbleDebouncer<Creature> _bubbles = new();
    private readonly List<NSpeechBubbleVfx> _activeBubbles = new();
    private ComboHintOverlayNode? _overlay;
    private bool _playerTurn;
    private bool _dirty = true;
    private bool _disposed;
    private string? _language;

    public CombatHintController(CombatState state, HintRules rules)
    {
        _state = state;
        _rules = rules;
        _matcher = new HintMatcher(rules);
        _playerTurn = state.CurrentSide == CombatSide.Player;
    }

    public void Attach(ComboHintOverlayNode overlay)
    {
        if (_disposed) { overlay.QueueFree(); return; }
        if (_overlay != null && GodotObject.IsInstanceValid(_overlay) && _overlay != overlay)
            _overlay.QueueFree();
        _overlay = overlay;
        _dirty = true;
    }

    public void Detach(ComboHintOverlayNode overlay)
    {
        if (_overlay != overlay) return;
        _overlay = null;
        _bubbles.Clear();
    }

    public void StartSide(CombatSide side)
    {
        _bubbles.Clear();
        _playerTurn = side == CombatSide.Player;
        Invalidate();
    }

    public void EndSide(CombatSide side)
    {
        if (side != CombatSide.Player) return;
        _playerTurn = false;
        _bubbles.Clear();
        Hide();
    }

    public void SettingsChanged()
    {
        _bubbles.Clear();
        ClearActiveBubbles();
        Invalidate();
        if (!CanShowOverlay) Hide();
    }

    public void Invalidate() => _dirty = true;

    private bool SinglePlayerAllowed => ComboHintSettings.EnableSinglePlayer
        || !(RunManager.Instance?.IsSingleplayerOrFakeMultiplayer ?? true);
    private bool CanShowOverlay => !_disposed && _playerTurn && SinglePlayerAllowed && ComboHintSettings.EnableOverlay;
    private bool CanShowBubbles => !_disposed && _playerTurn && SinglePlayerAllowed && ComboHintSettings.EnableBubbles;

    public void CardChangedPiles(CardModel card, PileType oldPile)
    {
        // The subscription already scopes this event to _state. A removed card can have
        // lost its owner/combat reference by this late hook, but must still refresh the view.
        if (_disposed) return;
        bool enteredHand = card.Pile?.Type == PileType.Hand && oldPile != PileType.Hand;
        if (enteredHand || oldPile == PileType.Hand) Invalidate();
        if (!enteredHand || !CanShowBubbles || card.Owner?.Creature is not { } owner) return;

        bool matches = Content(new[] { card }).VisibleTriggers.Count > 0;
        var baseline = MatchHand(owner, card);
        _bubbles.EnterHand(owner, matches, baseline);
    }

    // Called by the UI node, keeping matching, timer updates and VFX on the Godot main thread.
    public void Tick(double delta)
    {
        if (_disposed || _overlay == null || !GodotObject.IsInstanceValid(_overlay)) return;
        _activeBubbles.RemoveAll(b => !GodotObject.IsInstanceValid(b));
        var room = NCombatRoom.Instance;
        bool active = !_overlay.GetTree().Paused && !(RunManager.Instance?.IsPaused ?? false)
            && room != null && room.Mode == CombatRoomMode.ActiveCombat
            && ReferenceEquals(room.Ui, _overlay.GetParent());
        bool currentScreen = active && ActiveScreenContext.Instance.IsCurrent(room!);
        if (!currentScreen)
        {
            _bubbles.Clear();
            Hide();
            _dirty = true;
            return;
        }

        string language = MegaCrit.Sts2.Core.Localization.LocManager.Instance.Language;
        if (_language != language)
        {
            if (_language != null) _bubbles.Clear();
            _language = language;
            _dirty = true;
        }

        if (CanShowOverlay)
        {
            if (_dirty)
            {
                _dirty = false;
                _overlay.ShowContent(HintText.Title, BuildOverlayContent());
            }
        }
        else Hide();

        if (!CanShowBubbles) { _bubbles.Clear(); return; }
        foreach (var due in _bubbles.Advance(delta))
        {
            var hand = Hand(due.Key).ToArray();
            var content = Content(hand);
            if (!due.HasNewMatches(content.VisibleTriggers)) continue;
            var bubble = NSpeechBubbleVfx.Create(HintText.BubblePrefix + content.Text, due.Key, ComboHintSettings.BubbleDurationSeconds,
                due.Key.Player?.Character?.SpeechBubbleColor ?? VfxColor.White);
            if (bubble != null)
            {
                room!.CombatVfxContainer.AddChild(bubble);
                _activeBubbles.Add(bubble);
                HintHover.Attach(bubble, bubble.GetNode<MegaRichTextLabel>("%Text"), content.HoverTips);
            }
        }
    }

    private HintContent BuildOverlayContent()
    {
        var lines = new List<string>();
        var tips = new Dictionary<string, IHoverTip>();
        int index = 0;
        foreach (var player in _state.Players)
        {
            var hand = Hand(player.Creature).ToArray();
            var cards = hand.Select(HintText.Describe).ToArray();
            string name = HintText.Escape(HintText.PlayerName(player.Creature));
            string scope = "p" + index++ + ".";
            var content = HintContent.Build(_matcher.MatchCards(cards), _rules, hand, scope);
            if (content.Text.Length > 0) lines.Add(name + HintText.Has + content.Text);
            foreach (var entry in content.HoverTips) tips.Add(entry.Key, entry.Value);
        }
        return new HintContent(lines.Count == 0 ? HintText.NoMatches : string.Join("\n", lines), tips);
    }

    private List<MatchedTrigger> MatchHand(Creature owner, CardModel? excluding = null) =>
        Content(Hand(owner).Where(c => !ReferenceEquals(c, excluding)).ToArray()).VisibleTriggers.ToList();

    private HintContent Content(CardModel[] cards) =>
        HintContent.Build(_matcher.MatchCards(cards.Select(HintText.Describe)), _rules, cards);

    private static IEnumerable<CardModel> Hand(Creature owner) =>
        owner.Player?.PlayerCombatState?.Hand.Cards ?? Array.Empty<CardModel>();

    private void Hide()
    {
        if (_overlay != null && GodotObject.IsInstanceValid(_overlay)) _overlay.HideContent();
    }

    public void FrameFailed()
    {
        _bubbles.Clear();
        _dirty = false; // Retry on the next real state change, rather than flooding logs every frame.
        Hide();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _bubbles.Clear();
        ClearActiveBubbles();
        Hide();
        if (_overlay != null && GodotObject.IsInstanceValid(_overlay)) _overlay.QueueFree();
        _overlay = null;
    }

    private void ClearActiveBubbles()
    {
        foreach (var bubble in _activeBubbles)
            if (GodotObject.IsInstanceValid(bubble) && !bubble.IsQueuedForDeletion()) bubble.QueueFree();
        _activeBubbles.Clear();
    }
}
