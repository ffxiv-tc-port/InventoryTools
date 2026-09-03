using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AllaganLib.GameSheets.Sheets;
using DalaMock.Host.Mediator;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;
using InventoryTools.Logic;
using InventoryTools.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InventoryTools.IPC;

public interface IChat2Ipc : IHostedService
{
}

public class Chat2Ipc : IChat2Ipc
{
    private readonly ILogger<Chat2Ipc> _logger;
    private readonly ImGuiMenuService _menuService;
    private readonly MediatorService _mediatorService;
    private readonly ItemSheet _itemSheet;

    private ICallGateSubscriber<string> RegisterCallGate { get; }

    private ICallGateSubscriber<string, object?> UnregisterCallGate { get; }

    private ICallGateSubscriber<object?> AvailableCallGate { get; }

    private ICallGateSubscriber<string, PlayerPayload?, ulong, Payload?, SeString?, SeString?, object?> InvokeCallGate { get; }

    private string? _id;

    public Chat2Ipc(IDalamudPluginInterface pluginInterface, ILogger<Chat2Ipc> logger, ImGuiMenuService menuService, MediatorService mediatorService, ItemSheet itemSheet) {
        _logger = logger;
        _menuService = menuService;
        _mediatorService = mediatorService;
        _itemSheet = itemSheet;
        RegisterCallGate = pluginInterface.GetIpcSubscriber<string>("ChatTwo.Register");
        UnregisterCallGate = pluginInterface.GetIpcSubscriber<string, object?>("ChatTwo.Unregister");
        InvokeCallGate = pluginInterface.GetIpcSubscriber<string, PlayerPayload?, ulong, Payload?, SeString?, SeString?, object?>("ChatTwo.Invoke");
        AvailableCallGate = pluginInterface.GetIpcSubscriber<object?>("ChatTwo.Available");
    }

    private void Register() {
        // 刻意不採用上游 0d09a291 的「_id != null 就直接 return」重入防護。
        // 依 ChatTwo 的 IpcManager 實作:ChatTwo.Available 只在 IpcManager 建構子裡送出一次,
        // 而 Register() 每次都回傳新的 GUID 並存進 Registered 清單,Dispose() 會把清單清空。
        // 因此在 _id != null 的狀態下還收到 Available,只可能是 ChatTwo 被重新載入了 ——
        // 此時舊的 _id 已經不在 ChatTwo 新的 Registered 清單裡,而 PayloadHandler.Integrations()
        // 只會對清單內的 id 呼叫 Invoke,舊 id 永遠不會再被呼叫到。
        // 若照上游早退,右鍵選單會在 ChatTwo 重載後永久失效;重新註冊才是正確行為,
        // 且舊 id 隨舊實例一起消失,不會有註冊洩漏。
        try
        {
            _id = RegisterCallGate.InvokeFunc();
            _logger.LogTrace("Attempting to register with chat2");
        }
        catch (IpcNotReadyError)
        {
            // ChatTwo 是選用相依,可能比 InventoryTools 晚載入。
            // 這是預期中的載入順序狀況而不是錯誤:ChatTwo 就緒時會送出 ChatTwo.Available,
            // 屆時 StartAsync 裡的 AvailableCallGate 訂閱會再呼叫一次 Register()。
            // 只把這條路徑降到 Debug(實機 LogLevel 收得到),通用 catch 的 Warning 保持原樣
            // 留給真正非預期的例外,避免沒裝 ChatTwo 的使用者每次啟動都看到警告與堆疊。
            _logger.LogDebug("ChatTwo's IPC is not ready yet; waiting for its ChatTwo.Available event.");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Something went wrong while trying to register with Chat2's IPC. Ignore this if you don't have it installed.");
        }

    }

    public void Disable() {
        // 上游 0d09a291 補的:StartAsync 有 AvailableCallGate.Subscribe(Register) 卻從未取消訂閱。
        // CallGate 通道是 Dalamud 全域且生命週期比外掛長,不取消訂閱的話,本外掛卸載後
        // ChatTwo 下次送出 Available 仍會 DynamicInvoke 到已卸載組件裡的 Register。
        // 這是這顆 commit 裡最實質的修正;先停掉新的註冊來源,再往下做清理。
        AvailableCallGate.Unsubscribe(Register);

        if (_id != null) {
            try
            {
                UnregisterCallGate.InvokeAction(_id);
                _logger.LogTrace("Attempting to unregister with chat2's IPC");
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Something went wrong while trying to unregister with Chat2's IPC. Ignore this if you don't have it installed.");
            }
            _id = null;
        }
        // 刻意不採用上游在此處加的 catch (IpcNotReadyError)。
        // 查本 pin 的 Dalamud:CallGatePubSubBase.Unsubscribe 只轉呼叫 CallGateChannel.Unsubscribe,
        // 內容是 lock 之後的 List.Remove,不會擲 IpcNotReadyError;
        // 只有 InvokeAction / InvokeFunc 在 Action/Func 為 null 時才擲。那個 catch 是死碼。
        try
        {
            InvokeCallGate.Unsubscribe(Integration);
            _logger.LogTrace("Attempting to unsubscribe with chat2's IPC");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Something went wrong while trying to unsubscribe from Chat2's IPC. Ignore this if you don't have it installed.");
        }
    }

    private void Integration(string id, PlayerPayload? sender, ulong contentId, Payload? payload, SeString? senderString, SeString? content) {
        // Make sure the ID is the same as the saved registration ID.
        if (id != _id) {
            return;
        }

        if (payload is ItemPayload itemPayload)
        {
            using(var menu = ImRaii.Menu("Allagan Tools"))
            {
                if (menu)
                {
                    List<MessageBase> messages = [];
                    _menuService.DrawRightClickPopup(new SearchResult(_itemSheet.GetRow(itemPayload.ItemId)), messages);
                    if (messages.Count != 0)
                    {
                        _mediatorService.Publish(messages);
                    }
                }
            }
        }

    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        AvailableCallGate.Subscribe(Register);
        Register();
        InvokeCallGate.Subscribe(Integration);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Disable();
        return Task.CompletedTask;
    }
}
