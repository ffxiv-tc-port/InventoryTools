using System.Collections.Generic;
using System.Linq;
using InventoryTools.Logic.Settings.Abstract;
using InventoryTools.Logic.Settings.Abstract.Generic;
using InventoryTools.Services;
using InventoryTools.Services.Interfaces;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.Logging;

namespace InventoryTools.Logic.Settings;

public class TooltipDisplayCuratedListsSetting : GenericBooleanSetting
{
    public TooltipDisplayCuratedListsSetting(ILogger<TooltipDisplayCuratedListsSetting> logger,
        ImGuiService imGuiService) : base(
        "TooltipDisplayCuratedLists",
        "Add Curated List Info".Loc(),
        "When hovering an item, show which of your curated lists already contain it.".Loc(),
        false,
        SettingCategory.ToolTips,
        SettingSubCategory.CuratedLists,
        "15.0.12",
        logger,
        imGuiService)
    {
    }
}

public class TooltipCuratedListsMatchQualitySetting : GenericBooleanSetting
{
    public override uint? Order { get; } = 2;

    public TooltipCuratedListsMatchQualitySetting(ILogger<TooltipCuratedListsMatchQualitySetting> logger,
        ImGuiService imGuiService) : base(
        "TooltipCuratedListsMatchQuality",
        "Match Item Quality".Loc(),
        "Should the quality of the hovered item match the quality stored in the curated list? When off, a NQ item will match a HQ entry and vice versa.".Loc(),
        false,
        SettingCategory.ToolTips,
        SettingSubCategory.CuratedLists,
        "15.0.12",
        logger,
        imGuiService)
    {
    }
}

public class TooltipCuratedListsColorSetting : GenericGameColorSetting
{
    public override uint? Order { get; } = 3;

    public TooltipCuratedListsColorSetting(ILogger<TooltipCuratedListsColorSetting> logger,
        ImGuiService imGuiService, ExcelSheet<UIColor> uiColorSheet) : base(
        "TooltipCuratedListsColor",
        "Text Colour".Loc(),
        "When enabled, what colour should the text be for the 'Curated List' tooltip text be?".Loc(),
        null,
        SettingCategory.ToolTips,
        SettingSubCategory.CuratedLists,
        "15.0.12",
        logger,
        imGuiService,
        uiColorSheet)
    {
        this.DefaultValue = 535;
    }
}

/// <summary>要檢查的精選清單（存清單 Key）；空清單＝全部檢查。</summary>
public class TooltipCuratedListsSetting : MultipleChoiceSetting<string>
{
    private readonly IListService _listService;

    public TooltipCuratedListsSetting(ILogger<TooltipCuratedListsSetting> logger, ImGuiService imGuiService,
        IListService listService) : base(logger, imGuiService)
    {
        _listService = listService;
    }

    public override List<string> DefaultValue { get; set; } = new();

    public override List<string> CurrentValue(InventoryToolsConfiguration configuration)
    {
        return configuration.TooltipCuratedLists ?? DefaultValue;
    }

    public override void UpdateFilterConfiguration(InventoryToolsConfiguration configuration, List<string> newValue)
    {
        configuration.TooltipCuratedLists = newValue;
    }

    public override string Key { get; set; } = "TooltipCuratedLists";
    public override string Name { get; set; } = "Curated Lists to Check".Loc();
    public override string HelpText { get; set; } = "Which curated lists should be checked? If no lists are picked, every curated list is checked.".Loc();
    public override SettingCategory SettingCategory { get; set; } = SettingCategory.ToolTips;
    public override SettingSubCategory SettingSubCategory { get; } = SettingSubCategory.CuratedLists;
    public override string Version { get; } = "15.0.12";
    public override uint? Order { get; } = 1;

    public override Dictionary<string, string> GetChoices(InventoryToolsConfiguration configuration)
    {
        return _listService.Lists
            .Where(c => c.FilterType == FilterType.CuratedList)
            .DistinctBy(c => c.Key)
            .ToDictionary(c => c.Key, c => c.NameFormatted);
    }

    public override string GetPreviewValue(List<string> items)
    {
        return items.Count == 0 ? "All lists".Loc() : "?? lists selected".Loc(items.Count);
    }

    public override bool HideAlreadyPicked { get; set; } = true;
}
