using CriticalCommonLib.MarketBoard;
using CriticalCommonLib.Services;

using InventoryTools.Logic.Columns.Abstract;
using InventoryTools.Logic.Columns.ColumnSettings;
using InventoryTools.Services;
using Microsoft.Extensions.Logging;

namespace InventoryTools.Logic.Columns
{
    public class MarketBoardMinPriceColumn : MarketBoardPriceColumn
    {
        private readonly ICharacterMonitor _characterMonitor;
        private readonly IMarketCache _marketCache;

        public MarketBoardMinPriceColumn(ILogger<MarketBoardMinPriceColumn> logger, ImGuiService imGuiService, MarketboardWorldSetting marketboardWorldSetting, ICharacterMonitor characterMonitor, IMarketCache marketCache) : base(logger, imGuiService, marketboardWorldSetting, characterMonitor, marketCache)
        {
            _characterMonitor = characterMonitor;
            _marketCache = marketCache;
        }
        public override ColumnCategory ColumnCategory => ColumnCategory.Market;

        public override string HelpText { get; set; } =
            "Shows the minimum price of both the NQ and HQ form of the item. If no world is selected, your home world is used. 此資料原本來自 universalis；台服無此服務，故此欄位無市場資料。";

        public override (int, int)? CurrentValue(ColumnConfiguration columnConfiguration, SearchResult searchResult)
        {
            var activeCharacter = _characterMonitor.ActiveCharacter;

            if (searchResult.InventoryItem != null)
            {
                if (!searchResult.InventoryItem.CanBeTraded)
                {
                    return (Untradable, Untradable);
                }

                if (activeCharacter != null)
                {
                    var selectedWorldId = MarketboardWorldSetting.SelectedWorldId(columnConfiguration, activeCharacter);

                    var marketBoardData = _marketCache.GetPricing(searchResult.InventoryItem.ItemId, selectedWorldId, false);
                    if (marketBoardData != null)
                    {
                        var nq = marketBoardData.MinPriceNq;
                        var hq = marketBoardData.MinPriceHq;
                        return ((int) nq, (int) hq);
                    }
                }

                // 台服沒有 universalis 資料來源:畫成 EmptyText(N/A),不要永遠停在 loading...
                return _marketCache.MarketDataAvailable ? (Loading, Loading) : null;
            }
            if (!searchResult.Item.CanBeTraded)
            {
                return (Untradable, Untradable);
            }
            if (activeCharacter != null)
            {
                var selectedWorldId = MarketboardWorldSetting.SelectedWorldId(columnConfiguration, activeCharacter);

                var marketBoardData = _marketCache.GetPricing(searchResult.Item.RowId, selectedWorldId, false);
                if (marketBoardData != null)
                {
                    var nq = marketBoardData.MinPriceNq;
                    var hq = marketBoardData.MinPriceHq;
                    return ((int)nq, (int)hq);
                }
            }

            // 台服沒有 universalis 資料來源:畫成 EmptyText(N/A),不要永遠停在 loading...
            return _marketCache.MarketDataAvailable ? (Loading, Loading) : null;
        }
        public override string Name { get; set; } = "Market Board Minimum Price NQ/HQ".Loc();
        public override string RenderName => "MB Min. Price NQ/HQ".Loc();

        public override FilterType DefaultIn => Logic.FilterType.CraftFilter;
    }
}