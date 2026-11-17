using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BaseLib.Config;
using BaseLib.Patches.UI;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Modding;

namespace ComboHint;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public const string ModId = "ComboHint";
    public static Logger Logger { get; } = new(ModId, LogType.Generic);
    public static string ModRootPath { get; private set; } = string.Empty;
    public static HintRules Rules { get; private set; } = HintRules.Empty;

    private static readonly ConditionalWeakTable<CombatState, ComboHintCombatObserver> Observers = new();
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;
        ModRootPath = ModManager.Mods.FirstOrDefault(m => m.manifest?.id == ModId)?.path
            ?? Path.GetDirectoryName(typeof(ModEntry).Assembly.Location)
            ?? Directory.GetCurrentDirectory();
        LoadRules();

        Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(Assembly.GetExecutingAssembly());
        var settings = new ComboHintSettings();
        settings.MigrateLegacySettings(ModRootPath);
        ModConfigRegistry.Register(ModId, settings);
        settings.ConfigChanged += (_, _) => NotifySettingsChanged();
        settings.OnConfigReloaded += NotifySettingsChanged;

        ModHelper.SubscribeForCombatStateHooks(ModId, state => new AbstractModel[] { GetObserver(state) });
        ExtraCombatUi.RegisterCombatUiElement((ui, _, state) =>
        {
            try
            {
                var controller = GetObserver(state).Controller;
                var overlay = new ComboHintOverlayNode { Name = "ComboHintOverlay" };
                overlay.Initialize(controller);
                ui.AddChild(overlay);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to create combat UI: {ex}");
            }
        });

        _initialized = true;
        Logger.Info($"Initialized: {Rules.Groups.Count} trigger groups, explicit Model ID rules loaded.");
    }

    private static ComboHintCombatObserver GetObserver(CombatState state) =>
        Observers.GetValue(state, s => ComboHintCombatObserver.CreateForCombat(s, Rules));

    private static void LoadRules()
    {
        try
        {
            Rules = HintRules.Parse(File.ReadAllText(Path.Combine(ModRootPath, "combo_hint.config.json")));
        }
        catch (Exception ex)
        {
            Logger.Error($"Could not load combo_hint.config.json: {ex.Message}");
            Rules = HintRules.Empty;
        }
    }

    private static void NotifySettingsChanged()
    {
        foreach (var entry in Observers) entry.Value.Controller.SettingsChanged();
    }
}
