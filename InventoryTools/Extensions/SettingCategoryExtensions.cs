using InventoryTools.Logic.Settings.Abstract;

namespace InventoryTools.Extensions
{
    public static class SettingCategoryExtensions
    {
        public static string FormattedName(this SettingCategory settingCategory)
        {
            switch (settingCategory)
            {
                case SettingCategory.General:
                    return "General".Loc();
                case SettingCategory.Visuals:
                    return "Visuals".Loc();
                case SettingCategory.MarketBoard:
                    return "Marketboard".Loc();
                case SettingCategory.CraftOverlay:
                    return "Craft Overlay".Loc();
                case SettingCategory.CraftTracker:
                    return "Craft Tracker (Legacy)".Loc();
                case SettingCategory.ToolTips:
                    return "Tooltips".Loc();
                case SettingCategory.Hotkeys:
                    return "Hotkeys".Loc();
                case SettingCategory.History:
                    return "History".Loc();
                case SettingCategory.Windows:
                    return "Windows".Loc();
                case SettingCategory.Lists:
                    return "Lists".Loc();
                case SettingCategory.ContextMenu:
                    return "Context Menu".Loc();
                case SettingCategory.MobSpawnTracker:
                    return "Mob Spawn Tracker".Loc();
                case SettingCategory.TitleMenuButtons:
                    return "Title Menu Button".Loc();
                case SettingCategory.AutoSave:
                    return "Auto Save".Loc();
                case SettingCategory.Items:
                    return "Items".Loc();
                case SettingCategory.Highlighting:
                    return "Highlighting".Loc();
                case SettingCategory.EquipmentRecommendation:
                    return "Equipment Recommendations".Loc();
            }
            return settingCategory.ToString().Loc();
        }
    }
}