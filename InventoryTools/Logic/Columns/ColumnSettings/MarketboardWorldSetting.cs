using System.Collections.Generic;
using System.Linq;
using CriticalCommonLib.MarketBoard;
using CriticalCommonLib.Models;
using Dalamud.Plugin.Services;
using InventoryTools.Logic.Columns.Abstract.ColumnSettings;
using InventoryTools.Misc;
using InventoryTools.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.Logging;

namespace InventoryTools.Logic.Columns.ColumnSettings;

public class MarketboardWorldSetting : ChoiceColumnSetting<(uint,string)?>
{
    private readonly ExcelSheet<World> _worldSheet;
    private readonly UniversalisAvailability _universalisAvailability;
    public override string EmptyText => "Home World";

    public MarketboardWorldSetting(ILogger<MarketboardWorldSetting> logger, ImGuiService imGuiService, ExcelSheet<World> worldSheet, UniversalisAvailability universalisAvailability) : base(logger, imGuiService)
    {
        _worldSheet = worldSheet;
        _universalisAvailability = universalisAvailability;
    }
    public override (uint,string)? CurrentValue(ColumnConfiguration configuration)
    {
        configuration.GetSetting(Key, out uint? value);
        if (value == null)
        {
            return null;
        }

        if (value.Value == 0)
        {
            return (0, "Active World");
        }

        var world = _worldSheet.GetRowOrDefault(value.Value);
        if (world == null)
        {
            return null;
        }
        return (world.Value.RowId, world.Value.Name.ExtractText());
    }

    public uint SelectedWorldId(ColumnConfiguration configuration, Character character)
    {
        var settingValue = CurrentValue(configuration);
        var selectedWorld = character.WorldId;
        if (settingValue != null)
        {
            if (settingValue.Value.Item1 == 0)
            {
                selectedWorld = character.ActiveWorldId;
            }
            else
            {
                selectedWorld = settingValue.Value.Item1;
            }
        }

        return selectedWorld;
    }

    public override void ResetFilter(ColumnConfiguration configuration)
    {
        configuration.SetSetting(Key, (uint?)null);
    }

    public override void UpdateColumnConfiguration(ColumnConfiguration configuration, (uint,string)? newValue)
    {
        configuration.SetSetting(Key, newValue?.Item1 ?? null);
    }

    public override string Key { get; set; } = "MBWorld";
    public override string Name { get; set; } = "World".Loc();
    public override string HelpText { get; set; } = "The world for this column to display?".Loc();
    public override (uint,string)? DefaultValue { get; set; } = null;
    public override List<(uint,string)?> GetChoices(ColumnConfiguration configuration)
    {
        // 被排除的伺服器不當查價目標,所以不出現在這個欄位的選單裡。已經設好的舊值
        // 照樣顯示(CurrentValue 直接查表,不經過這份清單)。
        List<(uint RowId, string FormattedName)?> worlds = _worldSheet.Where(c => c.IsPriceableWorld(_universalisAvailability)).Select(c =>((uint, string)?)(c.RowId, c.Name.ExtractText())).ToList();
        worlds.Insert(0,(0,"Active World"));
        return worlds;
    }

    public override string GetFormattedChoice(ColumnConfiguration filterConfiguration, (uint,string)? choice)
    {
        return choice?.Item2 ?? "Active World";
    }
}