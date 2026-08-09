using InventoryTools.Logic.Settings.Abstract;
using InventoryTools.Logic.Settings.Abstract.Generic;
using InventoryTools.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.Logging;

namespace InventoryTools.Logic.Settings;

/// <summary>
/// Turns the "which classes/jobs are the contents for" tooltip line on.
/// </summary>
/// <remarks>
/// Default off, like every other optional tooltip module in this plugin. Note this rides on
/// <see cref="InventoryToolsConfiguration.Get(string,bool?)"/>, the generic key/value store, not
/// on a dedicated property: the default is supplied at the call site and a key that is absent
/// from the json simply returns it, so the DefaultValueHandling.IgnoreAndPopulate trap that bites
/// dedicated properties does not apply here.
/// </remarks>
public class TooltipDisplayCofferContentsSetting : GenericBooleanSetting
{
    public TooltipDisplayCofferContentsSetting(ILogger<TooltipDisplayCofferContentsSetting> logger,
        ImGuiService imGuiService) : base(
        "TooltipDisplayCofferContents",
        "Add Coffer Contents (Classes/Jobs)".Loc(),
        "When hovering a coffer, a weapon box, a treasure map reward or anything else that contains other items, adds a line listing which classes and jobs the contents are for. Where the game has its own name for that exact set of jobs it is used instead of a list.".Loc(),
        false,
        SettingCategory.ToolTips,
        SettingSubCategory.CofferContents,
        "1.12.0.11",
        logger,
        imGuiService)
    {
    }
}

public class TooltipCofferContentsColorSetting : GenericGameColorSetting
{
    public override uint? Order { get; } = 1;

    public TooltipCofferContentsColorSetting(ILogger<TooltipCofferContentsColorSetting> logger,
        ImGuiService imGuiService, ExcelSheet<UIColor> uiColorSheet) : base(
        "TooltipCofferContentsColor",
        "Text Colour".Loc(),
        "When enabled, what colour should the text be for the 'Coffer Contents' tooltip text be?".Loc(),
        null,
        SettingCategory.ToolTips,
        SettingSubCategory.CofferContents,
        "1.12.0.11",
        logger,
        imGuiService,
        uiColorSheet)
    {
        this.DefaultValue = 502;
    }
}
