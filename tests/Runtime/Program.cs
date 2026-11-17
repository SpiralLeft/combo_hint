using ComboHint;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Exceptions;
using MegaCrit.Sts2.Core.Runs;

// Runs only managed model code in this test process. No game, Godot scene, saves or Steam init.
ModelDb.Init(new[] { typeof(ComboHintCombatObserver) });
var canonical = ModelDb.GetById<ComboHintCombatObserver>(ModelDb.GetId<ComboHintCombatObserver>());
Require(canonical.IsCanonical && !canonical.ShouldReceiveCombatHooks, "Canonical observer must remain unbound.");

bool reproduced = false;
try { _ = new ComboHintCombatObserver(); }
catch (DuplicateModelException) { reproduced = true; }
Require(reproduced, "The original construction pattern must reproduce DuplicateModelException.");
Console.WriteLine("PASS reproduced original DuplicateModelException against real sts2.dll");

var state1 = NewState();
var state2 = NewState();
var first = ComboHintCombatObserver.CreateForCombat(state1, HintRules.Empty);
var second = ComboHintCombatObserver.CreateForCombat(state2, HintRules.Empty);
Require(first.IsMutable && second.IsMutable, "Runtime observers must be mutable clones.");
Require(first.Id == canonical.Id && second.Id == canonical.Id, "Clones must retain the canonical model ID.");
Require(!ReferenceEquals(first, second) && !ReferenceEquals(first, canonical), "Observer instances must be independent.");
Require(!ReferenceEquals(first.Controller, second.Controller), "Combat controllers must be independent.");
Require(first.ShouldReceiveCombatHooks && second.ShouldReceiveCombatHooks, "Bound observers must receive hooks.");
Require(!canonical.ShouldReceiveCombatHooks, "Binding must not mutate the canonical observer.");
Console.WriteLine("PASS combat factories create independent bound clones without duplicate registration");

var extraClone = (ComboHintCombatObserver)first.MutableClone();
Require(!extraClone.ShouldReceiveCombatHooks, "Cloning a bound observer must not share its combat controller.");
Require(!canonical.ShouldReceiveCombatHooks, "Canonical observer must still be unbound after further clones.");
Console.WriteLine("PASS further clones clear combat-scoped references");
Console.WriteLine("3/3 runtime regression checks passed");

static CombatState NewState() => new(null!, NullRunState.Instance,
    Array.Empty<ModifierModel>(), Array.Empty<BadgeModel>(), Array.Empty<RunModel>(), null!);
static void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
