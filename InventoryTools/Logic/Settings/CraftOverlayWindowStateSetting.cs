using System.Collections.Generic;
using InventoryTools.Logic.Settings.Abstract;
using InventoryTools.Logic.Settings.Abstract.Generic;
using InventoryTools.Services;
using InventoryTools.Ui;
using Microsoft.Extensions.Logging;

namespace InventoryTools.Logic.Settings;

public class CraftOverlayWindowStateSetting : GenericEnumChoiceSetting<CraftOverlayWindowState>
{
    public CraftOverlayWindowStateSetting(ILogger<CraftOverlayWindowStateSetting> logger, ImGuiService imGuiService) : base("CraftOverlayWindowState", "Window State".Loc(), "The current state of the craft overlay window.".Loc(), CraftOverlayWindowState.Single, new Dictionary<CraftOverlayWindowState, string>()
    {
        { CraftOverlayWindowState.Collapsed, "Collapsed".Loc()},
        { CraftOverlayWindowState.Single, "Single".Loc()},
        { CraftOverlayWindowState.List, "Expanded".Loc()},
    }, SettingCategory.None, SettingSubCategory.None, "1.11.0.8", logger, imGuiService)
    {
    }
}