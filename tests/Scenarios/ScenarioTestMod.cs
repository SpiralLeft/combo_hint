using System.Reflection;
using ComboHint;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.AutoSlay.Scenarios;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Platform.Steam;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace ComboHintScenarioTests;

[ModInitializer(nameof(Initialize))]
public static class ScenarioTestMod
{
    public static int BubbleCount;
    public static string LastBubble = "";
    public static NSpeechBubbleVfx? LastBubbleNode;
    public static bool ExtendNextBubble;
    public static CombatHintController? PreviousController;
    public static int Assertions;

    public static void Initialize()
    {
        Require(CommandLineHelper.HasArg("combohint-tests"), "Missing --combohint-tests guard.");
        Require(!SteamInitializer.Initialized, "Steam must be disabled for isolated tests.");
        string root = CommandLineHelper.GetValue("combohint-test-root") ?? "";
        Require(root.Length > 0 && Path.GetFullPath(OS.GetUserDataDir()).StartsWith(
            Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
            "User data directory must be inside the explicit test root.");
        var create = typeof(NSpeechBubbleVfx).GetMethod(nameof(NSpeechBubbleVfx.Create),
            new[] { typeof(string), typeof(Creature), typeof(double), typeof(VfxColor) })!;
        new Harmony("ComboHintScenarioTests").Patch(create,
            postfix: new HarmonyMethod(typeof(ScenarioTestMod), nameof(RecordBubble)));
        ModEntry.Logger.Info("[ScenarioTests] Isolated user data: " + OS.GetUserDataDir());
        // Retail NGame skips this public runner. This bridge is deliberately test-only.
        Callable.From(ScenarioRunner.StartFromCommandLine).CallDeferred();
    }

    public static void RecordBubble(string text, Creature speaker, double secondsToDisplay, VfxColor vfxColor, NSpeechBubbleVfx? __result)
    {
        if (__result == null || !text.StartsWith(HintText.BubblePrefix, StringComparison.Ordinal)) return;
        Require(vfxColor == (speaker.Player?.Character?.SpeechBubbleColor ?? VfxColor.White),
            "ComboHint bubble must use its speaker's native character color.");
        Require(secondsToDisplay == ComboHintSettings.BubbleDurationSeconds,
            "ComboHint bubble must use the live configured duration.");
        BubbleCount++;
        LastBubble = text;
        LastBubbleNode = __result;
        // Test-only: keep the one mouse-interaction bubble alive long enough for screenshots.
        if (ExtendNextBubble)
        {
            typeof(NSpeechBubbleVfx).GetProperty(nameof(NSpeechBubbleVfx.SecondsToDisplay))!.SetValue(__result, 15d);
            ExtendNextBubble = false;
        }
        ModEntry.Logger.Info("[ScenarioTests] Bubble " + BubbleCount + ": " + text);
    }

    public static T Field<T>(object owner, string name) => (T)(owner.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)
        ?? throw new InvalidOperationException("Unbound field " + name));

    public static ComboHintOverlayNode Overlay()
    {
        Require(NCombatRoom.Instance != null, "No live combat room.");
        var overlays = NCombatRoom.Instance!.Ui.GetChildren().OfType<ComboHintOverlayNode>().ToArray();
        Require(overlays.Length == 1, "Expected exactly one overlay, got " + overlays.Length);
        return overlays[0];
    }

    public static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

public sealed class ComboHintTestCommand : AbstractConsoleCmd
{
    public override string CmdName => "combohint_test";
    public override string Args => "<catalog|effect-catalog|hover-next|hover-effect <overlay|bubble> <key> [card]|unhover-effect|tip-gone|free-bubble|clear|mark|visible|hidden|contains|absent|bubbles|bubble-contains|remember|fresh|settings|complete> [args]";
    public override string Description => "Assert ComboHint state in the isolated scenario runner.";
    public override bool IsNetworked => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        try
        {
            ScenarioTestMod.Require(args.Length > 0, "Missing operation.");
            if (args[0] == "clear")
            {
                var state = CombatManager.Instance.DebugOnlyGetState()
                    ?? throw new InvalidOperationException("No combat state.");
                var combat = state.Players.Single().PlayerCombatState
                    ?? throw new InvalidOperationException("Player has no combat state.");
                return new CmdResult(CardPileCmd.RemoveFromCombat(combat.Hand.Cards.ToArray(), skipVisuals: false),
                    true, "Cleared hand through game commands.");
            }
            if (args[0] == "mad-science")
            {
                var state = CombatManager.Instance.DebugOnlyGetState()!;
                var player = state.Players.Single();
                var card = (MadScience)state.CreateCard(ModelDb.Card<MadScience>(), player);
                card.TinkerTimeType = CardType.Attack;
                card.TinkerTimeRider = Enum.Parse<TinkerTime.RiderEffect>(args[1]);
                return new CmdResult(CardPileCmd.Add(card, PileType.Hand), true, "Generated dynamic card.");
            }
            switch (args[0])
            {
                case "config-menu": ConfigMenuTest.Show(); break;
                case "config-expand": ConfigMenuTest.Expand(); break;
                case "config-scroll": ConfigMenuTest.ScrollToEffects(); break;
                case "config-hover": ConfigMenuTest.HoverLastEffect(); break;
                case "config-hover-visible": ConfigMenuTest.RequireHover(); break;
                case "config-close": ConfigMenuTest.Close(); break;
                case "duration":
                    ComboHintSettings.BubbleDurationSeconds = double.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
                    typeof(ModEntry).GetMethod("NotifySettingsChanged", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
                    break;
                case "remaining-features":
                    RemainingFeaturesTest.Verify();
                    break;
                case "effect":
                    typeof(ComboHintSettings).GetProperty("Enable" + args[1], BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase)!.SetValue(null, bool.Parse(args[2]));
                    typeof(ModEntry).GetMethod("NotifySettingsChanged", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
                    break;
                case "effect-catalog":
                    BubbleEffectsTest.VerifyAndExport();
                    break;
                case "hover-next":
                    ScenarioTestMod.ExtendNextBubble = true;
                    break;
                case "hover-effect":
                    BubbleEffectsTest.Hover(args[1], args[2], args.Length > 3 && args[3] == "card");
                    break;
                case "no-hover":
                    BubbleEffectsTest.NoHover(args[1]);
                    break;
                case "unhover-effect":
                    BubbleEffectsTest.Unhover();
                    break;
                case "tip-gone":
                    BubbleEffectsTest.RequireGone();
                    break;
                case "free-bubble":
                    ScenarioTestMod.LastBubbleNode!.QueueFree();
                    break;
                case "catalog":
                    ModEntry.Logger.Info("[ScenarioTests] Card catalog: " + CardCatalog.Export());
                    break;
                case "mark":
                    ScenarioTestMod.BubbleCount = 0;
                    ScenarioTestMod.LastBubble = "";
                    break;
                case "visible":
                case "hidden":
                    ScenarioTestMod.Require(ScenarioTestMod.Field<Control>(ScenarioTestMod.Overlay(), "_box").Visible
                        == (args[0] == "visible"), "Unexpected overlay visibility.");
                    break;
                case "contains":
                case "absent":
                    string text = ScenarioTestMod.Field<MegaRichTextLabel>(ScenarioTestMod.Overlay(), "_description").Text;
                    string expected = string.Join(' ', args.Skip(1));
                    ScenarioTestMod.Require(text.Contains(expected, StringComparison.Ordinal) == (args[0] == "contains"),
                        "Overlay expectation failed for '" + expected + "': " + text);
                    break;
                case "bubbles":
                    ScenarioTestMod.Require(ScenarioTestMod.BubbleCount == int.Parse(args[1]),
                        "Expected " + args[1] + " new bubbles, got " + ScenarioTestMod.BubbleCount);
                    break;
                case "bubble-contains":
                    ScenarioTestMod.Require(ScenarioTestMod.LastBubble.Contains(string.Join(' ', args.Skip(1)),
                        StringComparison.Ordinal), "Bubble text mismatch: " + ScenarioTestMod.LastBubble);
                    break;
                case "remember":
                    ScenarioTestMod.PreviousController = ScenarioTestMod.Field<CombatHintController>(ScenarioTestMod.Overlay(), "_controller");
                    break;
                case "fresh":
                    var current = ScenarioTestMod.Field<CombatHintController>(ScenarioTestMod.Overlay(), "_controller");
                    ScenarioTestMod.Require(ScenarioTestMod.PreviousController != null
                        && !ReferenceEquals(current, ScenarioTestMod.PreviousController), "Combat reused its old controller.");
                    ScenarioTestMod.Require(ScenarioTestMod.Field<bool>(ScenarioTestMod.PreviousController!, "_disposed"),
                        "Previous combat controller was not disposed.");
                    break;
                case "settings":
                    ComboHintSettings.EnableOverlay = bool.Parse(args[1]);
                    ComboHintSettings.EnableBubbles = bool.Parse(args[2]);
                    ComboHintSettings.EnableSinglePlayer = bool.Parse(args[3]);
                    typeof(ModEntry).GetMethod("NotifySettingsChanged", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
                    break;
                case "complete":
                    ModEntry.Logger.Info("[ScenarioTests] ALL ASSERTIONS PASSED: " + ScenarioTestMod.Assertions);
                    return new CmdResult(true, "All live assertions passed.");
                default: throw new InvalidOperationException("Unknown operation " + args[0]);
            }
            if (args[0] is not ("mark" or "settings" or "remember")) ScenarioTestMod.Assertions++;
            ModEntry.Logger.Info("[ScenarioTests] PASS " + string.Join(' ', args));
            return new CmdResult(true, "Assertion passed.");
        }
        catch (Exception ex)
        {
            ModEntry.Logger.Error("[ScenarioTests] FAIL " + string.Join(' ', args) + ": " + ex);
            return new CmdResult(false, ex.Message);
        }
    }
}
