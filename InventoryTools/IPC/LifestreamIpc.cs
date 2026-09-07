using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc.Exceptions;
using Dalamud.Plugin.Services;

namespace InventoryTools.IPC;

/// <summary>
/// 單向橋接到 Lifestream：把角色帶到地圖上的某一點（自動選最近的乙太之光傳送＋沿路走／飛過去）。
/// </summary>
/// <remarks>
/// 🔴 <b>零組件相依。</b>只用 Dalamud 原生 CallGate 的字串契約，對方沒安裝時
/// <see cref="IsAvailable"/> 回 <see langword="false"/>，UI 就把「前往」按鈕變灰，不會有任何錯誤。
/// <para>
/// 🔴 <b>契約逐字取自 Lifestream 的 <c>IPC/IPCProvider.cs</c></b>：
/// <c>bool GoToMapPoint(uint territory, float worldX, float worldZ, bool fly)</c>。
/// 它的文件註解寫著「參數順序與型別是對外契約，已有消費端照此接線，不要改」。
/// ⚠️ <b>吃的是世界座標，不是地圖座標。</b>換算在 <see cref="Services.VendorLookupService"/>。
/// </para>
/// <para>
/// 🔴 <b>絕不呼叫 <c>Lifestream.ExecuteCommand</c>（也就是 <c>/li</c>）。</b>
/// 那條在參數為空時的語意是「跨世界傳送」，不是本功能要的東西。
/// </para>
/// <para>
/// 📌 對方的 <c>GoToMapPoint</c> 自己包了 <c>IpcFrameworkGate</c>（會自己切到 framework 執行緒），
/// 所以呼叫端不必為它排隊；但 <c>IsBusy</c> 沒有閘門，只是讀幾個欄位，
/// 因此本檔規定<b>兩者都只從主執行緒（framework／繪製）呼叫</b>。
/// </para>
/// <para>
/// 📌 <see cref="IsAvailable"/> 會節流：對方沒安裝時每次探測都是一個例外，
/// 而這個值是每一幀、每一列都要問的，所以量到的結果快取 <see cref="ProbeInterval"/> 秒。
/// </para>
/// </remarks>
public class LifestreamIpc
{
    /// <summary>對方外掛的內部名稱，只用在記錄檔與提示的措辭上；判斷在不在一律靠 IPC 本身。</summary>
    public const string PluginName = "Lifestream";

    /// <summary><c>Func&lt;bool&gt;</c>：對方現在有沒有正在跑的編排。同時兼作「在不在」的探測。</summary>
    public const string TagIsBusy = "Lifestream.IsBusy";

    /// <summary><c>Func&lt;uint, float, float, bool, bool&gt;</c>：前往某區域的某個世界座標點。</summary>
    public const string TagGoToMapPoint = "Lifestream.GoToMapPoint";

    /// <summary>「在不在／忙不忙」的探測間隔。</summary>
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(2);

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IPluginLog _pluginLog;

    private DateTime _nextProbe = DateTime.MinValue;
    private bool _installed;
    private bool _busy;
    private bool _loggedNotInstalled;

    public LifestreamIpc(IDalamudPluginInterface pluginInterface, IPluginLog pluginLog)
    {
        _pluginInterface = pluginInterface;
        _pluginLog = pluginLog;
    }

    /// <summary>
    /// 對方裝了、載入了，而且現在沒有在跑別的編排。
    /// </summary>
    /// <remarks>
    /// 📌 這個值是節流過的快取（<see cref="ProbeInterval"/>），所以最壞情況會慢個兩秒才反映
    /// 「Lifestream 剛剛開始忙」——這只影響按鈕的灰不灰，按下去之後對方自己還會再判一次忙不忙。
    /// </remarks>
    public bool IsAvailable()
    {
        var now = DateTime.UtcNow;
        if (now < _nextProbe)
        {
            return _installed && !_busy;
        }

        _nextProbe = now + ProbeInterval;
        try
        {
            _busy = _pluginInterface.GetIpcSubscriber<bool>(TagIsBusy).InvokeFunc();
            _installed = true;
            _loggedNotInstalled = false;
        }
        catch (IpcNotReadyError)
        {
            _installed = false;
            _busy = false;
            if (!_loggedNotInstalled)
            {
                _loggedNotInstalled = true;
                _pluginLog.Information(
                    $"[{PluginName}] 製作清單的「前往」按鈕需要它，但它沒有安裝或尚未載入" +
                    $"（IPC「{TagIsBusy}」沒有人註冊）。按鈕會維持灰色，其餘功能不受影響。");
            }
        }
        catch (Exception e)
        {
            _installed = false;
            _busy = false;
            _pluginLog.Information(e, $"[{PluginName}] 探測失敗，這次當作不可用。");
        }

        return _installed && !_busy;
    }

    /// <summary>
    /// 請 Lifestream 把角色帶到指定區域的指定世界座標。
    /// </summary>
    /// <param name="territory">目標區域的 TerritoryType 列號。</param>
    /// <param name="worldX">世界座標 X。</param>
    /// <param name="worldZ">世界座標 Z（<b>不是</b> Y，Y 是高度，對方抵達後自己向 vnavmesh 問地板）。</param>
    /// <param name="fly">允許用飛行坐騎跑最後一段。區域不可飛時對方會自動退回地面路線。</param>
    /// <param name="reason">寫進記錄用的來源描述，讓 log 分得出是哪一條邊觸發的。</param>
    /// <returns>
    /// 對方有沒有真的把任務排進佇列。<see langword="false"/> 的常見原因：Lifestream 正在忙、
    /// 角色不可互動（讀取中／過場）、沒裝 vnavmesh、或那個區域沒有已解鎖的乙太之光。
    /// </returns>
    public bool TryGoToMapPoint(uint territory, float worldX, float worldZ, bool fly, string reason)
    {
        if (territory == 0)
        {
            return false;
        }

        try
        {
            var queued = _pluginInterface.GetIpcSubscriber<uint, float, float, bool, bool>(TagGoToMapPoint)
                .InvokeFunc(territory, worldX, worldZ, fly);

            // 📌 Information 級：這是「使用者說按了沒反應」時唯一問得出真相的一行。
            _pluginLog.Information(
                $"[{PluginName}] {reason}：GoToMapPoint(區域 {territory}, X {worldX:F1}, Z {worldZ:F1}, 飛行 {fly}) 回傳 {queued}。");
            return queued;
        }
        catch (IpcNotReadyError)
        {
            _pluginLog.Information($"[{PluginName}] {reason}：它沒有安裝或尚未載入，這次不前往。");
            return false;
        }
        catch (Exception e)
        {
            _pluginLog.Information(e, $"[{PluginName}] {reason}：呼叫 GoToMapPoint 失敗，這次不前往。");
            return false;
        }
    }
}
