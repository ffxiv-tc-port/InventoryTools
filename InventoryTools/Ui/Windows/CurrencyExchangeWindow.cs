using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using AllaganLib.GameSheets.Caches;
using AllaganLib.GameSheets.ItemSources;
using AllaganLib.GameSheets.Model;
using AllaganLib.GameSheets.Sheets;
using AllaganLib.GameSheets.Sheets.Rows;
using Autofac;
using CriticalCommonLib.Enums;
using CriticalCommonLib.MarketBoard;
using CriticalCommonLib.Services;
using CriticalCommonLib.Services.Mediator;
using DalaMock.Host.Mediator;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using InventoryTools.Logic;
using InventoryTools.Logic.CurrencyExchange;
using InventoryTools.Mediator;
using InventoryTools.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.Logging;

namespace InventoryTools.Ui;

// 本檔假設與契約：
// - 兌換資料來自 ItemInfoCache 的特殊商店／危命商店／軍票商店來源；貨幣解碼由 AllaganLib 負責。
// - 哪些算貨幣以 TrackedCurrencies 為準（與 Currency 容器的掃描同一份）；持有量只算角色篩選選到的角色。
// - 只讀市價快取；只有按「查價」才經 IMarketCache.RequestCheck 排進既有的節流佇列（只送物品 ID 與世界）。
// - 全部在框架執行緒的 Draw 內執行；不跨幀保存任何原生指標。
public class CurrencyExchangeWindow : GenericWindow, IMenuWindow
{
    private const double RecomputeSeconds = 0.5;
    private const double HoldingsRefreshSeconds = 2.0;
    private static readonly TimeSpan PendingTimeout = TimeSpan.FromMinutes(3);

    private readonly ItemInfoCache _itemInfoCache;
    private readonly ItemSheet _itemSheet;
    private readonly IMarketCache _marketCache;
    private readonly UniversalisAvailability _universalisAvailability;
    private readonly ICharacterMonitor _characterMonitor;
    private readonly IInventoryMonitor _inventoryMonitor;
    private readonly IComponentContext _context;
    private readonly ExcelSheet<GrandCompany> _grandCompanySheet;
    private readonly ExcelSheet<GrandCompanyRank> _grandCompanyRankSheet;
    private readonly ExcelSheet<World> _worldSheet;

    private Dictionary<uint, List<Offer>>? _offersByCurrency;
    private readonly Dictionary<uint, Holding> _holdings = new();
    private readonly Dictionary<(uint ItemId, uint WorldId), DateTime> _requestedAt = new();
    private List<uint> _currencyList = new();
    private readonly Dictionary<uint, string> _currencyLabels = new();
    private List<Row> _rows = new();
    private uint _selectedCurrency;
    private bool _showAllCurrencies;
    private bool _hideUnknown;
    private string _search = "";
    private int _sortColumn = ColValue;
    private bool _sortDescending = true;
    private volatile bool _dirty = true;
    private DateTime _lastRecompute = DateTime.MinValue;
    private DateTime _lastHoldings = DateTime.MinValue;
    private uint _rowsWorldId;
    private long _rowsHeld = -1;
    private int _uncheckedCount;
    private int? _pendingSortColumn;
    private string _summaryText = "";
    private string _summaryText2 = "";
    private string _summaryTooltip = "";
    private bool _summaryMuted = true;
    private KeyValuePair<ulong, CriticalCommonLib.Models.Character>[] _playerCharacters = [];

    private const int ColItem = 0;
    private const int ColCost = 1;
    private const int ColOther = 2;
    private const int ColValue = 3;
    private const int ColRobust = 4;
    private const int ColPrice = 5;
    private const int ColSales = 6;
    private const int ColTime = 7;
    private const int ColShop = 8;
    private const int ColumnCount = 9;

    public CurrencyExchangeWindow(
        ILogger<CurrencyExchangeWindow> logger,
        MediatorService mediator,
        ImGuiService imGuiService,
        InventoryToolsConfiguration configuration,
        ItemInfoCache itemInfoCache,
        ItemSheet itemSheet,
        IMarketCache marketCache,
        UniversalisAvailability universalisAvailability,
        ICharacterMonitor characterMonitor,
        IInventoryMonitor inventoryMonitor,
        IComponentContext context,
        ExcelSheet<GrandCompany> grandCompanySheet,
        ExcelSheet<GrandCompanyRank> grandCompanyRankSheet,
        ExcelSheet<World> worldSheet,
        string name = "Currency Exchange Value")
        : base(logger, mediator, imGuiService, configuration, name)
    {
        _itemInfoCache = itemInfoCache;
        _itemSheet = itemSheet;
        _marketCache = marketCache;
        _universalisAvailability = universalisAvailability;
        _characterMonitor = characterMonitor;
        _inventoryMonitor = inventoryMonitor;
        _context = context;
        _grandCompanySheet = grandCompanySheet;
        _grandCompanyRankSheet = grandCompanyRankSheet;
        _worldSheet = worldSheet;
    }

    public override void Initialize()
    {
        WindowName = "Currency Exchange Value".Loc() + "##currencyExchange";
        Key = GenericKey;
        MediatorService.Subscribe<MarketCacheUpdatedMessage>(this, _ => _dirty = true);
    }

    public override string GenericKey => "currencyexchange";
    public override string GenericName => "Currency Exchange Value".Loc();
    public override bool DestroyOnClose => false;
    public override bool SaveState => true;
    public override Vector2? DefaultSize { get; } = new(1000, 600);
    public override Vector2? MaxSize { get; } = new(3000, 3000);
    public override Vector2? MinSize { get; } = new(500, 300);
    public override FilterConfiguration? SelectedConfiguration => null;

    public override void PreDraw()
    {
        base.PreDraw();
        // 基底建構子設的是未依字級換算的值；Window 在 PreDraw 之後才套用，所以每幀在這裡換算。
        var factor = UiScale.WindowSizeFactor();
        SizeConstraints = new WindowSizeConstraints { MinimumSize = MinSize!.Value * factor, MaximumSize = MaxSize!.Value };
        Size = DefaultSize!.Value * factor;
    }

    public override void Invalidate()
    {
        _dirty = true;
    }

    public override void Draw()
    {
        _offersByCurrency ??= BuildOffers();
        var now = DateTime.Now;
        if ((now - _lastHoldings).TotalSeconds >= HoldingsRefreshSeconds)
        {
            _lastHoldings = now;
            RefreshHoldings();
        }

        var worldId = _characterMonitor.ActiveCharacter?.WorldId ?? 0;
        if (worldId != _rowsWorldId)
        {
            _rowsWorldId = worldId;
            _dirty = true;
        }

        if (HeldFor(_selectedCurrency).Amount != _rowsHeld)
        {
            _dirty = true;
        }

        if (_dirty && (now - _lastRecompute).TotalSeconds >= RecomputeSeconds)
        {
            _dirty = false;
            _lastRecompute = now;
            RecomputeRows(now);
        }

        using (var left = ImRaii.Child("currencyList", new Vector2(UiScale.Px(230), 0), true))
        {
            if (left.Success)
            {
                DrawCurrencyList();
            }
        }

        ImGui.SameLine();
        using (var right = ImRaii.Child("exchangeTable", new Vector2(0, 0), false))
        {
            if (right.Success)
            {
                DrawHeader(worldId);
                DrawTable(now);
            }
        }
    }

    private void DrawCurrencyList()
    {
        DrawCharacterFilter();
        if (ImGui.Checkbox("Show all currencies".Loc(), ref _showAllCurrencies))
        {
            RebuildCurrencyList();
        }

        HoverText("Off: only currencies one of your characters is holding, fullest first.".Loc());
        ImGui.Separator();

        if (_currencyList.Count == 0)
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, "No currency held (log in once to record holdings).".Loc());
        }

        foreach (var currencyId in _currencyList)
        {
            var item = _itemSheet.GetRowOrDefault(currencyId);
            if (item == null)
            {
                continue;
            }

            _holdings.TryGetValue(currencyId, out var holding);
            var fill = holding?.MaxFill;
            var noOffers = !HasOffers(currencyId);
            ImGui.Image(ImGuiService.GetIconTexture(item.Icon).Handle, UiScale.Px(18, 18));
            ImGui.SameLine();
            using (var color = ImRaii.PushColor(ImGuiCol.Text, noOffers ? ImGuiColors.DalamudGrey : ImGuiColors.DalamudOrange, noOffers || fill >= 0.9))
            {
                if (ImGui.Selectable(_currencyLabels.GetValueOrDefault(currencyId, "?"), _selectedCurrency == currencyId))
                {
                    _selectedCurrency = currencyId;
                    _dirty = true;
                    _lastRecompute = DateTime.MinValue;
                }
            }

            if (ImGui.IsItemHovered())
            {
                DrawHoldingTooltip(item, holding, noOffers);
            }
        }
    }

    private static void DrawHoldingTooltip(ItemRow item, Holding? holding, bool noOffers)
    {
        using var tooltip = ImRaii.Tooltip();
        if (!tooltip.Success)
        {
            return;
        }

        ImGui.TextUnformatted(item.NameString);
        if (noOffers)
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, "No shop exchanges this currency, so there is nothing to rank.".Loc());
        }

        if (holding == null || holding.PerCharacter.Count + holding.Unknown.Count == 0)
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, "None of the selected characters holds this.".Loc());
            return;
        }

        foreach (var (name, amount, cap) in holding.PerCharacter)
        {
            var capText = cap is { } c ? c.ToString("N0", CultureInfo.InvariantCulture) : "?";
            ImGui.TextUnformatted($"{name}: {amount.ToString("N0", CultureInfo.InvariantCulture)} / {capText}");
        }

        foreach (var name in holding.Unknown)
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, $"{name}: ?");
        }

        if (holding.Unknown.Count > 0)
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, "?: this currency started being tracked after the character's last login; log in once to record it.".Loc());
        }

        ImGui.Separator();
        ImGui.TextColored(ImGuiColors.DalamudGrey, "Other characters show the amount from their last login.".Loc());
        if (IsGrandCompanySeal(item.RowId))
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, "Seal caps depend on rank; only the logged-in character's cap is known.".Loc());
        }
    }

    private void DrawHeader(uint worldId)
    {
        var currency = _itemSheet.GetRowOrDefault(_selectedCurrency);
        if (currency == null)
        {
            ImGui.TextUnformatted("Pick a currency on the left.".Loc());
            return;
        }

        ImGui.TextUnformatted(currency.NameString);
        ImGui.SameLine();
        var worldName = _worldSheet.GetRowOrDefault(worldId)?.Name.ExtractText() ?? "?";
        ImGui.TextColored(ImGuiColors.DalamudGrey, "Prices: ??".Loc(worldName));
        ImGui.SameLine();
        ImGui.TextColored(ImGuiColors.DalamudGrey, "(?)");
        HoverText("Value per 1 currency = (market price of what you get x quantity - other things you also pay) / currency spent.\nMarket price is the lower of the recent average and the current cheapest listing on your home world, before market tax.\nSales/day counts sale transactions over the market history window in settings.\nGrey rows are older than the market refresh time in settings.\n\"Cannot value\" means the reward cannot be sold on the market, or something else you must also pay cannot be bought there.".Loc());

        var canQuery = _universalisAvailability.IsWorldSupported(worldId) && _uncheckedCount > 0;
        using (ImRaii.Disabled(!canQuery))
        {
            if (ImGui.Button("Check prices (?? items)".Loc(_uncheckedCount)))
            {
                RequestPrices(worldId);
            }
        }

        HoverText(canQuery
            ? "Asks universalis for the listed items through the normal market price queue. Only item IDs and your home world are sent.".Loc()
            : "Nothing to check: every item here is already priced and fresh, cannot be sold on the market, or your home world has no universalis data.".Loc());

        ImGui.SameLine();
        ImGui.SetNextItemWidth(UiScale.Px(180));
        if (ImGui.InputTextWithHint("##currencySearch", "Search items".Loc(), ref _search, 100))
        {
            _dirty = true;
            _lastRecompute = DateTime.MinValue;
        }

        ImGui.SameLine();
        if (ImGui.Checkbox("Hide cannot value".Loc(), ref _hideUnknown))
        {
            _dirty = true;
            _lastRecompute = DateTime.MinValue;
        }

        HoverText("Hides rows whose reward cannot be sold on the market or that also cost something unbuyable. Rows that are only waiting for prices stay.".Loc());

        // 模式就是「依哪一欄排序」：點表頭換排序時，選項跟著反映。
        if (ImGui.RadioButton("Net value first".Loc(), _sortColumn == ColValue))
        {
            _pendingSortColumn = ColValue;
        }

        ImGui.SameLine();
        if (ImGui.RadioButton("Sells out first".Loc(), _sortColumn == ColRobust))
        {
            _pendingSortColumn = ColRobust;
        }

        ImGui.SameLine();
        ImGui.TextColored(ImGuiColors.DalamudGrey, "(?)");
        HoverText("Net value first: rank by Gil per 1.\nSells out first: rank by robust value = Gil per 1 x min(1, sales/day / units you can exchange). Units you can exchange = how many times your holding covers the cost x items per exchange (counted as one exchange if you cannot afford any).\nRows whose sales/day is unknown are not ranked by robust value; they go after the rows that are and show \"Sales ?\".\nNegative values are not discounted.".Loc());

        ImGui.TextColored(_summaryMuted ? ImGuiColors.DalamudGrey : ImGuiColors.DalamudWhite, _summaryText);
        if (_summaryTooltip.Length != 0)
        {
            HoverText(_summaryTooltip);
        }

        if (_summaryText2.Length != 0)
        {
            ImGui.TextColored(_summaryMuted ? ImGuiColors.DalamudGrey : ImGuiColors.DalamudWhite, _summaryText2);
            if (_summaryTooltip.Length != 0)
            {
                HoverText(_summaryTooltip);
            }
        }
    }

    private void DrawCharacterFilter()
    {
        var excluded = Configuration.CurrencyExchangeExcludedCharacters;
        var all = _playerCharacters;
        var selected = all.Count(c => CurrencyExchangeMath.IsCharacterSelected(c.Key, excluded));
        if (ImGui.Button("Characters: ??/??".Loc(selected, all.Length) + "##currencyCharacters", new Vector2(-1, 0)))
        {
            ImGui.OpenPopup("currencyCharactersPopup");
        }

        HoverText("Only the checked characters count toward holdings, fill level and the \"all into #1\" totals. Saved; new characters are included automatically.".Loc());

        using var popup = ImRaii.Popup("currencyCharactersPopup");
        if (!popup.Success)
        {
            return;
        }

        if (ImGui.Button("Select all".Loc()))
        {
            SetExcludedCharacters(new HashSet<ulong>());
        }

        ImGui.SameLine();
        var activeId = _characterMonitor.ActiveCharacterId;
        var activeKnown = activeId != 0 && all.Any(c => c.Key == activeId);
        using (ImRaii.Disabled(!activeKnown))
        {
            if (ImGui.Button("Only current character".Loc()))
            {
                SetExcludedCharacters(CurrencyExchangeMath.ExcludeAllBut(all.Select(c => c.Key), activeId));
            }
        }

        if (!activeKnown)
        {
            HoverText("Not logged in.".Loc());
        }

        ImGui.Separator();
        using var list = ImRaii.Child("currencyCharactersList", new Vector2(UiScale.Px(260), UiScale.Px(Math.Min(all.Length, 12) * 24 + 8)), false);
        if (!list.Success)
        {
            return;
        }

        foreach (var (id, character) in all.OrderBy(c => c.Value.Name, StringComparer.CurrentCulture))
        {
            var on = CurrencyExchangeMath.IsCharacterSelected(id, excluded);
            var world = _worldSheet.GetRowOrDefault(character.WorldId)?.Name.ExtractText() ?? "?";
            if (ImGui.Checkbox($"{character.Name} ({world})##cc{id}", ref on))
            {
                var next = new HashSet<ulong>(excluded);
                if (on)
                {
                    next.Remove(id);
                }
                else
                {
                    next.Add(id);
                }

                SetExcludedCharacters(next);
            }
        }
    }

    private void SetExcludedCharacters(HashSet<ulong> excluded)
    {
        Configuration.CurrencyExchangeExcludedCharacters = excluded;
        RefreshHoldings();
        _dirty = true;
        _lastRecompute = DateTime.MinValue;
    }

    private void DrawTable(DateTime now)
    {
        if (_selectedCurrency == 0)
        {
            return;
        }

        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable |
                                      ImGuiTableFlags.Sortable | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY |
                                      ImGuiTableFlags.Hideable | ImGuiTableFlags.SizingFixedFit;
        // 表格 ID 隨欄數改：ImGui 依欄位索引套用存下的欄寬／隱藏，插入新欄後沿用舊 ID 會套錯欄。
        using var table = ImRaii.Table("currencyExchangeTable9", ColumnCount, flags);
        if (!table.Success)
        {
            return;
        }

        ImGui.TableSetupScrollFreeze(1, 1);
        ImGui.TableSetupColumn("Item".Loc(), ImGuiTableColumnFlags.WidthFixed, UiScale.Px(240));
        ImGui.TableSetupColumn("Cost".Loc(), ImGuiTableColumnFlags.WidthFixed, UiScale.Px(60));
        ImGui.TableSetupColumn("Also pay".Loc(), ImGuiTableColumnFlags.WidthFixed, UiScale.Px(140));
        ImGui.TableSetupColumn("Gil per 1".Loc(), ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.DefaultSort | ImGuiTableColumnFlags.PreferSortDescending, UiScale.Px(90));
        ImGui.TableSetupColumn("Robust per 1".Loc(), ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.PreferSortDescending, UiScale.Px(90));
        ImGui.TableSetupColumn("Market price".Loc(), ImGuiTableColumnFlags.WidthFixed, UiScale.Px(90));
        ImGui.TableSetupColumn("Sales/day".Loc(), ImGuiTableColumnFlags.WidthFixed, UiScale.Px(70));
        ImGui.TableSetupColumn("Data age".Loc(), ImGuiTableColumnFlags.WidthFixed, UiScale.Px(80));
        ImGui.TableSetupColumn("Shop".Loc(), ImGuiTableColumnFlags.WidthFixed, UiScale.Px(200));
        ImGui.TableHeadersRow();

        if (_pendingSortColumn is { } pending)
        {
            _pendingSortColumn = null;
            ImGuiP.TableSetColumnSortDirection(pending, ImGuiSortDirection.Descending, false);
        }

        var specs = ImGui.TableGetSortSpecs();
        if (specs.SpecsDirty)
        {
            _sortColumn = specs.Specs.ColumnIndex;
            _sortDescending = specs.Specs.SortDirection == ImGuiSortDirection.Descending;
            specs.SpecsDirty = false;
            SortRows();
        }

        var rows = _rows;
        var staleHours = Configuration.MarketRefreshTimeHours;
        var clipper = ImGui.ImGuiListClipper();
        try
        {
            clipper.Begin(rows.Count);
            while (clipper.Step())
            {
                for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                {
                    if (i < 0 || i >= rows.Count)
                    {
                        continue;
                    }

                    DrawRow(rows[i], i, now, staleHours);
                }
            }
        }
        finally
        {
            clipper.End();
            clipper.Destroy();
        }
    }

    private void DrawRow(Row row, int index, DateTime now, int staleHours)
    {
        using var id = ImRaii.PushId(index);
        ImGui.TableNextRow();
        var stale = row.Valuation.OldestUpdate != null && CurrencyExchangeMath.IsStale(row.Valuation.OldestUpdate, now, staleHours);

        ImGui.TableNextColumn();
        var reward = row.Offer.FirstReward;
        ImGui.Image(ImGuiService.GetIconTexture(reward.Icon).Handle, UiScale.Px(18, 18));
        ImGui.SameLine();
        ImGui.TextUnformatted(row.Offer.RewardText);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None) && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            MediatorService.Publish(new OpenUintWindowMessage(typeof(ItemWindow), reward.RowId));
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None) && ImGui.IsMouseReleased(ImGuiMouseButton.Right))
        {
            ImGui.OpenPopup("rc");
        }

        using (var popup = ImRaii.Popup("rc"))
        {
            if (popup.Success)
            {
                MediatorService.Publish(ImGuiService.ImGuiMenuService.DrawRightClickPopup(reward));
            }
        }

        ImGui.TableNextColumn();
        ImGui.TextUnformatted(row.Valuation.CurrencyCount.ToString("N0", CultureInfo.InvariantCulture));

        ImGui.TableNextColumn();
        if (row.OtherCostText.Length == 0)
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, "-");
        }
        else
        {
            ImGui.TextUnformatted(row.OtherCostText);
            HoverText(row.OtherCostText);
        }

        ImGui.TableNextColumn();
        if (row.Valuation.ValuePerCurrency is { } value)
        {
            var color = value < 0 ? ImGuiColors.DalamudRed : stale ? ImGuiColors.DalamudGrey : ImGuiColors.DalamudWhite;
            ImGui.TextColored(color, FormatGil(value));
            HoverText(row.ValueTooltip);
        }
        else
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, StateLabel(row.Valuation.State));
            HoverText(row.ValueTooltip);
        }

        ImGui.TableNextColumn();
        if (row.Robust is { } robust)
        {
            var color = robust < 0 ? ImGuiColors.DalamudRed : stale ? ImGuiColors.DalamudGrey : ImGuiColors.DalamudWhite;
            ImGui.TextColored(color, FormatGil(robust));
            if (ImGui.IsItemHovered())
            {
                HoverText(RobustTooltip(row));
            }
        }
        else if (row.Valuation.ValuePerCurrency.HasValue)
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, "Sales ?".Loc());
            HoverText("Sales/day unknown, so this row is not ranked by robust value and goes after the rows that are.".Loc());
        }
        else
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, StateLabel(row.Valuation.State));
            HoverText(row.ValueTooltip);
        }

        ImGui.TableNextColumn();
        if (row.RewardUnitPrice is { } price)
        {
            ImGui.TextColored(stale ? ImGuiColors.DalamudGrey : ImGuiColors.DalamudWhite, FormatGil(price));
        }
        else
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, "?");
        }

        ImGui.TableNextColumn();
        if (row.Valuation.DailySales is { } sales)
        {
            ImGui.TextColored(stale ? ImGuiColors.DalamudGrey : ImGuiColors.DalamudWhite, sales.ToString(sales < 10 ? "0.0" : "N0", CultureInfo.InvariantCulture));
        }
        else
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, "?");
            HoverText("No sale history recorded for this item, or it has not been fetched.".Loc());
        }

        ImGui.TableNextColumn();
        if (row.Valuation.OldestUpdate is { } updated)
        {
            ImGui.TextColored(stale ? ImGuiColors.DalamudGrey : ImGuiColors.DalamudWhite, FormatAge(now - updated));
            if (stale)
            {
                HoverText("Older than the market refresh time in settings; press Check prices to refresh.".Loc());
            }
        }
        else
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, "?");
        }

        ImGui.TableNextColumn();
        ImGui.TextUnformatted(row.Offer.ShopText);
        if (row.Offer.ShopNames.Count > 1)
        {
            HoverText(string.Join("\n", row.Offer.ShopNames));
        }
    }

    private void RecomputeRows(DateTime now)
    {
        RebuildCurrencyList();
        _uncheckedCount = 0;
        var held = HeldFor(_selectedCurrency).Amount;
        _rowsHeld = held;
        if (_offersByCurrency == null || !_offersByCurrency.TryGetValue(_selectedCurrency, out var offers))
        {
            _rows = new List<Row>();
            BuildSummary();
            return;
        }

        var worldId = _rowsWorldId;
        var historyDays = Configuration.MarketSaleHistoryLimit;
        var staleHours = Configuration.MarketRefreshTimeHours;
        var toCheck = new HashSet<uint>();
        var rows = new List<Row>(offers.Count);
        foreach (var offer in offers)
        {
            if (_search.Length != 0 && !offer.RewardText.Contains(_search, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var valuation = CurrencyExchangeMath.Evaluate(offer.Costs, offer.Rewards, _selectedCurrency, part =>
            {
                var q = Quote(part, worldId, historyDays, now);
                if (q.State == QuoteState.NotChecked || (q.State == QuoteState.Ok && CurrencyExchangeMath.IsStale(q.UpdatedAt, now, staleHours)))
                {
                    toCheck.Add(part.ItemId);
                }

                return q;
            });

            if (_hideUnknown && valuation.State is ExchangeValueState.Untradable or ExchangeValueState.CostUntradable)
            {
                continue;
            }

            var firstQuote = Quote(offer.Rewards[0], worldId, historyDays, now);
            var otherCost = string.Join("、", offer.Costs.Where(c => c.ItemId != _selectedCurrency).Select(PartText));
            var unitsPerExchange = offer.Rewards.Max(r => r.Count);
            var units = CurrencyExchangeMath.ExchangeableUnits(held, valuation.CurrencyCount, unitsPerExchange);
            var robust = CurrencyExchangeMath.RobustValue(valuation.ValuePerCurrency, valuation.DailySales, units);
            rows.Add(new Row(offer, valuation, firstQuote.State == QuoteState.Ok ? firstQuote.UnitPrice : null, otherCost, BuildValueTooltip(valuation, otherCost), robust, units, unitsPerExchange));
        }

        _uncheckedCount = toCheck.Count;
        _rows = rows;
        SortRows();
    }

    private void SortRows()
    {
        var desc = _sortDescending;
        Comparison<Row> cmp = _sortColumn switch
        {
            ColItem => (a, b) => Dir(string.Compare(a.Offer.RewardText, b.Offer.RewardText, StringComparison.CurrentCulture), desc),
            ColCost => (a, b) => Dir(a.Valuation.CurrencyCount.CompareTo(b.Valuation.CurrencyCount), desc),
            ColOther => (a, b) => Dir(string.Compare(a.OtherCostText, b.OtherCostText, StringComparison.CurrentCulture), desc),
            ColRobust => (a, b) => CurrencyExchangeMath.CompareByRobust(a.Valuation, a.Robust, b.Valuation, b.Robust, desc),
            ColPrice => (a, b) => CurrencyExchangeMath.CompareNullableLast(a.RewardUnitPrice, b.RewardUnitPrice, desc),
            ColSales => (a, b) => CurrencyExchangeMath.CompareNullableLast(a.Valuation.DailySales, b.Valuation.DailySales, desc),
            ColTime => (a, b) => CurrencyExchangeMath.CompareNullableLast(Ticks(a), Ticks(b), desc),
            ColShop => (a, b) => Dir(string.Compare(a.Offer.ShopText, b.Offer.ShopText, StringComparison.CurrentCulture), desc),
            _ => (a, b) => CurrencyExchangeMath.CompareByValue(a.Valuation, b.Valuation, desc),
        };

        // List.Sort 不穩定；用原始順序當最後一層比較，避免同值列每次重算都跳動。
        var indexed = _rows.Select((r, i) => (r, i)).ToList();
        indexed.Sort((x, y) =>
        {
            var c = cmp(x.r, y.r);
            return c != 0 ? c : x.i.CompareTo(y.i);
        });
        _rows = indexed.Select(x => x.r).ToList();
        BuildSummary();

        static int Dir(int c, bool d) => d ? -c : c;
        static double? Ticks(Row r) => r.Valuation.OldestUpdate?.Ticks;
    }

    private MarketQuote Quote(ExchangePart part, uint worldId, int historyDays, DateTime now)
    {
        var item = _itemSheet.GetRowOrDefault(part.ItemId);
        if (item == null || !item.CanBeTraded || !item.CanBePlacedOnMarket)
        {
            return new MarketQuote(QuoteState.NotMarketable, 0, null, null);
        }

        if (!_marketCache.CachedPricing.TryGetValue((part.ItemId, worldId), out var pricing))
        {
            if (_requestedAt.TryGetValue((part.ItemId, worldId), out var at))
            {
                return now - at < PendingTimeout
                    ? new MarketQuote(QuoteState.Pending, 0, null, null)
                    : new MarketQuote(QuoteState.NoData, 0, null, null);
            }

            return new MarketQuote(QuoteState.NotChecked, 0, null, null);
        }

        var unit = part.IsHq
            ? CurrencyExchangeMath.ConservativeUnitPrice(pricing.AveragePriceHq, pricing.MinPriceHq)
            : CurrencyExchangeMath.ConservativeUnitPrice(pricing.AveragePriceNq, pricing.MinPriceNq);
        var daily = CurrencyExchangeMath.DailySales(pricing.SevenDaySellCount, pricing.LastSellDate, historyDays);
        if (unit == null)
        {
            return new MarketQuote(QuoteState.NoData, 0, daily, pricing.LastUpdate);
        }

        return new MarketQuote(QuoteState.Ok, unit.Value, daily, pricing.LastUpdate);
    }

    private void RequestPrices(uint worldId)
    {
        if (_offersByCurrency == null || !_offersByCurrency.TryGetValue(_selectedCurrency, out var offers))
        {
            return;
        }

        var now = DateTime.Now;
        var staleHours = Configuration.MarketRefreshTimeHours;
        var sent = new HashSet<uint>();
        var queued = 0;
        foreach (var offer in offers)
        {
            foreach (var part in offer.Rewards.Concat(offer.Costs))
            {
                if (part.ItemId == _selectedCurrency || part.ItemId == CurrencyExchangeMath.GilItemId || !sent.Add(part.ItemId))
                {
                    continue;
                }

                var item = _itemSheet.GetRowOrDefault(part.ItemId);
                if (item == null || !item.CanBeTraded || !item.CanBePlacedOnMarket)
                {
                    continue;
                }

                var cached = _marketCache.CachedPricing.TryGetValue((part.ItemId, worldId), out var pricing);
                var stale = cached && CurrencyExchangeMath.IsStale(pricing!.LastUpdate, now, staleHours);
                if (cached && !stale)
                {
                    continue;
                }

                if (_marketCache.RequestCheck(part.ItemId, worldId, stale))
                {
                    queued++;
                    if (!cached)
                    {
                        _requestedAt[(part.ItemId, worldId)] = now;
                    }
                }
            }
        }

        Logger.LogInformation("貨幣兌換價值排行：貨幣 {Currency} 排入查價 {Count} 件（世界 {WorldId}）。", _selectedCurrency, queued, worldId);
        _dirty = true;
    }

    private void RefreshHoldings()
    {
        _holdings.Clear();
        var activeId = _characterMonitor.ActiveCharacterId;
        _playerCharacters = _characterMonitor.GetPlayerCharacters();
        PruneDeletedCharacters();
        var excluded = Configuration.CurrencyExchangeExcludedCharacters;
        var scanVersions = Configuration.CurrencyScanVersionByCharacter;
        foreach (var (characterId, character) in _playerCharacters)
        {
            if (!CurrencyExchangeMath.IsCharacterSelected(characterId, excluded))
            {
                continue;
            }

            if (!CurrencyExchangeMath.IsAmountKnown(true, scanVersions.TryGetValue(characterId, out var v) ? v : null, TrackedCurrencies.ExtraSetVersion))
            {
                foreach (var extraId in TrackedCurrencies.ExtraItemIds)
                {
                    if (_itemSheet.GetRowOrDefault(extraId) != null)
                    {
                        GetOrAddHolding(extraId).Unknown.Add(character.Name);
                    }
                }
            }

            foreach (var item in _inventoryMonitor.GetSpecificInventory(characterId, InventoryType.Currency))
            {
                if (item.Quantity == 0 || item.ItemId == CurrencyExchangeMath.GilItemId)
                {
                    continue;
                }

                var holding = GetOrAddHolding(item.ItemId);
                var cap = CapFor(item.ItemId, characterId == activeId);
                holding.PerCharacter.Add((character.Name, item.Quantity, cap));
                holding.Unknown.Remove(character.Name);
                if (characterId == activeId)
                {
                    holding.ActiveIndex = holding.PerCharacter.Count - 1;
                }
                var fill = CurrencyExchangeMath.FillRatio(item.Quantity, cap);
                if (fill is { } f && (holding.MaxFill == null || f > holding.MaxFill))
                {
                    holding.MaxFill = f;
                }
            }
        }

        RebuildCurrencyList();
    }

    private Holding GetOrAddHolding(uint itemId)
    {
        if (!_holdings.TryGetValue(itemId, out var holding))
        {
            holding = new Holding();
            _holdings[itemId] = holding;
        }

        return holding;
    }

    // 刪掉的角色不留在設定裡，兩份清單才不會一直長大；角色清單是空的（還沒載入）時不動。
    private void PruneDeletedCharacters()
    {
        var existing = _playerCharacters.Select(c => c.Key).ToHashSet();
        if (CurrencyExchangeMath.PruneCharacters(Configuration.CurrencyExchangeExcludedCharacters, existing) is { } excluded)
        {
            Configuration.CurrencyExchangeExcludedCharacters = excluded;
        }

        if (CurrencyExchangeMath.PruneCharacters(Configuration.CurrencyScanVersionByCharacter, existing) is { } versions)
        {
            Configuration.CurrencyScanVersionByCharacter = versions;
        }
    }

    private bool HasOffers(uint currencyId) => _offersByCurrency != null && _offersByCurrency.ContainsKey(currencyId);

    private long? CapFor(uint itemId, bool isActiveCharacter)
    {
        if (!IsGrandCompanySeal(itemId))
        {
            var stack = _itemSheet.GetRowOrDefault(itemId)?.Base.StackSize ?? 0;
            return stack > 0 ? stack : null;
        }

        // 軍票上限看軍階，而軍階只讀得到目前登入的角色。
        if (!isActiveCharacter || !_context.TryResolve<IPlayerState>(out var playerState) || !playerState.IsLoaded)
        {
            return null;
        }

        var grandCompany = _grandCompanySheet.GetRowOrDefault(itemId - 19);
        if (grandCompany == null)
        {
            return null;
        }

        var rank = playerState.GetGrandCompanyRank(grandCompany.Value);
        var maxSeals = rank == 0 ? 0u : _grandCompanyRankSheet.GetRowOrDefault(rank)?.MaxSeals ?? 0u;
        return maxSeals > 0 ? maxSeals : null;
    }

    private void RebuildCurrencyList()
    {
        if (_offersByCurrency == null)
        {
            return;
        }

        // 沒有商店兌換的貨幣（抽獎、升級用）也列出來看持有量，灰字排在最後。
        IEnumerable<uint> ids = _showAllCurrencies
            ? _offersByCurrency.Keys.Concat(_holdings.Keys).Concat(TrackedCurrencies.ExtraItemIds.Where(c => _itemSheet.GetRowOrDefault(c) != null))
            : _offersByCurrency.Keys.Where(c => _holdings.ContainsKey(c)).Concat(_holdings.Keys);
        _currencyList = ids
            .Distinct()
            .OrderBy(c => HasOffers(c) ? 0 : 1)
            .ThenByDescending(c => _holdings.TryGetValue(c, out var h) ? h.MaxFill ?? -0.5 : -1)
            .ThenBy(c => _itemSheet.GetRowOrDefault(c)?.NameString ?? "", StringComparer.CurrentCulture)
            .ToList();
        _currencyLabels.Clear();
        foreach (var c in _currencyList)
        {
            var fill = _holdings.TryGetValue(c, out var h) ? h.MaxFill : null;
            var fillText = fill is { } f ? (f * 100).ToString("0", CultureInfo.InvariantCulture) + "%" : "?";
            if (fill != null && h!.Unknown.Count > 0)
            {
                fillText += " ?";
            }

            _currencyLabels[c] = (_itemSheet.GetRowOrDefault(c)?.NameString ?? "?") + "  " + fillText + "##cur" + c;
        }

        if (_selectedCurrency == 0 && _currencyList.Count > 0)
        {
            _selectedCurrency = _currencyList[0];
            _dirty = true;
        }
    }

    private Dictionary<uint, List<Offer>> BuildOffers()
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var bySignature = new Dictionary<string, Offer>();
        void Add(object listingKey, IReadOnlyList<ExchangePart> costs, IReadOnlyList<ExchangePart> rewards, string shopName)
        {
            if (!seen.Add(listingKey) || costs.Count == 0 || rewards.Count == 0)
            {
                return;
            }

            if (shopName == "Unknown Vendor")
            {
                shopName = "Unknown".Loc();
            }

            var signature = string.Join(",", costs.Select(c => $"{c.ItemId}x{c.Count}{(c.IsHq ? "h" : "")}")) + ">" +
                            string.Join(",", rewards.Select(r => $"{r.ItemId}x{r.Count}{(r.IsHq ? "h" : "")}"));
            if (bySignature.TryGetValue(signature, out var existing))
            {
                if (!existing.ShopNames.Contains(shopName))
                {
                    existing.ShopNames.Add(shopName);
                }

                return;
            }

            var first = _itemSheet.GetRowOrDefault(rewards[0].ItemId);
            if (first == null || first.Icon == 0)
            {
                return;
            }

            bySignature[signature] = new Offer(costs, rewards, first, new List<string> { shopName }, this);
        }

        foreach (var source in _itemInfoCache.GetItemSourcesByType(ItemInfoType.SpecialShop).OfType<ItemSpecialShopSource>())
        {
            Add(source.ShopListing, Parts(source.ShopListing.Costs), Parts(source.ShopListing.Rewards), source.Shop.Name);
        }

        foreach (var source in _itemInfoCache.GetItemSourcesByType(ItemInfoType.FateShop).OfType<ItemFateShopSource>())
        {
            Add(source.ShopListing, Parts(source.ShopListing.Costs), Parts(source.ShopListing.Rewards), source.Shop.Name);
        }

        // 軍票商店一次只給 1 個，成本直接取軍票價。
        foreach (var source in _itemInfoCache.GetItemSourcesByType(ItemInfoType.GCShop).OfType<ItemGCShopSource>())
        {
            if (source.CostItem == null)
            {
                continue;
            }

            var costs = new[] { new ExchangePart(source.CostItem.RowId, source.GCScripShopItem.Base.CostGCSeals, false) };
            var rewards = new[] { new ExchangePart(source.Item.RowId, 1, false) };
            Add(source.GCScripShopItem, costs, rewards, source.Shop.Name);
        }

        var result = new Dictionary<uint, List<Offer>>();
        foreach (var offer in bySignature.Values)
        {
            foreach (var cost in offer.Costs.Select(c => c.ItemId).Distinct())
            {
                var costItem = _itemSheet.GetRowOrDefault(cost);
                if (costItem == null || !IsCurrencyItem(costItem))
                {
                    continue;
                }

                if (!result.TryGetValue(cost, out var list))
                {
                    list = new List<Offer>();
                    result[cost] = list;
                }

                list.Add(offer);
            }
        }

        foreach (var offer in bySignature.Values)
        {
            offer.ShopNames.Sort(StringComparer.CurrentCulture);
        }

        Logger.LogInformation("貨幣兌換價值排行：整理出 {Offers} 筆兌換、{Currencies} 種貨幣。", bySignature.Count, result.Count);
        return result;

        static ExchangePart[] Parts(IEnumerable<IShopListingItem> items) =>
            items.Where(i => i.Item.RowId != 0).Select(i => new ExchangePart(i.Item.RowId, Math.Max(1u, i.Count), i.IsHq == true)).ToArray();
    }

    private static bool IsCurrencyItem(ItemRow item) => TrackedCurrencies.IsTracked(item);

    private static bool IsGrandCompanySeal(uint itemId) => itemId is 20 or 21 or 22;

    private string BuildValueTooltip(ExchangeValuation v, string otherCost)
    {
        var currencyName = _itemSheet.GetRowOrDefault(_selectedCurrency)?.NameString ?? "?";
        return v.State switch
        {
            ExchangeValueState.Ok => "Rewards ?? - also pay ?? = ?? Gil for ?? ??".Loc(
                FormatGil(v.RewardValue ?? 0), FormatGil(v.OtherCost ?? 0),
                FormatGil((v.RewardValue ?? 0) - (v.OtherCost ?? 0)), v.CurrencyCount, currencyName),
            ExchangeValueState.Untradable => "Cannot value: the reward cannot be sold on the market.".Loc(),
            ExchangeValueState.CostUntradable => "Cannot value: you also have to pay something that cannot be bought on the market (??).".Loc(otherCost),
            ExchangeValueState.NotChecked => "No price yet. Press Check prices.".Loc(),
            ExchangeValueState.Pending => "Waiting for universalis.".Loc(),
            ExchangeValueState.NoData => "universalis has no listing or sale for this item on your home world.".Loc(),
            _ => "?",
        };
    }

    private static string StateLabel(ExchangeValueState state) => state switch
    {
        ExchangeValueState.Untradable or ExchangeValueState.CostUntradable => "Cannot value".Loc(),
        ExchangeValueState.NotChecked => "Not checked".Loc(),
        ExchangeValueState.Pending => "Checking...".Loc(),
        ExchangeValueState.NoData => "No data".Loc(),
        _ => "?",
    };

    private static string FormatGil(double value)
    {
        return Math.Abs(value) < 100
            ? value.ToString("0.0", CultureInfo.InvariantCulture)
            : value.ToString("N0", CultureInfo.InvariantCulture);
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalMinutes < 1)
        {
            return "just now".Loc();
        }

        if (age.TotalHours < 1)
        {
            return "?? min ago".Loc((int)age.TotalMinutes);
        }

        return age.TotalDays < 1 ? "?? h ago".Loc((int)age.TotalHours) : "?? d ago".Loc((int)age.TotalDays);
    }

    private static void HoverText(string text)
    {
        if (!ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            return;
        }

        using var tooltip = ImRaii.Tooltip();
        if (tooltip.Success)
        {
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 30);
            ImGui.TextUnformatted(text);
            ImGui.PopTextWrapPos();
        }
    }

    /// <summary>
    /// 摘要與穩健值用的「手上這批」：只看篩選選到的角色；目前登入角色有紀錄就用它，否則取其中最多的那位（數量是上次登入時的）。
    /// 沒有任何角色持有回 (0, null)。
    /// </summary>
    private (long Amount, string? Name) HeldFor(uint currencyId)
    {
        if (!_holdings.TryGetValue(currencyId, out var holding) || holding.PerCharacter.Count == 0)
        {
            return (0, null);
        }

        if (holding.ActiveIndex is { } active)
        {
            var a = holding.PerCharacter[active];
            return (a.Amount, a.Name);
        }

        var best = holding.PerCharacter[0];
        foreach (var entry in holding.PerCharacter)
        {
            if (entry.Amount > best.Amount)
            {
                best = entry;
            }
        }

        return (best.Amount, best.Name);
    }

    private void BuildSummary()
    {
        _summaryMuted = true;
        _summaryTooltip = "";
        _summaryText2 = "";
        _holdings.TryGetValue(_selectedCurrency, out var holding);
        var unknown = holding?.Unknown.Count ?? 0;
        var unknownNote = unknown > 0 ? "?? selected characters: amount unknown (?)".Loc(unknown) : "";
        var (held, holder) = HeldFor(_selectedCurrency);
        if (holder == null)
        {
            _summaryText = unknown > 0 ? unknownNote : "None of the selected characters holds this.".Loc();
            return;
        }

        if (!HasOffers(_selectedCurrency))
        {
            var sum = CurrencyExchangeMath.MultiCharacterExchange(holding!.PerCharacter.Select(p => p.Amount), 0).Held;
            _summaryText = "Selected characters hold ?? in total. No shop exchanges this currency, so there is nothing to rank.".Loc(
                sum.ToString("N0", CultureInfo.InvariantCulture));
            _summaryText2 = unknownNote;
            return;
        }

        var robustMode = _sortColumn == ColRobust;
        Row? top = null;
        if (robustMode)
        {
            foreach (var r in _rows)
            {
                if (r.Robust is { } v && (top == null || v > top.Robust!.Value))
                {
                    top = r;
                }
            }
        }

        var fellBack = robustMode && top == null;
        if (top == null)
        {
            foreach (var r in _rows)
            {
                if (r.Valuation.ValuePerCurrency is { } v && (top == null || v > top.Valuation.ValuePerCurrency!.Value))
                {
                    top = r;
                }
            }
        }

        if (top == null)
        {
            _summaryText = "No row can be valued yet; press Check prices.".Loc();
            _summaryText2 = unknownNote;
            return;
        }

        var heldText = held.ToString("N0", CultureInfo.InvariantCulture);
        var v0 = top.Valuation;
        var times = CurrencyExchangeMath.ExchangeTimes(held, v0.CurrencyCount);
        if (times == 0)
        {
            _summaryText = "Holding ?? (??): not enough for one #1 ?? (needs ??)".Loc(heldText, holder, top.Offer.RewardText,
                v0.CurrencyCount.ToString("N0", CultureInfo.InvariantCulture));
        }
        else
        {
            var total = times * ((v0.RewardValue ?? 0) - (v0.OtherCost ?? 0));
            _summaryText = "Holding ?? (??): all into #1 ?? = ?? exchanges, about ?? Gil".Loc(heldText, holder, top.Offer.RewardText,
                times.ToString("N0", CultureInfo.InvariantCulture), FormatGil(total));
            _summaryMuted = false;
        }

        if (holding!.PerCharacter.Count >= 2)
        {
            var (sumHeld, sumTimes) = CurrencyExchangeMath.MultiCharacterExchange(holding.PerCharacter.Select(p => p.Amount), v0.CurrencyCount);
            _summaryText2 = "?? selected characters hold ?? in total: each exchanging all into #1 = ?? exchanges, about ?? Gil".Loc(
                holding.PerCharacter.Count, sumHeld.ToString("N0", CultureInfo.InvariantCulture),
                sumTimes.ToString("N0", CultureInfo.InvariantCulture), FormatGil(sumTimes * ((v0.RewardValue ?? 0) - (v0.OtherCost ?? 0))));
            if (unknownNote.Length != 0)
            {
                _summaryText2 += "   " + unknownNote;
            }
        }
        else
        {
            _summaryText2 = unknownNote;
        }

        var lines = new List<string>
        {
            "Ranked by: ??".Loc(robustMode ? "Sells out first".Loc() : "Net value first".Loc()),
        };
        if (fellBack)
        {
            lines.Add("Sells out first has no row with known sales yet, so #1 is by net value.".Loc());
        }

        var items = Math.Max(1, times) * (double)top.UnitsPerExchange;
        if (v0.DailySales is { } sales && sales > 0)
        {
            lines.Add("At ?? sales/day, selling ?? items takes about ?? days.".Loc(
                sales.ToString(sales < 10 ? "0.0" : "N0", CultureInfo.InvariantCulture),
                items.ToString("N0", CultureInfo.InvariantCulture),
                Math.Ceiling(items / sales).ToString("N0", CultureInfo.InvariantCulture)));
        }
        else
        {
            lines.Add("Sales/day unknown or zero: cannot tell how long it takes to sell.".Loc());
        }

        lines.Add("Before market tax; other things you also pay are already subtracted. Other characters' amounts are from their last login.".Loc());
        lines.Add("Currencies cannot be pooled between characters, so the total counts each character's exchanges separately. Only the characters checked under Characters are counted.".Loc());
        _summaryTooltip = string.Join("\n", lines);
    }

    private static string RobustTooltip(Row row)
    {
        var v = row.Valuation.ValuePerCurrency ?? 0;
        if (v < 0)
        {
            return "Negative value is not discounted.".Loc();
        }

        var sales = row.Valuation.DailySales ?? 0;
        return "Gil per 1 ?? x min(1, sales/day ?? / ?? units) = ??".Loc(FormatGil(v),
            sales.ToString(sales < 10 ? "0.0" : "N0", CultureInfo.InvariantCulture),
            row.Units.ToString("N0", CultureInfo.InvariantCulture), FormatGil(row.Robust ?? 0));
    }

    private sealed class Holding
    {
        public readonly List<(string Name, long Amount, long? Cap)> PerCharacter = new();
        public readonly List<string> Unknown = new();
        public double? MaxFill;
        public int? ActiveIndex;
    }

    private sealed record Row(Offer Offer, ExchangeValuation Valuation, double? RewardUnitPrice, string OtherCostText, string ValueTooltip,
        double? Robust, double Units, uint UnitsPerExchange);

    private sealed class Offer
    {
        public Offer(IReadOnlyList<ExchangePart> costs, IReadOnlyList<ExchangePart> rewards, ItemRow firstReward, List<string> shopNames, CurrencyExchangeWindow window)
        {
            Costs = costs;
            Rewards = rewards;
            FirstReward = firstReward;
            ShopNames = shopNames;
            RewardText = string.Join(" + ", rewards.Select(r => window.PartText(r)));
        }

        private string? _shopText;
        public IReadOnlyList<ExchangePart> Costs { get; }
        public IReadOnlyList<ExchangePart> Rewards { get; }
        public ItemRow FirstReward { get; }
        public List<string> ShopNames { get; }
        public string RewardText { get; }

        // 店名清單在 BuildOffers 收完才定，所以第一次取用時才組字串。
        public string ShopText => _shopText ??= ShopNames.Count <= 1
            ? ShopNames.FirstOrDefault() ?? ""
            : "?? (+?? more)".Loc(ShopNames[0], ShopNames.Count - 1);
    }

    private string PartText(ExchangePart part)
    {
        var name = _itemSheet.GetRowOrDefault(part.ItemId)?.NameString ?? "?";
        var hq = part.IsHq ? " (HQ)" : "";
        return part.Count > 1 ? $"{name}{hq} x{part.Count.ToString("N0", CultureInfo.InvariantCulture)}" : name + hq;
    }
}
