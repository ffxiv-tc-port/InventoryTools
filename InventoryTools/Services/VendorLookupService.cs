using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Dalamud.Plugin.Services;
using InventoryTools.IPC;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.Logging;

namespace InventoryTools.Services;

/// <summary>
/// 「這個道具哪個 NPC 有賣、在哪裡」的一筆答案。
/// </summary>
public class VendorHint
{
    /// <summary>ENpcResident 的列號。</summary>
    public uint NpcId;

    /// <summary>商人名字。查不到名字時是空字串——呼叫端要顯示成「?」，不要顯示成空白。</summary>
    public string NpcName = string.Empty;

    /// <summary>商店名字；沒有就是空字串。只放進 tooltip。</summary>
    public string ShopName = string.Empty;

    /// <summary>TerritoryType 列號，直接餵給 Lifestream。</summary>
    public uint TerritoryTypeId;

    /// <summary>地名（區域的 PlaceName）。查不到時是空字串。</summary>
    public string PlaceName = string.Empty;

    /// <summary>地圖座標（玩家在地圖上看到的那個 X/Y），只拿來顯示。</summary>
    public float MapX;

    /// <summary>地圖座標，只拿來顯示。</summary>
    public float MapY;

    /// <summary>
    /// <see cref="MapX"/>／<see cref="MapY"/> 是不是真的算得出來。
    /// </summary>
    /// <remarks>🔴 <see langword="false"/> 時那兩個欄位是 0，<b>不要當座標印出來</b>。</remarks>
    public bool MapCoordinatesKnown;

    /// <summary>世界座標 X，餵給 Lifestream 用。</summary>
    public float WorldX;

    /// <summary>世界座標 Z，餵給 Lifestream 用。</summary>
    public float WorldZ;

    /// <summary>代價的摘要字串（例如「3 個 亞拉戈詩學神典石」）。沒有就是空字串。</summary>
    public string CostSummary = string.Empty;
}

/// <summary>
/// 「哪裡買」這一格現在是什麼狀態。
/// </summary>
/// <remarks>
/// 🔴 <b><see cref="Unknown"/> 與 <see cref="NoVendor"/> 一定要在畫面上分得出來。</b>
/// 前者是「我們問不到」（IVL 沒裝），後者是「問到了，沒有 NPC 賣」。
/// 把前者畫成後者等於騙使用者說這東西買不到。
/// ⚠️ 這個列舉刻意讓 <see cref="Unknown"/> ＝ 0，所以 <c>default</c> 落在「不知道」這個安全值上。
/// </remarks>
public enum VendorLookupState
{
    /// <summary>問不到（ItemVendorLocation 沒安裝／沒載入／回答失敗）。</summary>
    Unknown = 0,

    /// <summary>問到了：沒有帶位置的 NPC 商人。</summary>
    NoVendor = 1,

    /// <summary>問到了：有 NPC 可以買。</summary>
    Found = 2,
}

/// <summary>
/// 製作清單「缺料」列的「哪裡買」：向 ItemVendorLocation 問商人，向 Lifestream 要「前往」。
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>執行緒：只從主執行緒（framework／ImGui 繪製）呼叫。</b>底下的
/// <see cref="_cache"/> 是<b>裸 Dictionary</b>，這是刻意的——本服務沒有對外的 IPC 端點，
/// 唯一的呼叫路徑是製作清單的繪製，那條路徑整條都在同一個執行緒上。
/// ⚠️ <b>將來若要把這裡開成 IPC 端點或從背景 Task 呼叫，必須先換成有鎖的容器</b>，
/// 否則失敗形式不是「拿到舊值」而是字典本身壞掉。
/// </para>
/// <para>
/// 📌 <b>只在視窗開著的時候查。</b>本服務不註冊任何 tick／事件，
/// <see cref="Lookup"/> 只被繪製路徑呼叫，而繪製只在視窗開著時發生。
/// </para>
/// <para>
/// 📌 <b>快取。</b>查得到就永久留著：IVL 的資料表是它建構子裡一次建完的靜態表
/// （見 <see cref="ItemVendorLocationIpc"/> 的註解），不會隨遊戲狀態變。
/// 查不到（對方沒裝）則<b>不寫進快取</b>，只把重試時間往後推
/// <see cref="UnknownRetryInterval"/>，這樣使用者中途才裝上 IVL 也能自己恢復。
/// </para>
/// </remarks>
public class VendorLookupService
{
    /// <summary>對方不在時，隔多久再試一次（每次重試在對方沒裝時是一個例外，不能每幀來）。</summary>
    private static readonly TimeSpan UnknownRetryInterval = TimeSpan.FromSeconds(5);

    /// <summary>每個道具最多記幾個商人。挑選只看這幾個，留太多沒有意義。</summary>
    private const int MaxHintsPerItem = 8;

    private readonly ItemVendorLocationIpc _itemVendorLocationIpc;
    private readonly LifestreamIpc _lifestreamIpc;
    private readonly ExcelSheet<ENpcResident> _eNpcResidents;
    private readonly ExcelSheet<TerritoryType> _territoryTypes;
    private readonly IClientState _clientState;
    private readonly ILogger<VendorLookupService> _logger;

    /// <summary>道具 id → 商人清單（可能是空的＝問到了但沒有）。🔴 只在主執行緒上碰，見類別註解。</summary>
    private readonly Dictionary<uint, List<VendorHint>> _cache = new();

    private DateTime _retryUnknownAfter = DateTime.MinValue;

    public VendorLookupService(
        ILogger<VendorLookupService> logger,
        ItemVendorLocationIpc itemVendorLocationIpc,
        LifestreamIpc lifestreamIpc,
        ExcelSheet<ENpcResident> eNpcResidents,
        ExcelSheet<TerritoryType> territoryTypes,
        IClientState clientState)
    {
        _logger = logger;
        _itemVendorLocationIpc = itemVendorLocationIpc;
        _lifestreamIpc = lifestreamIpc;
        _eNpcResidents = eNpcResidents;
        _territoryTypes = territoryTypes;
        _clientState = clientState;
    }

    /// <summary>
    /// 查一個道具的 NPC 商人。<b>只能從主執行緒呼叫。</b>
    /// </summary>
    /// <param name="itemId">道具 id。</param>
    /// <returns>
    /// 狀態，以及（狀態為 <see cref="VendorLookupState.Found"/> 時）挑出來的那一筆。
    /// 挑選規則：優先挑<b>角色目前所在區域</b>的商人（省一次傳送），
    /// 其次挑有名字的，最後照 npcId 排序讓結果每次一樣。
    /// </returns>
    public (VendorLookupState State, VendorHint? Hint) Lookup(uint itemId)
    {
        if (itemId == 0)
        {
            return (VendorLookupState.NoVendor, null);
        }

        if (!_cache.TryGetValue(itemId, out var hints))
        {
            if (DateTime.UtcNow < _retryUnknownAfter)
            {
                return (VendorLookupState.Unknown, null);
            }

            // 先問給世界座標的新端點；對方是舊版才退回舊端點＋自己反解。
            if (_itemVendorLocationIpc.TryGetItemVendorsWorld(itemId, out var worldVendors))
            {
                hints = this.BuildHintsFromWorld(worldVendors);
            }
            else if (_itemVendorLocationIpc.TryGetItemVendorsByMapCoords(itemId, out var mapVendors))
            {
                hints = this.BuildHintsFromMapCoords(mapVendors);
            }
            else
            {
                _retryUnknownAfter = DateTime.UtcNow + UnknownRetryInterval;
                return (VendorLookupState.Unknown, null);
            }

            _cache[itemId] = hints;
        }

        if (hints.Count == 0)
        {
            return (VendorLookupState.NoVendor, null);
        }

        var currentTerritory = _clientState.TerritoryType;
        var best = hints
            .OrderBy(c => c.TerritoryTypeId == currentTerritory ? 0 : 1)
            .ThenBy(c => string.IsNullOrEmpty(c.NpcName) ? 1 : 0)
            .ThenBy(c => c.NpcId)
            .First();

        return (VendorLookupState.Found, best);
    }

    /// <summary>Lifestream 裝了、載入了，而且現在沒在忙。</summary>
    public bool IsTravelAvailable() => _lifestreamIpc.IsAvailable();

    /// <summary>
    /// 請 Lifestream 把角色帶到這個商人身邊。
    /// </summary>
    /// <param name="hint">要去的商人。</param>
    /// <param name="itemName">寫進記錄用的道具名，讓 log 看得出是為了哪個道具跑的。</param>
    /// <returns>對方有沒有真的排進佇列。</returns>
    public bool Travel(VendorHint hint, string itemName)
    {
        // 允許飛行：Lifestream 在不可飛的區域會自動退回地面路線，不會因此失敗。
        return _lifestreamIpc.TryGoToMapPoint(
            hint.TerritoryTypeId,
            hint.WorldX,
            hint.WorldZ,
            true,
            $"製作清單「前往」：{itemName} → {(string.IsNullOrEmpty(hint.NpcName) ? "?" : hint.NpcName)}");
    }

    /// <summary>新端點（世界座標）的結果 → <see cref="VendorHint"/>。</summary>
    private List<VendorHint> BuildHintsFromWorld(List<VendorLocationInfo> vendors)
    {
        var hints = new List<VendorHint>();
        foreach (var vendor in vendors.OrderBy(c => c.NpcId))
        {
            if (hints.Count >= MaxHintsPerItem)
            {
                break;
            }

            // 我們的整個賣點是「帶你過去」，沒有位置就沒有東西可以帶。
            if (!vendor.HasLocation || vendor.TerritoryTypeId == 0)
            {
                continue;
            }

            hints.Add(new VendorHint
            {
                NpcId = vendor.NpcId,
                NpcName = vendor.NpcName ?? string.Empty,
                ShopName = vendor.ShopName ?? string.Empty,
                TerritoryTypeId = vendor.TerritoryTypeId,
                PlaceName = this.GetPlaceName(vendor.TerritoryTypeId),
                MapX = vendor.MapX,
                MapY = vendor.MapY,
                MapCoordinatesKnown = vendor.MapCoordinatesKnown,
                WorldX = vendor.WorldX,
                WorldZ = vendor.WorldZ,
                CostSummary = FormatCosts(vendor.Costs),
            });
        }

        return this.LogAndReturn(hints, "world");
    }

    /// <summary>
    /// 舊端點（地圖座標）的結果 → <see cref="VendorHint"/>，座標自己反解成世界座標。
    /// </summary>
    private List<VendorHint> BuildHintsFromMapCoords(HashSet<(uint npcId, uint territory, (float x, float y))> vendors)
    {
        var hints = new List<VendorHint>();
        foreach (var vendor in vendors.OrderBy(c => c.npcId))
        {
            if (hints.Count >= MaxHintsPerItem)
            {
                break;
            }

            // territory 0 ＝ 對方明講「這個商人沒有位置」。我們已經用 filterNoLocation=true
            // 要求過了，這裡是第二道，因為那個旗標的語意是對方定義的，不是我們能保證的。
            if (vendor.territory == 0)
            {
                continue;
            }

            var territoryRow = _territoryTypes.GetRowOrDefault(vendor.territory);
            if (territoryRow == null)
            {
                continue;
            }

            var map = territoryRow.Value.Map.ValueNullable;
            if (map == null)
            {
                continue;
            }

            hints.Add(new VendorHint
            {
                NpcId = vendor.npcId,
                NpcName = _eNpcResidents.GetRowOrDefault(vendor.npcId)?.Singular.ExtractText() ?? string.Empty,
                TerritoryTypeId = vendor.territory,
                PlaceName = this.GetPlaceName(vendor.territory),
                MapX = vendor.Item3.x,
                MapY = vendor.Item3.y,
                MapCoordinatesKnown = true,
                WorldX = MapToWorld(vendor.Item3.x, map.Value.SizeFactor, map.Value.OffsetX),
                WorldZ = MapToWorld(vendor.Item3.y, map.Value.SizeFactor, map.Value.OffsetY),
            });
        }

        return this.LogAndReturn(hints, "legacy-map-coords");
    }

    private List<VendorHint> LogAndReturn(List<VendorHint> hints, string source)
    {
        if (hints.Count != 0)
        {
            _logger.LogDebug(
                "Vendor lookup ({Source}): {Count} located vendor(s), first is npc {NpcId} in territory {Territory}.",
                source,
                hints.Count,
                hints[0].NpcId,
                hints[0].TerritoryTypeId);
        }

        return hints;
    }

    private string GetPlaceName(uint territoryTypeId)
    {
        return _territoryTypes.GetRowOrDefault(territoryTypeId)?.PlaceName.ValueNullable?.Name.ExtractText()
               ?? string.Empty;
    }

    private static string FormatCosts(List<VendorCostInfo>? costs)
    {
        if (costs == null || costs.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var cost in costs)
        {
            if (string.IsNullOrEmpty(cost.CurrencyName))
            {
                continue;
            }

            if (builder.Length != 0)
            {
                builder.Append(" + ");
            }

            builder.Append(cost.Amount).Append(' ').Append(cost.CurrencyName);
        }

        return builder.ToString();
    }

    /// <summary>
    /// 地圖座標 → 世界座標（<b>ItemVendorLocation 舊端點那條公式的反函數</b>）。
    /// </summary>
    /// <remarks>
    /// 📌 只有走舊端點（<c>GetItemVendors</c>）時才會用到；新端點直接給世界座標。
    /// <para>
    /// 🔴 <b>刻意反解 IVL 自己用的那條，不是 Dalamud <c>MapUtil</c> 那條。</b>
    /// IVL 的 <c>NpcLocation.ToMapCoordinate</c> 是：
    /// <code>
    /// c   = scale / 100
    /// val = (world + offset) * c
    /// map = 41 / c * ((val + 1024) / 2048) + 1
    ///     = 41 * (world + offset) / 2048 + 2050 / scale + 1
    /// </code>
    /// 所以反過來是 <c>world = (map - 1 - 2050 / scale) * 2048 / 41 - offset</c>。
    /// </para>
    /// <para>
    /// ⚠️ Dalamud 的 <c>MapUtil.ConvertWorldCoordXZToMapCoord</c> 寫的是
    /// <c>0.02 * offset + 2048 / scale + 0.02 * world + 1</c>——<b>同一條式子的近似版</b>
    /// （0.02 vs 41/2048、2048 vs 2050）。拿它去反解 IVL 給的值，在大地圖上會偏掉約
    /// <b>2.45 個世界單位</b>（用台服 <c>Map</c> 表 319 種真實 scale/offset 組合離線量過，
    /// 腳本在 <c>tools/exd/mapcoord_roundtrip.py</c>）；反解本式的誤差是 0.00012。
    /// 失敗形式是「導航目標靜靜地落在偏掉的位置」，零錯誤訊息。
    /// </para>
    /// <para>
    /// 📌 <c>offset</c> 用的是 Lumina <c>Map.OffsetX</c>／<c>OffsetY</c> 的<b>原值不加負號</b>——
    /// IVL 傳進它自己的換算時就是原值。（<c>AgentMap.SelectedOffsetX</c> 是相反符號的那一套，
    /// 這裡沒有用到。）
    /// </para>
    /// <para>
    /// 📌 IVL 算 <c>MapX</c>／<c>MapY</c> 時<b>一律用 TerritoryType 預設地圖</b>的 scale/offset
    /// （即使那個 NPC 的 <c>MapId</c> 指向子地圖），所以這裡也用 <c>TerritoryType.Map</c>，兩邊一致。
    /// </para>
    /// </remarks>
    private static float MapToWorld(float mapCoordinate, ushort sizeFactor, short offset)
    {
        // scale 進到公式裡是分母，0 會算出無限大。100 是遊戲的預設縮放。
        float scale = sizeFactor == 0 ? 100f : sizeFactor;
        return ((mapCoordinate - 1f - (2050f / scale)) * (2048f / 41f)) - offset;
    }
}
