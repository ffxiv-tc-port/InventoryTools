using InventoryTools.Logic.Settings.Abstract;
using InventoryTools.Logic.Settings.Abstract.Generic;
using InventoryTools.Services;
using Microsoft.Extensions.Logging;

namespace InventoryTools.Logic.Settings;

/// <summary>
/// 背包快滿時請 TataruPraise（塔塔露誇獎）念一句的總開關。
/// </summary>
/// <remarks>
/// 預設開啟。這不是「預設多一個提示音」——沒有安裝 TataruPraise 的人這條路徑整條是 no-op，
/// 裝了的人也可以在 TataruPraise 自己的設定視窗單獨關掉「背包快滿」這個情境。
/// <para>
/// 📌 這個設定走的是 <see cref="InventoryToolsConfiguration.Get(string,bool?)"/> 這個泛用鍵值表，
/// 不是專屬屬性：設定檔用 <c>DefaultValueHandling.IgnoreAndPopulate</c> 讀回來，
/// 專屬屬性只要在既有使用者的 json 裡缺鍵就會被<b>填成 default(T)</b>（也就是 <c>false</c>），
/// 而鍵值表的預設值是在呼叫點給的，缺鍵時直接回那個值——所以只有這條路能讓既有使用者也吃到預設開啟。
/// </para>
/// </remarks>
public class BagAlmostFullPraiseSetting : GenericBooleanSetting
{
    public BagAlmostFullPraiseSetting(ILogger<BagAlmostFullPraiseSetting> logger, ImGuiService imGuiService) : base(
        BagSpacePraiseService.EnabledSettingKey,
        "Speak when bags are nearly full".Loc(),
        "When your character's main bags drop to the number of free slots below, asks the TataruPraise plugin to say one short line. Only the four main bag pages are counted - crystals, currency, the armoury chest and the chocobo saddlebag are not. It speaks once per fill-up: nothing more is said until you have freed up space again. Does nothing at all if TataruPraise is not installed.".Loc(),
        true,
        SettingCategory.General,
        SettingSubCategory.General,
        "1.13.0.0",
        logger,
        imGuiService)
    {
    }
}

/// <summary>
/// 「還剩幾格算快滿」的門檻。
/// </summary>
public class BagAlmostFullPraiseThresholdSetting : GenericIntegerSetting
{
    public override uint? Order { get; } = 1;

    public BagAlmostFullPraiseThresholdSetting(ILogger<BagAlmostFullPraiseThresholdSetting> logger, ImGuiService imGuiService) : base(
        BagSpacePraiseService.ThresholdSettingKey,
        "Free slots before speaking".Loc(),
        "How few free slots in the main bags (140 slots in total) counts as nearly full. The default of 10 is meant to warn you before your next dungeon or gathering trip rather than once loot has already started bouncing. Setting it to 0 means you are only told once the bags are completely full.".Loc(),
        BagSpacePraiseService.DefaultThreshold,
        SettingCategory.General,
        SettingSubCategory.General,
        "1.13.0.0",
        logger,
        imGuiService)
    {
    }
}
