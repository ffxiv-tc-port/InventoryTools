using System.Collections.Generic;
using System.Numerics;
using CriticalCommonLib.MarketBoard;
using CriticalCommonLib.Models;
using CriticalCommonLib.Services;
using CriticalCommonLib.Services.Mediator;
using DalaMock.Host.Mediator;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;
using InventoryTools.Logic.Columns.Abstract;
using InventoryTools.Logic.Columns.ColumnSettings;
using InventoryTools.Services;
using InventoryTools.Ui.Widgets;
using Microsoft.Extensions.Logging;

namespace InventoryTools.Logic.Columns
{
    public class MarketBoardPriceColumn : DoubleGilColumn
    {
        private readonly IMarketCache _marketCache;
        private readonly ICharacterMonitor _characterMonitor;

        public MarketBoardPriceColumn(ILogger<MarketBoardPriceColumn> logger, ImGuiService imGuiService, MarketboardWorldSetting marketboardWorldSetting, ICharacterMonitor characterMonitor, IMarketCache marketCache) : base(logger, imGuiService)
        {
            _marketCache = marketCache;
            _characterMonitor = characterMonitor;
            MarketboardWorldSetting = marketboardWorldSetting;
        }
        public override ColumnCategory ColumnCategory => ColumnCategory.Market;
        protected readonly string LoadingString = "loading...";
        protected readonly string UntradableString = "untradable";
        protected readonly int Loading = -1;
        protected readonly int Untradable = -2;
        public MarketboardWorldSetting MarketboardWorldSetting { get; }

        public override bool IsConfigurable => true;

        public override List<MessageBase>? DrawEditor(ColumnConfiguration columnConfiguration,
            FilterConfiguration configuration)
        {
            ImGui.NewLine();
            ImGui.Separator();
            MarketboardWorldSetting.Draw(columnConfiguration, null);
            return null;
        }
        public override List<MessageBase>? Draw(FilterConfiguration configuration,
            ColumnConfiguration columnConfiguration,
            SearchResult searchResult, int rowIndex, int columnIndex)
        {
            return DoDraw(searchResult, CurrentValue(columnConfiguration, searchResult), rowIndex, configuration, columnConfiguration);
        }

        public override List<MessageBase>? DoDraw(SearchResult searchResult, (int, int)? currentValue, int rowIndex,
            FilterConfiguration filterConfiguration, ColumnConfiguration columnConfiguration)
        {
            if (currentValue.HasValue && currentValue.Value.Item1 == Loading)
            {
                ImGui.TableNextColumn();
                if (ImGui.TableGetColumnFlags().HasFlag(ImGuiTableColumnFlags.IsEnabled))
                {
                    ImGuiUtil.VerticalAlignTextColored(LoadingString, ImGuiColors.DalamudYellow,
                        filterConfiguration.TableHeight, false);
                }
            }
            else if (currentValue.HasValue && currentValue.Value.Item1 == Untradable)
            {
                ImGui.TableNextColumn();
                if (ImGui.TableGetColumnFlags().HasFlag(ImGuiTableColumnFlags.IsEnabled))
                {
                    ImGuiUtil.VerticalAlignTextColored(UntradableString, ImGuiColors.DalamudRed,
                        filterConfiguration.TableHeight, false);
                }
            }
            else if(currentValue.HasValue)
            {
                base.DoDraw(searchResult, currentValue, rowIndex, filterConfiguration, columnConfiguration);
            }
            else
            {
                base.DoDraw(searchResult, currentValue, rowIndex, filterConfiguration, columnConfiguration);
            }

            if (ImGui.TableGetColumnFlags().HasFlag(ImGuiTableColumnFlags.IsEnabled))
            {
                var activeCharacter = _characterMonitor.ActiveCharacter;
                if (activeCharacter != null)
                {
                    ImGui.SameLine();
                    ImGui.Image(ImGuiService.GetIconTexture(Icons.MarketboardIcon).Handle, new Vector2(16, 16));
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                    {
                        using (var tooltip = ImRaii.Tooltip())
                        {
                            if (tooltip.Success)
                            {
                                var selectedWorldId =
                                    MarketboardWorldSetting.SelectedWorldId(columnConfiguration, activeCharacter);
                                var pricing = _marketCache.GetPricing(searchResult.Item.RowId, selectedWorldId, false);
                                // 查不到資料時 pricing 會是 null,不能讓 tooltip 開出來卻是空的。
                                if (!_marketCache.MarketDataAvailable || pricing is { recentHistory: null, listings: null })
                                {
                                    ImGui.Text("No data available".Loc());
                                }

                                if (pricing is { listings: not null })
                                {
                                    ImGui.Text("Listings: ".Loc());
                                    ImGui.Separator();

                                    foreach (var price in pricing.listings)
                                    {
                                        ImGui.Text(price.quantity + " available at " + price.pricePerUnit +
                                                   (price.hq ? " (HQ)" : ""));
                                    }
                                }

                                if (pricing is { recentHistory: not null })
                                {
                                    ImGui.Text("History: ".Loc());
                                    ImGui.Separator();

                                    foreach (var price in pricing.recentHistory)
                                    {
                                        ImGui.Text(price.quantity + " available at " + price.pricePerUnit +
                                                   (price.hq ? " (HQ)" : ""));
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return null;
        }

        public override (int, int)? CurrentValue(ColumnConfiguration columnConfiguration, SearchResult searchResult)
        {
            if (searchResult.InventoryItem is {CanBeTraded: false})
            {
                return (Untradable, Untradable);
            }
            if (!searchResult.Item.CanBeTraded)
            {
                return (Untradable, Untradable);
            }

            // 試過的世界全部查不到 universalis 資料時:回 null 讓欄位畫成 EmptyText(N/A),
            // 而不是永遠停在「loading...」,也不要畫成會誤導人的 0。
            if (!_marketCache.MarketDataAvailable)
            {
                return null;
            }

            var activeCharacter = _characterMonitor.ActiveCharacter;
            if (activeCharacter != null)
            {
                var selectedWorldId = MarketboardWorldSetting.SelectedWorldId(columnConfiguration, activeCharacter);
                var marketBoardData = _marketCache.GetPricing(searchResult.Item.RowId, selectedWorldId, false);
                if (marketBoardData != null)
                {
                    var nq = marketBoardData.AveragePriceNq;
                    var hq = marketBoardData.AveragePriceHq;
                    return ((int)nq, (int)hq);
                }
            }

            return (Loading, Loading);
        }

        public override string Name { get; set; } = "Market Board Average Price NQ/HQ".Loc();
        public override string RenderName => "MB Avg. Price NQ/HQ".Loc();
        public override string HelpText { get; set; } =
            "Shows the average price of both the NQ and HQ form of the item. If no world is selected, your home world is used. 此資料來自 universalis 的社群上傳，涵蓋台服 8 個世界；沒有人上傳過的道具會顯示為無資料。";
        public override float Width { get; set; } = 200.0f;
        public override bool HasFilter { get; set; } = true;
        public override ColumnFilterType FilterType { get; set; } = ColumnFilterType.Text;
    }
}