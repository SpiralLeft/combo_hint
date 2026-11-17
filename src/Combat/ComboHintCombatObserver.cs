using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;

namespace ComboHint;

// One observer per real CombatState. UI hints never mutate gameplay or block its task queue.
public sealed class ComboHintCombatObserver : AbstractModel, ICustomModel
{
    private CombatHintController? _controller;
    public CombatHintController Controller => _controller ?? throw new InvalidOperationException("This is the canonical, unbound observer.");
    public override bool ShouldReceiveCombatHooks => _controller != null;

    // Only ModelDb may construct the canonical instance. Runtime instances must be mutable
    // clones: calling any AbstractModel constructor again triggers DuplicateModelException.
    public ComboHintCombatObserver() { }

    public static ComboHintCombatObserver CreateForCombat(CombatState state, HintRules rules)
    {
        var canonical = ModelDb.GetById<ComboHintCombatObserver>(ModelDb.GetId<ComboHintCombatObserver>());
        var observer = (ComboHintCombatObserver)canonical.MutableClone();
        observer._controller = new CombatHintController(state, rules);
        return observer;
    }

    protected override void DeepCloneFields()
    {
        base.DeepCloneFields();
        _controller = null;
    }

    public override Task AfterCardChangedPilesLate(CardModel card, PileType oldPileType, AbstractModel? clonedBy) =>
        Observe(c => c.CardChangedPiles(card, oldPileType));

    public override Task AfterCardPlayedLate(PlayerChoiceContext choiceContext, CardPlay cardPlay) => Observe(c => c.Invalidate());

    public override Task AfterPotionUsed(PotionModel potion, Creature? target) => Observe(c => c.Invalidate());

    public override Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side,
        IReadOnlyList<Creature> participants, ICombatState combatState) => Observe(c => c.StartSide(side));

    public override Task AfterSideTurnStartLate(CombatSide side, IReadOnlyList<Creature> participants,
        ICombatState combatState) => Observe(c => c.Invalidate());

    public override Task BeforeSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side,
        IEnumerable<Creature> participants) => Observe(c => c.EndSide(side));

    public override Task AfterCombatVictory(CombatRoom room) => Observe(c => c.Dispose());
    public override Task AfterCombatEnd(CombatRoom room) => Observe(c => c.Dispose());

    private Task Observe(Action<CombatHintController> action)
    {
        if (_controller == null) return Task.CompletedTask;
        try { action(_controller); }
        catch (Exception ex) { ModEntry.Logger.Error($"Combat hint callback failed: {ex}"); }
        return Task.CompletedTask;
    }
}
