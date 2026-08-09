using InventoryTools.Logic.Settings.Abstract;
using InventoryTools.Logic.Settings.Abstract.Generic;
using InventoryTools.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.Logging;

namespace InventoryTools.Logic.Settings;

/// <summary>
/// Turns the recolouring of the game's own "crafting material" line on.
/// </summary>
/// <remarks>
/// Default off. This one changes text the game wrote rather than adding text of its own, so it is
/// the tweak most likely to surprise someone who did not ask for it. Stored in the generic
/// key/value store, so an absent key returns the default handed in here.
/// </remarks>
public class TooltipCraftingMaterialHighlightSetting : GenericBooleanSetting
{
    public TooltipCraftingMaterialHighlightSetting(ILogger<TooltipCraftingMaterialHighlightSetting> logger,
        ImGuiService imGuiService) : base(
        "TooltipCraftingMaterialHighlight",
        "Highlight Crafting Material".Loc(),
        "Colours the game's own 'crafting material' line in the item tooltip so it is easier to spot. This changes text the game already displays rather than adding a line.".Loc(),
        false,
        SettingCategory.ToolTips,
        SettingSubCategory.CraftingMaterialHighlight,
        "1.12.0.11",
        logger,
        imGuiService)
    {
    }
}

public class TooltipCraftingMaterialHighlightColorSetting : GenericGameColorSetting
{
    public override uint? Order { get; } = 1;

    public TooltipCraftingMaterialHighlightColorSetting(
        ILogger<TooltipCraftingMaterialHighlightColorSetting> logger, ImGuiService imGuiService,
        ExcelSheet<UIColor> uiColorSheet) : base(
        "TooltipCraftingMaterialHighlightColor",
        "Text Colour".Loc(),
        "What colour should the game's 'crafting material' line be changed to?".Loc(),
        null,
        SettingCategory.ToolTips,
        SettingSubCategory.CraftingMaterialHighlight,
        "1.12.0.11",
        logger,
        imGuiService,
        uiColorSheet)
    {
        // 540 is #FFB619 in the TC UIColor sheet - the same amber the ingredient patch tooltip
        // already uses, so the crafting-related additions stay one colour family.
        this.DefaultValue = 540;
    }
}
