using System.Collections.Generic;
using System.Linq;
using CriticalCommonLib.MarketBoard;
using InventoryTools.Logic.Settings.Abstract;
using InventoryTools.Misc;
using InventoryTools.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.Logging;

namespace InventoryTools.Logic.Settings;

public class MarketBoardExtraWorldsSetting : MultipleChoiceSetting<uint>
{
    private readonly ExcelSheet<World> _worldSheet;
    private readonly UniversalisAvailability _universalisAvailability;

    public MarketBoardExtraWorldsSetting(ILogger<MarketBoardExtraWorldsSetting> logger, ImGuiService imGuiService, ExcelSheet<World> worldSheet, UniversalisAvailability universalisAvailability) : base(logger, imGuiService)
    {
        _worldSheet = worldSheet;
        _universalisAvailability = universalisAvailability;
    }

    public override List<uint> DefaultValue { get; set; } = new List<uint>();
    public override List<uint> CurrentValue(InventoryToolsConfiguration configuration)
    {
        return configuration.MarketBoardWorldIds;
    }

    public override void UpdateFilterConfiguration(InventoryToolsConfiguration configuration, List<uint> newValue)
    {
        configuration.MarketBoardWorldIds = newValue;
    }

    public override string Key { get; set; } = "MarketBoardExtraWorlds";
    public override string Name { get; set; } = "Price Worlds".Loc();
    public override string HelpText { get; set; } = "A list of extra worlds we should automatically price".Loc();
    public override SettingCategory SettingCategory { get; set; } = SettingCategory.MarketBoard;
    public override SettingSubCategory SettingSubCategory { get; } = SettingSubCategory.Market;
    public override string Version { get; } = "1.7.0.0";
    private Dictionary<uint, string>? _worldNames;
    private int _worldNamesRevision = -1;

    /// <summary>
    /// 這裡刻意保留被排除的伺服器,只是在名字後面標上「(已排除)」。
    /// 理由:使用者可能在排除之前就已經把那個伺服器選進這份清單,若把它從 choices 裡
    /// 拿掉,基底類別的 DrawResults 會因為查不到名字而整顆按鈕都不畫 ⇒ 那一筆就變成
    /// 看不見也刪不掉的殘留。擋住「再選進來」的是下面的 FilterSearch。
    /// </summary>
    public override Dictionary<uint, string> GetChoices(InventoryToolsConfiguration configuration)
    {
        var exclusionRevision = _universalisAvailability.ExclusionRevision;
        if (_worldNames == null || _worldNamesRevision != exclusionRevision)
        {
            _worldNames = _worldSheet.Where(c => c.IsPublicWorld()).OrderBy(c => c.Name.ExtractText())
                .ToDictionary(
                    c => c.RowId,
                    c => _universalisAvailability.IsWorldExcluded(c.RowId)
                        ? c.Name.ExtractText() + " " + "(excluded)".Loc()
                        : c.Name.ExtractText());
            _worldNamesRevision = exclusionRevision;
        }

        return _worldNames;
    }

    /// <summary>
    /// 被排除的伺服器不出現在「可以加進來」的下拉清單裡(基底類別的 GetActiveChoices
    /// 逐項呼叫這個方法,「Add All」走的也是同一份)。
    /// </summary>
    public override bool FilterSearch(uint itemId, string itemName, string searchString)
    {
        if (_universalisAvailability.IsWorldExcluded(itemId))
        {
            return false;
        }

        return base.FilterSearch(itemId, itemName, searchString);
    }

    public override bool HideAlreadyPicked { get; set; } = true;
}