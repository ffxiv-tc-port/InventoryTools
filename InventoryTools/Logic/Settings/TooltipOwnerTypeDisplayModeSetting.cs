using System.Collections.Generic;
using InventoryTools.Logic.Settings.Abstract;
using InventoryTools.Services;
using Microsoft.Extensions.Logging;

namespace InventoryTools.Logic.Settings;

public class TooltipOwnerTypeDisplayModeSetting : ChoiceSetting<TooltipOwnerTypeDisplayMode>
{
    public override TooltipOwnerTypeDisplayMode DefaultValue { get; set; } =
        TooltipOwnerTypeDisplayMode.Never;

    public override TooltipOwnerTypeDisplayMode CurrentValue(InventoryToolsConfiguration configuration)
    {
        return configuration.TooltipOwnerTypeDisplayMode;
    }

    public override void UpdateFilterConfiguration(InventoryToolsConfiguration configuration,
        TooltipOwnerTypeDisplayMode newValue)
    {
        configuration.TooltipOwnerTypeDisplayMode = newValue;
    }

    public override string Key { get; set; } = "TooltipOwnerTypeDisplayMode";
    public override string Name { get; set; } = "Add Item Locations (Owner Type)".Loc();

    public override string WizardName { get; } = "Owner Type".Loc();

    public override string HelpText { get; set; } =
        "Retainer containers are already named apart from the character ones, so you should not normally need this. It is here for what that cannot cover: currency and crystals are one category for characters and retainers alike, and a free company or a house may itself be named after one of your characters. This affixes the kind of owner to the name. This requires 'Add Item Locations?' to be on.";

    public override SettingCategory SettingCategory { get; set; } = SettingCategory.ToolTips;
    public override SettingSubCategory SettingSubCategory { get; } = SettingSubCategory.AddItemLocations;

    public override Dictionary<TooltipOwnerTypeDisplayMode, string> Choices
    {
        get
        {
            return new Dictionary<TooltipOwnerTypeDisplayMode, string>()
            {
                { TooltipOwnerTypeDisplayMode.Never, "Never".Loc() },
                { TooltipOwnerTypeDisplayMode.WhenAmbiguous, "Only when the name is ambiguous".Loc() },
                { TooltipOwnerTypeDisplayMode.Always, "Always".Loc() },
            };
        }
    }

    // Shared with the other 'Add Item Locations' settings on purpose. The configuration wizard
    // announces a setting as a new feature when its Version is not in WizardVersionsSeen, and
    // this is an addition to an existing feature, not a new one to be prompted about.
    public override string Version => "1.7.0.0";

    public TooltipOwnerTypeDisplayModeSetting(ILogger<TooltipOwnerTypeDisplayModeSetting> logger,
        ImGuiService imGuiService) : base(logger, imGuiService)
    {
    }
}
