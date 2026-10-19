using InventoryTools.Logic.Settings.Abstract;
using InventoryTools.Logic.Settings.Abstract.Generic;
using InventoryTools.Services;
using Microsoft.Extensions.Logging;

namespace InventoryTools.Logic.Settings;

public class ShopHighlightingDisableItemsSetting : GenericBooleanSetting
{
    public ShopHighlightingDisableItemsSetting(ILogger<ShopHighlightingDisableItemsSetting> logger, ImGuiService imGuiService) : base("ShopHighlightingDisableItems", "Shop Highlighting - Disable Items".Loc(), "When highlighting items in a shop, should the not highlighted items be disabled?".Loc(), false, SettingCategory.Highlighting, SettingSubCategory.General, "1.12.0.15", logger, imGuiService)
    {
    }
}