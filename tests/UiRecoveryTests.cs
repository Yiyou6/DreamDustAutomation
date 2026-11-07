using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using DreamDustAutomation.UI;

// Only localization is substituted by the runner. RestoreTabs, panel lookup,
// and inventory traversal execute the installed game's real methods.
namespace DreamDustAutomation
{
    internal static class ModEntry
    {
        internal static string Locale = "zh";
        internal static string Text(string key) => Locale + ":" + key;
    }
}

public static class UiRecoveryTests
{
    private sealed class TestItem : GameItem
    {
        internal TestItem() : base("dream_dust_automation.synthesizer") { }
        public override void Validate() { }
        public override void Destroy() { }
    }
    private static int checks;
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static void Check(bool value, string reason)
    { checks++; if (!value) throw new Exception(reason); }
    private static T Unbuilt<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static void Field(object target, string name, object value) => target.GetType().GetField(name, Fields).SetValue(target, value);
    private static MachineryStack Page(params PixelElement[] nodes)
    {
        var page = Unbuilt<MachineryStack>();
        Field(page, "children", nodes.ToList());
        return page;
    }
    private static void CheckStorageRecovery()
    {
        var stock = Enumerable.Range(0, 8).Select(i => Unbuilt<GameGridInventory>()).ToArray();
        PixelElement[] pages = { Page(stock[0], stock[1]), Page(stock[2], stock[3], stock[4], stock[5]), stock[6], stock[7] };
        string[] captions = { "投料", "升级", "成品", "垃圾" };
        var tabs = Unbuilt<MachineryTabs>();
        var tabType = typeof(MachineryTabs).GetNestedType("Tab", BindingFlags.NonPublic);
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(tabType));
        for (int i = 0; i < pages.Length; i++)
        {
            var tab = Activator.CreateInstance(tabType, true);
            Field(tab, "caption", captions[i]); Field(tab, "page", pages[i]);
            list.Add(tab);
        }
        Field(tabs, "tabs", list);
        Field(tabs, "<selected>k__BackingField", 2);
        var panel = Unbuilt<MachineryPanel>();
        panel.role = "storage";
        Field(panel, "<content>k__BackingField", tabs);
        var row = Unbuilt<MachineryPanelRow>();
        Field(row, "panels", new List<MachineryPanel> { panel });
        var window = Unbuilt<PixelWindow>();
        Field(window, "<childElement>k__BackingField", row);
        var item = new TestItem();
        typeof(GameItem).GetProperty("contentWindow").SetValue(item, window);
        // Emulate exactly the saved-state assignment in DecodeNodesInternal.
        var saved = new TagSystem();
        saved.GetTag("STORAGE_TAB_NAMES_STRING").Enable().SetString("old tab 1\nold tab 2");
        saved.GetTag("STORAGE_EXPANSION_TABS_INT").Enable().SetInt(4);
        item.state = saved.Clone(); item.modifiedState = saved.Clone();
        var findTabs = typeof(StorageExpansionHelper).GetMethod("GetTabs", BindingFlags.Static | BindingFlags.NonPublic);
        Check(ReferenceEquals(findTabs.Invoke(null, new object[] { item }), tabs),
            "Positive control: vanilla mistakenly identifies the old mod panel as storage-bay tabs");
        var before = row.children.ToArray();
        Check(before.SequenceEqual(stock), "Eight saved inventory indices match the old machine");
        panel.role = MachineUiNames.StorageRole;
        // 0.3.20: summary and residue cards follow the tabs. Each tab page is
        // wrapped by a presentation-only region, without a new storage node.
        for (int i = 0; i < pages.Length; i++)
        {
            var region = Unbuilt<MachineRegion>();
            Field(region, "<Content>k__BackingField", pages[i]);
            Field(list[i], "page", region);
        }
        var summaryCard = Unbuilt<MachineryPanel>();
        var residueCard = Unbuilt<MachineryPanel>();
        summaryCard.role = "preview"; residueCard.role = "residue";
        Field(summaryCard, "<content>k__BackingField", Page());
        Field(residueCard, "<content>k__BackingField", Page());
        Field(panel, "<content>k__BackingField", Page(tabs, Page(summaryCard, residueCard)));
        Check(findTabs.Invoke(null, new object[] { item }) == null, "Mod panel is outside vanilla's tab rename target");
        for (int load = 0; load < 3; load++)
        {
            StorageExpansionHelper.RestoreTabs(item);
            Check(Enumerable.Range(0, 4).Select(tabs.GetCaption).SequenceEqual(captions), "Repeated reload preserves named tabs");
            Check(row.children.SequenceEqual(before), "Reload preserves every inventory identity and index");
            Check(tabs.selected == 2, "UI repair preserves active tab");
            Check(tabs.onRenameRequest == null, "Native storage rename handler is not attached");
        }
        Check(item.GetTagReadonly("STORAGE_TAB_NAMES_STRING").GetString() == "old tab 1\nold tab 2", "Old save tags left intact");
    }
    private static void CheckNames()
    {
        var lookup = typeof(LocHelper).GetField("HeadlessUiText", BindingFlags.Static | BindingFlags.Public);
        string[] ids = { "dream_dust", "nightmare_dust", "caffeine_pill", "zyanide_pill", "birth_control_pill", "fanta_pill", "pink_alt_injector" };
        foreach (string id in ids)
        {
            var recipe = ChemFinisherRecipe.GetRecipeById(id);
            recipe.displayName = "No translation found for old name";
            foreach (string locale in new[] { "zh", "en" })
            {
                DreamDustAutomation.ModEntry.Locale = locale;
                foreach (string missing in new[] { null, "", "   ", "item_" + id + "_name" })
                {
                    lookup.SetValue(null, missing);
                    Check(MachineUiNames.Product(id) == locale + ":product_" + id,
                        "Missing table/empty/raw key uses current language fallback for " + id);
                }
                lookup.SetValue(null, locale + " fresh " + id);
                Check(MachineUiNames.Product(id) == locale + " fresh " + id, "Current native localized name takes precedence");
            }
            Check(recipe.displayName == "No translation found for old name", "Does not mutate the global native recipe cache");
        }
    }
    public static string Run()
    {
        CheckStorageRecovery(); CheckNames();
        return "UI recovery: " + checks + " checks passed; native save-tab restoration/traversal, 8 inventory identities, 7 names, zh/en and missing localization. Headless test; no live render verification.";
    }
}
