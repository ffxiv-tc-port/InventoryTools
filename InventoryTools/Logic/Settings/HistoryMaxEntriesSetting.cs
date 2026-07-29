using System;
using CriticalCommonLib.Services;
using InventoryTools.Logic.Settings.Abstract;
using InventoryTools.Services;
using Microsoft.Extensions.Logging;

namespace InventoryTools.Logic.Settings;

public class HistoryMaxEntriesSetting : IntegerSetting
{
    private readonly HostedInventoryHistory _hostedInventoryHistory;

    public HistoryMaxEntriesSetting(ILogger<HistoryMaxEntriesSetting> logger, ImGuiService imGuiService, HostedInventoryHistory hostedInventoryHistory) : base(logger, imGuiService)
    {
        _hostedInventoryHistory = hostedInventoryHistory;
    }

    public override int DefaultValue { get; set; } = 50000;
    public override int CurrentValue(InventoryToolsConfiguration configuration)
    {
        return configuration.HistoryMaxEntries;
    }

    public override void UpdateFilterConfiguration(InventoryToolsConfiguration configuration, int newValue)
    {
        newValue = Math.Max(0, newValue);
        _hostedInventoryHistory.MaxEntries = newValue;
        configuration.HistoryMaxEntries = newValue;
    }

    public override string Key { get; set; } = "HistoryMaxEntries";
    public override string Name { get; set; } = "History Max Entries".Loc();

    public override string HelpText { get; set; } =
        "The maximum number of historical inventory change entries to keep. Once this limit is reached, the oldest entries are removed first. Set to 0 to disable the limit (not recommended - history.csv is fully re-read and re-written every time the plugin starts and stops, so an ever-growing file will make that slower over time).".Loc();

    public override SettingCategory SettingCategory { get; set; } = SettingCategory.History;
    public override SettingSubCategory SettingSubCategory { get; } = SettingSubCategory.General;
    public override string Version => "1.7.0.0";
}
