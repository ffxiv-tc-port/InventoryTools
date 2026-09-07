using System;
using System.Collections.Generic;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc.Exceptions;
using Dalamud.Plugin.Services;

namespace InventoryTools.IPC;

/// <summary>
/// <c>ItemVendorLocation.GetItemVendorsWorld</c> 回傳的一筆商人資料的<b>鏡像型別</b>。
/// </summary>
/// <remarks>
/// 🔴 <b>成員名逐字取自對方的 <c>IPC/VendorLocationInfo.cs</c>，不可以「差不多就好」地改名。</b>
/// CallGate 在兩邊型別不同時走 JSON 來回轉換，名字打錯的失敗形式是
/// <b>那個欄位靜默變成預設值</b>，不是例外。
/// <para>
/// 📌 缺欄位是安全的（Newtonsoft 忽略多出來的鍵）——這裡刻意<b>不</b>抄
/// <c>ItemId</c>／<c>MapId</c>／<c>SourceType</c>，本外掛用不到。
/// </para>
/// </remarks>
public class VendorLocationInfo
{
    /// <summary>商人的 ENpcBase／ENpcResident 列號。</summary>
    public uint NpcId { get; set; }

    /// <summary>商人名稱（客戶端語言）。</summary>
    public string NpcName { get; set; } = string.Empty;

    /// <summary>商店名稱；沒有就是空字串。</summary>
    public string ShopName { get; set; } = string.Empty;

    /// <summary>
    /// 這個商人有沒有已知的所在位置。
    /// </summary>
    /// <remarks>
    /// 🔴 <see langword="false"/> 時底下所有座標欄位都是 0，<b>那個 0 沒有意義</b>——
    /// 依這個旗標判斷，不要把 0 當成「在原點」。
    /// </remarks>
    public bool HasLocation { get; set; }

    /// <summary>所在區域的 TerritoryType 列號。可以直接餵 <c>Lifestream.GoToMapPoint</c>。</summary>
    public uint TerritoryTypeId { get; set; }

    /// <summary>世界座標 X。</summary>
    public float WorldX { get; set; }

    /// <summary>世界座標 <b>Z</b>（不是地圖上的 Y）。<c>GoToMapPoint</c> 的第三個參數要的就是它。</summary>
    public float WorldZ { get; set; }

    /// <summary>地圖座標 X（遊戲內地圖上顯示的那組數字），只拿來顯示。</summary>
    public float MapX { get; set; }

    /// <summary>地圖座標 Y，只拿來顯示。</summary>
    public float MapY { get; set; }

    /// <summary>
    /// <see cref="MapX"/>／<see cref="MapY"/> 算得出來嗎。
    /// </summary>
    /// <remarks>🔴 <see langword="false"/> 時那兩個欄位是 0，同樣不要把它當座標顯示。</remarks>
    public bool MapCoordinatesKnown { get; set; }

    /// <summary>要付的代價；可能是空清單（例如任務獎勵）。</summary>
    public List<VendorCostInfo> Costs { get; set; } = [];
}

/// <summary>一筆代價的鏡像型別。🔴 成員名同樣是跨外掛契約。</summary>
public class VendorCostInfo
{
    /// <summary>數量。</summary>
    public uint Amount { get; set; }

    /// <summary>貨幣／材料名稱（客戶端語言）。</summary>
    public string CurrencyName { get; set; } = string.Empty;
}

/// <summary>
/// 唯讀橋接到「道具商人位置」(ItemVendorLocation，以下簡稱 IVL)：問「這個道具哪個 NPC 有賣」。
/// </summary>
/// <remarks>
/// 🔴 <b>零組件相依。</b>只用 Dalamud 原生 CallGate 的字串契約（本外掛沒有 ECommons），
/// 對方沒安裝時每一條路徑都是安靜的「查不到」，InventoryTools 這邊完全無感。
/// <para>
/// 🔑 <b>兩個端點，優先用新的那個。</b>
/// <see cref="TagGetItemVendorsWorld"/>（IVL 7.20.0.19 起）直接給<b>世界座標</b>＋商人名／商店名／代價，
/// 型別是有名字的類別，接得乾淨；舊的 <see cref="TagGetItemVendors"/> 只給<b>地圖座標</b>與巢狀 tuple，
/// 要自己反解成世界座標才能餵 Lifestream。
/// ⇒ 先試新的，擲 <c>IpcNotReadyError</c>（＝對方是舊版）才退回舊的。
/// <b>兩條都留著</b>，這樣使用者的 IVL 是舊版也不會整個功能消失。
/// </para>
/// <para>
/// ⚠️ 端點名寫錯不會有錯誤訊息，只會永遠得到「這個頻道沒有人註冊」——<b>靜默斷線</b>，
/// 所以名字寫成常數，不散在呼叫點上。
/// </para>
/// <para>
/// 🔴 <b>只能從主執行緒（framework／繪製）呼叫。</b>IPC 的實作是在<b>呼叫端</b>的執行緒上跑的，
/// 從背景 Task 叫過去等於把對方的程式碼拉到背景執行緒。目前唯一的呼叫點是
/// <see cref="Services.VendorLookupService"/>，而它只被製作清單的繪製路徑用到。
/// </para>
/// <para>
/// 📌 IVL 的資料表（<c>ItemLookup</c>）是在它自己的建構子裡<b>同步</b>建完之後才註冊 IPC 的
/// （<c>EntryPoint.cs</c>：先 <c>ItemLookup = new()</c>，之後才 <c>new ItemVendorLocationIpc()</c>），
/// 所以「呼叫得通」就等於「資料已經齊了」，不會有「先回空、之後才有」的中間狀態。
/// ⇒ 呼叫成功時的空結果可以安心快取成「這個道具沒有 NPC 賣」。
/// </para>
/// <para>
/// 📌 每次呼叫都重新取 subscriber，不快取。IVL 可以在 InventoryTools 載入之後才被裝上／重載，
/// 快取住的 subscriber 在那之後的行為沒有保證；重取的成本只是一次字典查詢。
/// </para>
/// </remarks>
public class ItemVendorLocationIpc
{
    /// <summary>對方外掛的內部名稱，只用在記錄檔與提示的措辭上；判斷在不在一律靠 IPC 本身。</summary>
    public const string PluginName = "ItemVendorLocation";

    /// <summary>
    /// <c>Func&lt;uint, List&lt;VendorLocationInfo&gt;?&gt;</c>：查某個道具的商人，附<b>世界座標</b>。
    /// IVL 7.20.0.19 起才有。
    /// </summary>
    public const string TagGetItemVendorsWorld = "ItemVendorLocation.GetItemVendorsWorld";

    /// <summary>
    /// <c>Func&lt;uint, bool, HashSet&lt;(uint npcId, uint territory, (float x, float y))&gt;?&gt;</c>：
    /// 舊端點，只給<b>地圖座標</b>。第二個參數 <c>filterNoLocation</c> 為 <see langword="true"/> 時
    /// 對方會略過沒有位置的商人。
    /// </summary>
    /// <remarks>
    /// 🔴 ValueTuple 的元素名字只存在於編譯期，執行期型別就是
    /// <c>ValueTuple&lt;uint, uint, ValueTuple&lt;float, float&gt;&gt;</c>，兩邊參照同一個 corelib 型別，
    /// 所以這一條<b>不需要（也不可以）</b>另外做鏡像型別。
    /// </remarks>
    public const string TagGetItemVendors = "ItemVendorLocation.GetItemVendors";

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IPluginLog _pluginLog;

    /// <summary>「對方沒安裝」只寫一次記錄，不要每開一次製作清單就刷一行。</summary>
    private bool _loggedNotInstalled;

    /// <summary>「對方是舊版、已退回舊端點」也只寫一次。</summary>
    private bool _loggedLegacyFallback;

    public ItemVendorLocationIpc(IDalamudPluginInterface pluginInterface, IPluginLog pluginLog)
    {
        _pluginInterface = pluginInterface;
        _pluginLog = pluginLog;
    }

    /// <summary>
    /// 問 IVL「這個道具有哪些 NPC 商人」，優先走給世界座標的新端點。
    /// </summary>
    /// <param name="itemId">道具 id。</param>
    /// <param name="vendors">
    /// 查到的商人（<b>包含沒有位置的</b>，請看 <see cref="VendorLocationInfo.HasLocation"/>）。
    /// 只有回傳 <see langword="true"/> 時才有意義。
    /// </param>
    /// <returns>
    /// <see langword="true"/> = 新端點在，而且回答了。
    /// <see langword="false"/> = 對方沒安裝、或它是<b>沒有這個端點的舊版</b>——
    /// 呼叫端應該接著試 <see cref="TryGetItemVendorsByMapCoords"/>。
    /// </returns>
    public bool TryGetItemVendorsWorld(uint itemId, out List<VendorLocationInfo> vendors)
    {
        vendors = [];
        try
        {
            var result = _pluginInterface
                .GetIpcSubscriber<uint, List<VendorLocationInfo>?>(TagGetItemVendorsWorld)
                .InvokeFunc(itemId);

            _loggedNotInstalled = false;

            // null ＝「這個道具不在它的表裡」；空清單 ＝「在表裡但沒有商人」。
            // 兩者對我們是同一件事：問到了，沒東西。
            if (result != null)
            {
                vendors = result;
            }

            return true;
        }
        catch (IpcNotReadyError)
        {
            return false;
        }
        catch (Exception e)
        {
            _pluginLog.Information(e, $"[{PluginName}] 查詢道具 {itemId} 的商人（世界座標端點）失敗，改試舊端點。");
            return false;
        }
    }

    /// <summary>
    /// 舊端點：只拿得到<b>地圖座標</b>的商人清單。給 IVL 版本太舊的使用者用。
    /// </summary>
    /// <param name="itemId">道具 id。</param>
    /// <param name="vendors">
    /// 查到的商人（已要求對方<b>略過沒有位置的</b>）。只有回傳 <see langword="true"/> 時才有意義。
    /// </param>
    /// <returns>
    /// <see langword="true"/> = 對方在、而且回答了。
    /// <see langword="false"/> = <b>問不到</b>（沒安裝／尚未載入／對方擲例外）——
    /// 呼叫端必須把這個狀態顯示成「不知道」，<b>絕不可以畫成「沒有」</b>。
    /// </returns>
    public bool TryGetItemVendorsByMapCoords(uint itemId, out HashSet<(uint npcId, uint territory, (float x, float y))> vendors)
    {
        vendors = [];
        try
        {
            var result = _pluginInterface
                .GetIpcSubscriber<uint, bool, HashSet<(uint npcId, uint territory, (float x, float y))>?>(TagGetItemVendors)
                .InvokeFunc(itemId, true);

            _loggedNotInstalled = false;
            if (result != null)
            {
                vendors = result;
            }

            if (!_loggedLegacyFallback)
            {
                _loggedLegacyFallback = true;
                _pluginLog.Information(
                    $"[{PluginName}] 它沒有提供「{TagGetItemVendorsWorld}」（7.20.0.19 起才有），" +
                    $"已退回舊的「{TagGetItemVendors}」並自行把地圖座標反解成世界座標。" +
                    "功能一樣可用，只是商人名字改由本外掛自己查 ENpcResident。");
            }

            return true;
        }
        catch (IpcNotReadyError)
        {
            // 對方沒安裝／還沒載入。這是完全正常的狀態，不是錯誤。
            if (!_loggedNotInstalled)
            {
                _loggedNotInstalled = true;
                _pluginLog.Information(
                    $"[{PluginName}] 製作清單想標「哪裡買」，但它沒有安裝或尚未載入" +
                    $"（IPC「{TagGetItemVendors}」沒有人註冊）。那一欄會顯示灰色的「?」，" +
                    "InventoryTools 其餘功能完全不受影響。");
            }

            return false;
        }
        catch (Exception e)
        {
            // 型別不合、對方版本不同、或它自己的實作裡爆掉。同樣不可以往上冒去打斷繪製。
            _pluginLog.Information(e, $"[{PluginName}] 查詢道具 {itemId} 的商人失敗，這次當作查不到。");
            return false;
        }
    }
}
