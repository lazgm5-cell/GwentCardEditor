using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using GwentVisuals;
using HarmonyLib;
using DeckList = Il2CppSystem.Collections.Generic.List<GwentVisuals.CollectionDeck>;

namespace GwentCardEditor.DeckVisibility;

[BepInPlugin(Id, "GWENT Card Editor - Hide Starter Decks", "0.1.0")]
public sealed class DeckVisibilityPlugin : BasePlugin
{
    public const string Id = "local.gwent.cardeditor.hide_starter_decks";
    [ThreadStatic] private static int displayDepth;
    private static ManualLogSource? log;
    private static bool reported;

    public override void Load()
    {
        log = Log;
        var harmony = new Harmony(Id);
        try
        {
            var refresh = Required(typeof(UIDeckListContainer), "RefreshDeckList");
            var selection = Required(typeof(UIDeckSelectorListContainer), "RefreshDeckList");
            var collection = Required(typeof(CollectionData), "GetDeckList");
            var mainMenu = Required(typeof(GwentUnity.MainMenu.DeckSelector), "GetDeckList");
            _ = Required(typeof(CollectionDeck), "get_IsStarterDeck");
            foreach (var method in new[] { refresh, selection })
                harmony.Patch(method, prefix: Hook(nameof(EnterDisplay)), finalizer: Hook(nameof(LeaveDisplay)));
            harmony.Patch(collection, postfix: Hook(nameof(FilterCollectionResult)));
            harmony.Patch(mainMenu, postfix: Hook(nameof(FilterMenuResult)));
            Log.LogInfo("Starter-deck display filters loaded. No decks or cards are deleted.");
        }
        catch (Exception e)
        {
            harmony.UnpatchSelf();
            Log.LogError("Unsupported client: starter-deck filtering disabled. " + e);
        }
    }

    private static MethodInfo Required(Type type, string name) => AccessTools.Method(type, name)
        ?? throw new MissingMethodException(type.FullName, name);
    private static HarmonyMethod Hook(string name) => new(typeof(DeckVisibilityPlugin), name);
    private static void EnterDisplay() => displayDepth++;
    private static void LeaveDisplay() => displayDepth = Math.Max(0, displayDepth - 1);
    private static void FilterCollectionResult(ref DeckList __result)
    {
        if (displayDepth > 0) Filter(ref __result);
    }
    private static void FilterMenuResult(ref DeckList __result) => Filter(ref __result);
    private static void Filter(ref DeckList result)
    {
        if (result == null) return;
        try
        {
            // Copy the presentation list; never remove items from CollectionData itself.
            var filtered = new DeckList();
            int hidden = 0;
            for (int i = 0; i < result.Count; i++)
            {
                var deck = result[i];
                if (deck != null && deck.IsStarterDeck) hidden++;
                else filtered.Add(deck!);
            }
            result = filtered;
            if (!reported && hidden > 0) { reported = true; log?.LogInfo($"Hidden {hidden} starter decks from a presentation list."); }
        }
        catch (Exception e) { log?.LogWarning("Keeping unfiltered deck list: " + e.Message); }
    }
}
