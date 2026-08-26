using System;
using InventoryTools.Logic.Settings.Abstract;

namespace InventoryTools.Extensions
{
    public static class SettingSubCategoryExtensions
    {
        public static string FormattedName(this SettingSubCategory settingSubCategory)
        {
            switch (settingSubCategory)
            {
                case SettingSubCategory.Experimental:
                    return "Experimental".Loc();
                case SettingSubCategory.Fun:
                    return "Fun".Loc();
                case SettingSubCategory.Highlighting:
                    return "Highlighting".Loc();
                case SettingSubCategory.DestinationHighlighting:
                    return "Destination Highlighting".Loc();
                case SettingSubCategory.RetainerHighlighting:
                    return "Retainer Highlighting".Loc();
                case SettingSubCategory.Market:
                    return "Market".Loc();
                case SettingSubCategory.General:
                    return "General".Loc();
                case SettingSubCategory.Subsetting:
                    return "Settings".Loc();
                case SettingSubCategory.Visuals:
                    return "Visuals".Loc();
                case SettingSubCategory.WindowLayout:
                    return "Window Layout".Loc();
                case SettingSubCategory.AutoSave:
                    return "Auto Save".Loc();
                case SettingSubCategory.FilterSettings:
                    return "List Settings".Loc();
                case SettingSubCategory.ActiveLists:
                    return "Active Lists".Loc();
                case SettingSubCategory.ContextMenus:
                    return "Context/Right Click Menu".Loc();
                case SettingSubCategory.Hotkeys:
                    return "Hotkeys".Loc();
                case SettingSubCategory.IgnoreEscape:
                    return "Ignore Escape Key".Loc();
                case SettingSubCategory.SourceGrouping:
                    return "Source Grouping".Loc();
                case SettingSubCategory.UseGrouping:
                    return "Use Grouping".Loc();
                case SettingSubCategory.Colours:
                    return "Colours".Loc();
                case SettingSubCategory.AddItemLocations:
                    return "Add Item Locations".Loc();
                case SettingSubCategory.MarketPricing:
                    return "Market Pricing".Loc();
                case SettingSubCategory.AmountToRetrieve:
                    return "Amount To Retrieve".Loc();
                case SettingSubCategory.ItemUnlockStatus:
                    return "Item Unlock Status".Loc();
                case SettingSubCategory.SourceInformation:
                    return "Source Information".Loc();
                case SettingSubCategory.UseInformation:
                    return "Use Information".Loc();
                case SettingSubCategory.AcquisitionTracker:
                    return "Acquisition Tracker".Loc();
                case SettingSubCategory.IngredientPatch:
                    return "Ingredient Patch".Loc();
            }
            return settingSubCategory.ToString().Loc();
        }
    }
}