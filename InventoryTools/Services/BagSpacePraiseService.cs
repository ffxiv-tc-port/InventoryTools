using CriticalCommonLib.Extensions;
using CriticalCommonLib.Models;
using CriticalCommonLib.Services;
using Dalamud.Plugin.Services;
using InventoryTools.IPC;
using GameInventoryType = FFXIVClientStructs.FFXIV.Client.Game.InventoryType;

namespace InventoryTools.Services;

/// <summary>
/// 「背包快滿了」的語音提醒：算出角色主背包還剩幾格，跨過門檻時請 TataruPraise 念一句。
/// </summary>
/// <remarks>
/// 🔴 <b>純通知。</b>不整理背包、不丟東西、不開視窗、不動任何既有流程。叫不到 TataruPraise 就當沒這回事。
/// <para>
/// 🔴🔴 <b>去重是這個功能的全部難度。</b>背包在門檻附近會反覆抖動（撿一件、丟一件、堆疊合併），
/// 「每次低於門檻就念」等於每撿一件東西念一次。所以這裡只在<b>下降緣</b>出聲，
/// 而且要回到 <c>門檻 + <see cref="RearmMargin"/></c> 格以上才會重新武裝。
/// 中間那段是刻意留的遲滯區（hysteresis），不是隨手加的緩衝。
/// </para>
/// <para>
/// ⚠️ <b>只算主背包四頁</b>（<see cref="InventoryCategory.CharacterBags"/> ＝ Bag0~Bag3，4×35＝140 格）。
/// 水晶、貨幣、兵裝庫、陸行鳥鞍囊、僱員都<b>不算</b>——它們各自有獨立的容量，
/// 而且「兵裝庫滿了」跟「撿不到東西了」是兩件事。容器分類直接沿用 CriticalCommonLib 的既有分類，
/// 不另造一套（見 <c>CriticalCommonLib/Extensions/EnumExtensions.GetTypes</c>）。
/// </para>
/// </remarks>
public class BagSpacePraiseService
{
    /// <summary>總開關的設定鍵。<b>與 <see cref="Logic.Settings.BagAlmostFullPraiseSetting"/> 共用。</b></summary>
    public const string EnabledSettingKey = "TataruPraiseBagAlmostFull";

    /// <summary>門檻的設定鍵。<b>與 <see cref="Logic.Settings.BagAlmostFullPraiseThresholdSetting"/> 共用。</b></summary>
    public const string ThresholdSettingKey = "TataruPraiseBagAlmostFullThreshold";

    /// <summary>
    /// 預設門檻：剩 <b>10</b> 格。
    /// </summary>
    /// <remarks>
    /// 📌 主背包共 140 格。挑 10 的理由是「還來得及處理」而不是「已經來不及了」：
    /// 一趟副本或一輪採集常見會多出 5~15 種新物品，門檻設在 10 表示在<b>下一趟出發前</b>就會被提醒，
    /// 而不是在戰利品掉不進來的當下才知道。再往上調（例如 20）在正常遊玩時會太早響，
    /// 再往下調（例如 3）則常常來不及在下一次撿東西前處理完。
    /// </remarks>
    public const int DefaultThreshold = 10;

    /// <summary>
    /// 重新武裝需要回升的格數。
    /// </summary>
    /// <remarks>
    /// 🔴 這個值就是遲滯區的寬度。設成 0 等於沒有遲滯——在門檻上下抖一格就會再念一次。
    /// 5 格的意思是「使用者真的動手清了一些東西」，不是「賣掉一件又撿回來一件」。
    /// </remarks>
    private const int RearmMargin = 5;

    private readonly IInventoryMonitor _inventoryMonitor;
    private readonly IInventoryScanner _inventoryScanner;
    private readonly ICharacterMonitor _characterMonitor;
    private readonly InventoryToolsConfiguration _configuration;
    private readonly TataruPraiseIpc _tataruPraiseIpc;
    private readonly IPluginLog _pluginLog;

    /// <summary>已經為目前這一輪「快滿」念過了，回到安全水位之前不再念。</summary>
    private bool _warned;

    /// <summary>上一次評估時的角色。換角要把 <see cref="_warned"/> 清掉，不然新角色會被上一個角色的狀態壓著。</summary>
    private ulong _latchedCharacterId;

    public BagSpacePraiseService(
        IInventoryMonitor inventoryMonitor,
        IInventoryScanner inventoryScanner,
        ICharacterMonitor characterMonitor,
        InventoryToolsConfiguration configuration,
        TataruPraiseIpc tataruPraiseIpc,
        IPluginLog pluginLog)
    {
        _inventoryMonitor = inventoryMonitor;
        _inventoryScanner = inventoryScanner;
        _characterMonitor = characterMonitor;
        _configuration = configuration;
        _tataruPraiseIpc = tataruPraiseIpc;
        _pluginLog = pluginLog;
    }

    /// <summary>
    /// 背包有變動時評估一次。<b>必須在主執行緒上呼叫</b>（IPC 的實作跑在呼叫端的執行緒上）。
    /// </summary>
    public void Evaluate()
    {
        if (_configuration.Get(EnabledSettingKey, true) != true)
        {
            // 關掉的時候順手把閂子清掉，這樣重新打開時是乾淨的狀態，不會因為關閉期間的歷史而沉默。
            _warned = false;
            return;
        }

        var characterId = _characterMonitor.ActiveCharacterId;
        if (characterId == 0)
        {
            _warned = false;
            _latchedCharacterId = 0;
            return;
        }

        if (characterId != _latchedCharacterId)
        {
            _latchedCharacterId = characterId;
            _warned = false;
        }

        var freeSlots = CountFreeCharacterBagSlots(characterId);
        if (freeSlots == null)
        {
            // 🔑 分不出「真的 0 格」與「還沒掃到」的時候，一律當成「不知道」。
            // 這裡回 0 的失敗形式是「一登入就喊背包滿了」，而且完全靜默——所以寧可漏報。
            return;
        }

        var threshold = _configuration.Get(ThresholdSettingKey, DefaultThreshold) ?? DefaultThreshold;
        if (threshold < 0)
        {
            threshold = 0;
        }

        if (freeSlots.Value <= threshold)
        {
            if (_warned)
            {
                return;
            }

            _warned = true;
            _pluginLog.Information(
                $"[TataruPraise] 主背包只剩 {freeSlots.Value} 格（門檻 {threshold}），請它念一句「背包快滿」。");
            _tataruPraiseIpc.TryPraiseBagAlmostFull($"主背包剩 {freeSlots.Value} 格");
        }
        else if (freeSlots.Value >= threshold + RearmMargin)
        {
            if (_warned)
            {
                _pluginLog.Information(
                    $"[TataruPraise] 主背包回到 {freeSlots.Value} 格，「背包快滿」重新武裝。");
            }

            _warned = false;
        }
    }

    /// <summary>
    /// 算出角色主背包（Bag0~Bag3）還有幾個空格。
    /// </summary>
    /// <returns>空格數；<b>資料還不完整時回 <see langword="null" /></b>，代表「不知道」而不是「0 格」。</returns>
    /// <remarks>
    /// 🔴 兩道閘門缺一不可：
    /// <list type="number">
    /// <item>四個主背包都要真的在記憶體裡（<c>IInventoryScanner.InMemory</c>）——
    /// 這一條擋掉「從設定檔載入的舊快照」：存檔時空格是被濾掉的，只憑快照算會把空格算成 0。</item>
    /// <item>140 個槽位一個 <see langword="null" /> 都不能有——
    /// 陣列是先配置好再逐格填的，<see langword="null" /> 代表這一格從來沒被掃過。</item>
    /// </list>
    /// 少了任何一道，失敗形式都是<b>登入瞬間喊一聲「背包快滿」</b>，而且看起來像功能正常。
    /// </remarks>
    private int? CountFreeCharacterBagSlots(ulong characterId)
    {
        if (!_inventoryScanner.InMemory.Contains(GameInventoryType.Inventory1) ||
            !_inventoryScanner.InMemory.Contains(GameInventoryType.Inventory2) ||
            !_inventoryScanner.InMemory.Contains(GameInventoryType.Inventory3) ||
            !_inventoryScanner.InMemory.Contains(GameInventoryType.Inventory4))
        {
            return null;
        }

        if (!_inventoryMonitor.Inventories.TryGetValue(characterId, out var inventory))
        {
            return null;
        }

        var freeSlots = 0;
        foreach (var inventoryType in InventoryCategory.CharacterBags.GetTypes())
        {
            var bag = inventory.GetInventoryByType(inventoryType);
            if (bag == null)
            {
                return null;
            }

            foreach (var slot in bag)
            {
                if (slot == null)
                {
                    return null;
                }

                if (slot.ItemId == 0)
                {
                    freeSlots++;
                }
            }
        }

        return freeSlots;
    }
}
