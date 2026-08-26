using System.Collections.Generic;
using InventoryTools.Logic.Settings.Abstract;
using InventoryTools.Logic.Settings.Abstract.Generic;
using InventoryTools.Services;
using Microsoft.Extensions.Logging;

namespace InventoryTools.Logic.Settings;

public enum CraftWindowView
{
    Crafts,
    Tree,
    Configuration
}

public class CraftWindowViewSetting : GenericEnumChoiceSetting<CraftWindowView>
{
    public CraftWindowViewSetting(ILogger<CraftWindowViewSetting> logger, ImGuiService imGuiService) : base("CraftWindowView", "Craft Window View".Loc(), "What view is the craft list currently in?".Loc(),CraftWindowView.Crafts, new Dictionary<CraftWindowView, string>(){
        { CraftWindowView.Crafts , "Crafts".Loc()}, { CraftWindowView.Configuration, "Configuration".Loc()},
        { CraftWindowView.Tree, "Treeview".Loc()}}, SettingCategory.None, SettingSubCategory.None, "12.0.20", logger, imGuiService)
    {
    }
}