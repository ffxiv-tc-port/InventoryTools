using System;
using System.Collections.Generic;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc.Exceptions;
using Dalamud.Plugin.Services;

namespace InventoryTools.IPC;

/// <summary>正在把背包填滿的是哪一種自動化。</summary>
public enum AutomationKind
{
    None = 0,
    AutoRetainerExpertDeliveryLoop,
    ArtisanEndurance,
    ArtisanList,
}

/// <summary>
/// 唯讀探測：別的外掛現在有沒有在跑「會故意把主背包填滿」的自動化。只讀狀態，不送任何指令。
/// </summary>
/// <remarks>
/// 只從主執行緒呼叫（IPC 實作跑在呼叫端的執行緒上）。
/// 對方沒裝、版本太舊（沒有這個端點）或呼叫失敗，一律當成「沒在跑」：寧可照舊提醒，也不要靜默吞掉。
/// 刻意不用各家的 <c>IsBusy</c>：AutoRetainer 的含多角色模式（開著就恆真）與一般僱員處理，
/// Artisan 的含玩家自己手動製作，都不專屬於「自動化在填背包」。
/// </remarks>
public class AutomationActivityIpc
{
    /// <summary><c>Func&lt;bool&gt;</c>：稀有品繳交循環從開始到停止為真。</summary>
    public const string TagExpertDeliveryLoopRunning = "AutoRetainer.PluginState.IsExpertDeliveryLoopRunning";

    /// <summary><c>Func&lt;bool&gt;</c>：Artisan 耐力模式（連續製作同一配方，含別的外掛經 IPC 叫它做）。</summary>
    public const string TagArtisanEndurance = "Artisan.GetEnduranceStatus";

    /// <summary><c>Func&lt;bool&gt;</c>：Artisan 製作清單處理中，<b>暫停中也算</b>，所以要配 <see cref="TagArtisanListPaused"/>。</summary>
    public const string TagArtisanListRunning = "Artisan.IsListRunning";

    /// <summary><c>Func&lt;bool&gt;</c>：Artisan 製作清單處理中而且暫停著。</summary>
    public const string TagArtisanListPaused = "Artisan.IsListPaused";

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IPluginLog _pluginLog;

    /// <summary>已經寫過「查不到／呼叫失敗」的端點；該端點成功一次就移除，重新失敗才再寫。</summary>
    private readonly HashSet<string> _loggedUnavailable = new();

    public AutomationActivityIpc(IDalamudPluginInterface pluginInterface, IPluginLog pluginLog)
    {
        _pluginInterface = pluginInterface;
        _pluginLog = pluginLog;
    }

    /// <summary>現在正在跑的自動化；沒有或不知道都回 <see cref="AutomationKind.None"/>。</summary>
    public AutomationKind GetActive()
    {
        if (Query(TagExpertDeliveryLoopRunning) == true)
        {
            return AutomationKind.AutoRetainerExpertDeliveryLoop;
        }

        if (Query(TagArtisanEndurance) == true)
        {
            return AutomationKind.ArtisanEndurance;
        }

        // 暫停中的清單不在製作，那段期間背包的變化是玩家自己的。
        if (Query(TagArtisanListRunning) == true && Query(TagArtisanListPaused) == false)
        {
            return AutomationKind.ArtisanList;
        }

        return AutomationKind.None;
    }

    /// <summary>給記錄用的原因說明。</summary>
    public static string Describe(AutomationKind kind) => kind switch
    {
        AutomationKind.AutoRetainerExpertDeliveryLoop => "AutoRetainer 的稀有品繳交循環正在跑（它會故意把背包取到只剩保留格數）",
        AutomationKind.ArtisanEndurance => "Artisan 的耐力模式正在連續製作",
        AutomationKind.ArtisanList => "Artisan 正在跑製作清單",
        _ => "沒有自動化在跑",
    };

    /// <returns>對方的回答；對方不在或呼叫失敗回 <see langword="null" />（不知道）。</returns>
    private bool? Query(string tag)
    {
        try
        {
            var value = _pluginInterface.GetIpcSubscriber<bool>(tag).InvokeFunc();
            _loggedUnavailable.Remove(tag);
            return value;
        }
        catch (IpcNotReadyError)
        {
            if (_loggedUnavailable.Add(tag))
            {
                _pluginLog.Information(
                    $"[TataruPraise] 查不到 IPC「{tag}」（插件沒裝、沒載入或版本太舊），這一項自動化不會擋下「背包快滿」提醒。");
            }

            return null;
        }
        catch (Exception e)
        {
            if (_loggedUnavailable.Add(tag))
            {
                _pluginLog.Information(e, $"[TataruPraise] 呼叫 IPC「{tag}」失敗，這一項自動化當作沒在跑。");
            }

            return null;
        }
    }
}
