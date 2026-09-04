using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc.Exceptions;
using Dalamud.Plugin.Services;

namespace InventoryTools.IPC;

/// <summary>
/// 單向橋接到「塔塔露誇獎」(TataruPraise)：背包快滿的時候請它念一句。
/// </summary>
/// <remarks>
/// 🔴 <b>零組件相依。</b>只用 Dalamud 原生 CallGate 的字串契約（本外掛沒有 ECommons），
/// 對方沒安裝時本檔的每一條路徑都是 no-op，InventoryTools 這邊完全無感。
/// <para>
/// 🔴 契約名與情境鍵逐字取自 TataruPraise 的 <c>IpcContract.cs</c> 與 <c>Core/PraiseCategory.cs</c>
/// （<c>PraiseCategory.BagAlmostFull</c>）。CallGate 是純字串比對，名字打錯不會有任何錯誤訊息，
/// 只會永遠得到「這個頻道沒有人註冊」——<b>靜默斷線</b>。所以字串都寫成常數，不散在呼叫點上。
/// </para>
/// <para>
/// 🔴 <b>只能從主執行緒（framework tick）呼叫。</b>IPC 的實作是在<b>呼叫端</b>的執行緒上跑的，
/// 從背景 Task 叫過去等於把對方的程式碼拉到背景執行緒。目前唯一的呼叫點是
/// <see cref="Services.BagSpacePraiseService"/>，它掛在 <c>IInventoryMonitor.OnInventoryChanged</c> 上，
/// 而那個事件是 <c>InventoryMonitor</c> 用 <c>RunOnFrameworkThread</c> 送出來的。
/// </para>
/// <para>
/// ⚠️ 這是<b>單向通知</b>：回傳值只拿來寫記錄，不影響 InventoryTools 的任何流程，不重試。
/// </para>
/// <para>
/// 📌 每次呼叫都重新取 subscriber，不快取。TataruPraise 可以在 InventoryTools 載入之後才被裝上／重載，
/// 快取住的 subscriber 在那之後的行為沒有保證；重取的成本只是一次字典查詢。
/// </para>
/// </remarks>
public class TataruPraiseIpc
{
    /// <summary>對方外掛的內部名稱，只用在記錄檔的措辭上；判斷在不在一律靠 IPC 本身。</summary>
    public const string PluginName = "TataruPraise";

    /// <summary><c>Func&lt;bool&gt;</c>：總開關開著而且池裡真的有已合成的語音。</summary>
    public const string TagIsAvailable = "TataruPraise.IsAvailable";

    /// <summary><c>Func&lt;string, bool&gt;</c>：從指定情境的誇獎池挑一句來念。</summary>
    public const string TagPraise = "TataruPraise.Praise";

    /// <summary>
    /// 送過去的情境字串。<b>逐字對應 TataruPraise 內建情境 <c>PraiseCategory.BagAlmostFull</c>。</b>
    /// ⚠️ 這同時是對方 <c>pool.json</c> 的鍵：查不到這個鍵時 <c>Praise</c> 只回 <c>false</c>
    /// （不出聲、不報錯），使用者要自己在對方的設定視窗裡替這個情境生語音。
    /// </summary>
    public const string CategoryBagAlmostFull = "背包快滿";

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IPluginLog _pluginLog;

    /// <summary>「對方沒安裝」只寫一次記錄，不要每次背包滿了都刷一行。</summary>
    private bool _loggedNotInstalled;

    public TataruPraiseIpc(IDalamudPluginInterface pluginInterface, IPluginLog pluginLog)
    {
        _pluginInterface = pluginInterface;
        _pluginLog = pluginLog;
    }

    /// <summary>
    /// 請塔塔露念一句「背包快滿」。對方沒裝、關著、或池裡沒東西，這裡都是安靜的 no-op。
    /// </summary>
    /// <param name="reason">寫進記錄用的來源描述，讓 log 分得出是哪一條邊觸發的。</param>
    /// <returns>對方有沒有把這一句排進播放。只拿來寫記錄，呼叫端不應該據此改變行為。</returns>
    /// <remarks>
    /// 🔴 <b>這個方法自己沒有去重。</b>呼叫端必須確定自己站在「狀態邊緣」上
    /// （剛剛才從「還有空間」跨到「快滿了」），而不是每次背包一動就叫。
    /// 放到輪詢路徑上的話，失敗形式是「一直念」，不是報錯。
    /// </remarks>
    public bool TryPraiseBagAlmostFull(string reason)
    {
        try
        {
            // 先問 IsAvailable：對方的總開關關著、或池裡一句已合成的都沒有，就不要浪費它的冷卻。
            // 這一步同時兼作「對方在不在」的探測——沒註冊就會在這裡擲 IpcNotReadyError。
            if (!_pluginInterface.GetIpcSubscriber<bool>(TagIsAvailable).InvokeFunc())
            {
                return false;
            }

            var accepted = _pluginInterface.GetIpcSubscriber<string, bool>(TagPraise)
                .InvokeFunc(CategoryBagAlmostFull);
            _loggedNotInstalled = false;

            // 📌 Information 級：這是「使用者說沒出聲」時唯一問得出真相的一行。
            // ⚠️ 回傳 false 不是錯誤：可能還在冷卻，也可能「背包快滿」這個情境在池裡一句都沒有。
            _pluginLog.Information(
                $"[{PluginName}] {reason}：Praise(「{CategoryBagAlmostFull}」) 回傳 {accepted}。");
            return accepted;
        }
        catch (IpcNotReadyError)
        {
            // 對方沒安裝／還沒載入。這是完全正常的狀態，不是錯誤。
            if (!_loggedNotInstalled)
            {
                _loggedNotInstalled = true;
                _pluginLog.Information(
                    $"[{PluginName}] 想請它在背包快滿時念一句，但它沒有安裝或尚未載入（IPC「{TagIsAvailable}」沒有人註冊）。" +
                    "這個功能會維持靜默，InventoryTools 其餘功能完全不受影響。");
            }

            return false;
        }
        catch (Exception e)
        {
            // 對方版本不合、簽名對不上、或它自己的回呼裡爆掉。同樣不可以往上冒去打斷背包更新。
            _pluginLog.Information(e, $"[{PluginName}] 呼叫 IPC 失敗，這次不念（原因：{reason}）。");
            return false;
        }
    }
}
