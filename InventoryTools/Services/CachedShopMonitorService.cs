using System;
using System.Collections.Generic;
using AllaganLib.GameSheets.Model;
using AllaganLib.GameSheets.Sheets;
using AllaganLib.GameSheets.Sheets.Rows;
using AllaganLib.Monitors.Enums;
using AllaganLib.Monitors.Interfaces;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace InventoryTools.Services;

/// <summary>
/// 包在 AllaganLib.Monitors 的 <see cref="IShopMonitorService"/> 外面，只改 <see cref="GetCurrentShopType"/>：
/// 沒有目標時直接回 null（原本比對的是目標的 BaseId，沒有目標一定回 null），
/// 有目標時同一個目標 250 ms 內沿用上一次的結果；換目標、商店視窗開關都立刻重算。
/// 目標的 BaseId 在 ENpcBase 查不到（怪物、玩家、物件）也直接回 null：內層只有在 ENpcBase
/// 查得到那個 BaseId 時才回非 null，所以結果相同。
/// </summary>
/// <remarks>
/// 過了 250 ms 先做一次不配置的輸入指紋掃描（<see cref="Fingerprint"/>），和上次相同就沿用結果，
/// 不同才呼叫內層（內層每個事件處理器配置一個閉包）。AcquisitionMonitorService 在非製作／採集時每幀呼叫。
/// 只在框架執行緒上用。注入具體型別 ShopMonitorService 的地方（例：SelectIconStringOverlay）不經過這一層。
/// 目標直接讀 TargetSystem（同 ITargetManager.Target 的來源與「玩家未載入＝沒有目標」），不建包裝物件；指標只在這次呼叫內用。
/// </remarks>
public sealed class CachedShopMonitorService : IShopMonitorService
{
    private const long CacheWindowMs = 250;

    // 與 AllaganLib.Monitors 1.3.12 內層寫死的數字相同；用數字不用列舉名，CS 世代不同時名稱對應的值會變。
    private const uint AgentIdCheckedWithContent42 = 202;
    private const uint AgentIdShop = 118;

    private readonly IShopMonitorService inner;
    private readonly ENpcBaseSheet enpcBaseSheet;
    private Fingerprint cachedPrint = new();
    private Fingerprint scratchPrint = new();
    private bool hasCache;
    private long cachedAt;
    private (ENpcBaseRow Npc, List<IShop> Shops, IShop? ActiveShop)? cachedValue;

    public CachedShopMonitorService(IShopMonitorService inner, ENpcBaseSheet enpcBaseSheet)
    {
        this.inner = inner;
        this.enpcBaseSheet = enpcBaseSheet;
        this.inner.OnShopChanged += this.Invalidate;
    }

    public event IShopMonitorService.ShopChangedDelegate? OnShopChanged
    {
        add => this.inner.OnShopChanged += value;
        remove => this.inner.OnShopChanged -= value;
    }

    public event IShopMonitorService.ShopOpenedDelegate? OnShopOpened
    {
        add => this.inner.OnShopOpened += value;
        remove => this.inner.OnShopOpened -= value;
    }

    public event IShopMonitorService.ShopClosedDelegate? OnShopClosed
    {
        add => this.inner.OnShopClosed += value;
        remove => this.inner.OnShopClosed -= value;
    }

    public unsafe (ENpcBaseRow Npc, List<IShop> Shops, IShop? ActiveShop)? GetCurrentShopType()
    {
        var target = TargetSystem.Instance()->GetHardTarget();
        if (target == null || !PlayerState.Instance()->IsLoaded || this.enpcBaseSheet.GetRowOrDefault(target->BaseId) == null)
        {
            this.Invalidate();
            return null;
        }

        var key = new TargetKey((nint)target, target->GetGameObjectId(), target->BaseId);
        var now = Environment.TickCount64;
        if (this.hasCache && this.cachedPrint.Target == key && now - this.cachedAt < CacheWindowMs)
        {
            return this.cachedValue;
        }

        var print = this.scratchPrint;
        print.Capture(key);
        if (this.hasCache && print.SameAs(this.cachedPrint))
        {
            this.cachedAt = now;
            return this.cachedValue;
        }

        var value = this.inner.GetCurrentShopType();
        this.scratchPrint = this.cachedPrint;
        this.cachedPrint = print;
        this.cachedValue = value;
        this.cachedAt = now;
        this.hasCache = true;
        return value;
    }

    public IShop? GetShopByIdAndType(uint shopId, ShopType type) => this.inner.GetShopByIdAndType(shopId, type);

    public (uint NpcId, List<(ShopType, uint)> Shops, (ShopType, uint)? ActiveShopId)? GetCurrentShopTypeIds() =>
        this.inner.GetCurrentShopTypeIds();

    private void Invalidate()
    {
        this.hasCache = false;
        this.cachedValue = null;
    }

    private readonly record struct TargetKey(nint Address, ulong GameObjectId, uint BaseId);

    private readonly record struct MatchedHandler(uint Key, nint Handler, uint EventId);

    /// <summary>
    /// 內層 GetCurrentShopTypeIds 讀的全部可變輸入：目標、事件物件裡有目標 BaseId 的處理器（依表的順序），
    /// 以及有命中時才讀的三個介面／代理狀態；其餘輸入是內層建構時就建好、之後不再改的表。
    /// 兩份指紋相同＝內層結果相同。讀取條件照內層：沒有命中就不碰介面與代理（內層也不碰）。
    /// </summary>
    private sealed class Fingerprint
    {
        private static bool proxyInstanceUnavailable;

        private readonly List<MatchedHandler> matches = new(8);
        private bool content42AgentActive;
        private bool shopProxyHandlerPresent;
        private uint shopProxyEventId;
        private nint proxyInstanceHandler;

        public TargetKey Target { get; private set; }

        public unsafe void Capture(TargetKey target)
        {
            this.Target = target;
            this.matches.Clear();
            this.content42AgentActive = false;
            this.shopProxyHandlerPresent = false;
            this.shopProxyEventId = 0;
            this.proxyInstanceHandler = 0;

            var framework = EventFramework.Instance();
            if (framework == null)
            {
                return;
            }

            foreach (var pair in framework->EventHandlerModule.EventHandlerMap)
            {
                var handler = pair.Item2.Value;
                if (handler == null)
                {
                    continue;
                }

                foreach (var eventObject in handler->EventObjects)
                {
                    if (eventObject.Value != null && eventObject.Value->BaseId == target.BaseId)
                    {
                        this.matches.Add(new MatchedHandler(pair.Item1, (nint)handler, handler->Info.EventId.Id));
                        break;
                    }
                }
            }

            if (this.matches.Count == 0)
            {
                return;
            }

            var agentModule = UIModule.Instance()->GetAgentModule();
            var content42Agent = agentModule->GetAgentByInternalId((AgentId)AgentIdCheckedWithContent42);
            this.content42AgentActive = content42Agent != null && content42Agent->IsAgentActive();

            var agentShop = (AgentShop*)agentModule->GetAgentByInternalId((AgentId)AgentIdShop);
            if (agentShop != null && agentShop->IsAgentActive() && agentShop->EventReceiver != null && agentShop->IsAddonReady())
            {
                var proxy = (ShopEventHandler.AgentProxy*)agentShop->EventReceiver;
                if (proxy->Handler != null)
                {
                    this.shopProxyHandlerPresent = true;
                    this.shopProxyEventId = proxy->Handler->Info.EventId.Id;
                }
            }

            var proxyInstance = TryGetProxyInstance();
            this.proxyInstanceHandler = proxyInstance != null ? (nint)proxyInstance->Handler : 0;
        }

        // 內層只在部分分支才呼叫 Instance()；位址解不出時它會擲例外，這裡不能讓它變成每次都擲。
        private static unsafe ShopEventHandler.AgentProxy* TryGetProxyInstance()
        {
            if (proxyInstanceUnavailable)
            {
                return null;
            }

            try
            {
                return ShopEventHandler.AgentProxy.Instance();
            }
            catch (InvalidOperationException)
            {
                proxyInstanceUnavailable = true;
                return null;
            }
        }

        public bool SameAs(Fingerprint other)
        {
            if (this.Target != other.Target
                || this.content42AgentActive != other.content42AgentActive
                || this.shopProxyHandlerPresent != other.shopProxyHandlerPresent
                || this.shopProxyEventId != other.shopProxyEventId
                || this.proxyInstanceHandler != other.proxyInstanceHandler
                || this.matches.Count != other.matches.Count)
            {
                return false;
            }

            for (var i = 0; i < this.matches.Count; i++)
            {
                if (this.matches[i] != other.matches[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
