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

/// <summary>
/// 「不要查價的伺服器」。這是整個外掛唯一一份排除清單的真值來源:設定存在這裡,
/// 執行期灌進 UniversalisAvailability,查價引擎與所有查價目標選單都只問那一個物件。
///
/// 為什麼需要這個:台服的拉姆(4034)已經停止營運,但它在 World 表、在 universalis 的
/// 世界清單裡都還在 ⇒ 既有的兩道判準(World.IsPublic / universalis 認不認得)都攔不住,
/// 每小時的市場快取重新整理會一直對一個不存在的伺服器送請求。
///
/// 排除**不會**動到已經抓下來的價格資料:MarketCache 的快取命中判斷在這道閘門之前,
/// 所以舊資料照樣看得到,只是不再主動去查、也不再出現在可選的查價目標裡。
/// </summary>
public class MarketBoardExcludedWorldsSetting : MultipleChoiceSetting<uint>
{
    private readonly ExcelSheet<World> _worldSheet;
    private readonly UniversalisAvailability _universalisAvailability;

    public MarketBoardExcludedWorldsSetting(ILogger<MarketBoardExcludedWorldsSetting> logger, ImGuiService imGuiService, ExcelSheet<World> worldSheet, UniversalisAvailability universalisAvailability) : base(logger, imGuiService)
    {
        _worldSheet = worldSheet;
        _universalisAvailability = universalisAvailability;
    }

    public override List<uint> DefaultValue { get; set; } = new List<uint>(PublicWorlds.DefaultExcludedWorldIds);

    public override List<uint> CurrentValue(InventoryToolsConfiguration configuration)
    {
        return configuration.MarketBoardExcludedWorldIds;
    }

    public override void UpdateFilterConfiguration(InventoryToolsConfiguration configuration, List<uint> newValue)
    {
        configuration.MarketBoardExcludedWorldIds = newValue;
        _universalisAvailability.SetExcludedWorlds(newValue);
    }

    /// <summary>
    /// 重設。刻意配一份新的 List:基底類別的 Reset 會把 DefaultValue **那個實例本身**
    /// 指派給設定,之後使用者在 UI 上的增刪就會就地改掉 DefaultValue,
    /// 讓「出廠值」跟著跑掉(而且 HasValueSet 從此永遠是 false)。
    /// </summary>
    public override void Reset(InventoryToolsConfiguration configuration)
    {
        UpdateFilterConfiguration(configuration, new List<uint>(DefaultValue));
    }

    public override string Key { get; set; } = "MarketBoardExcludedWorlds";
    public override string Name { get; set; } = "Excluded Worlds".Loc();
    public override string HelpText { get; set; } = "Worlds listed here are never priced automatically and never appear in any pricing world picker. Prices already downloaded for them are kept.".Loc();
    public override SettingCategory SettingCategory { get; set; } = SettingCategory.MarketBoard;
    public override SettingSubCategory SettingSubCategory { get; } = SettingSubCategory.Market;

    // 刻意沿用既有的版本字串:這是既有「市場整合」功能的補充,不是要在設定精靈裡
    // 另外跳一則新功能提示的東西。
    public override string Version { get; } = "1.7.0.0";

    private Dictionary<uint, string>? _worldNames;

    /// <summary>
    /// 這裡刻意給**未過濾**的世界清單:被排除的世界正是這份設定的內容,若把它們濾掉,
    /// 已經選起來的項目在 DrawResults 裡會找不到名字而整個消失,使用者就再也拿不掉了。
    /// </summary>
    public override Dictionary<uint, string> GetChoices(InventoryToolsConfiguration configuration)
    {
        return _worldNames ??= _worldSheet.Where(c => c.IsPublicWorld()).OrderBy(c => c.Name.ExtractText())
            .ToDictionary(c => c.RowId, c => c.Name.ExtractText());
    }

    public override bool HideAlreadyPicked { get; set; } = true;
}
