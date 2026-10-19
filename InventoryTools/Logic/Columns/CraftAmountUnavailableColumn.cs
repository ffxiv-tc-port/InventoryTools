using System;
using System.Collections.Generic;
using CriticalCommonLib.Services.Mediator;
using DalaMock.Host.Mediator;
using Dalamud.Interface.Colors;
using Dalamud.Bindings.ImGui;
using InventoryTools.Logic.Columns.Abstract;
using InventoryTools.Services;
using Microsoft.Extensions.Logging;

namespace InventoryTools.Logic.Columns
{
    public class CraftAmountUnavailableColumn : IntegerColumn
    {
        public CraftAmountUnavailableColumn(ILogger<CraftAmountUnavailableColumn> logger, ImGuiService imGuiService) : base(logger, imGuiService)
        {
        }
        public override ColumnCategory ColumnCategory => ColumnCategory.Crafting;

        public override int? CurrentValue(ColumnConfiguration columnConfiguration, SearchResult searchResult)
        {
            if (searchResult.CraftItem == null) return 0;
            return Math.Max(0, (int)searchResult.CraftItem.QuantityMissingOverall);
        }

        public override List<MessageBase>? Draw(FilterConfiguration configuration,
            ColumnConfiguration columnConfiguration,
            SearchResult searchResult, int rowIndex, int columnIndex)
        {
            if (searchResult.CraftItem == null) return null;

            // 背景重新整理可能在推與彈之間改掉數量；PopStyleColor 必須照推入當下的判定。
            var highlight = CurrentValue(columnConfiguration, searchResult) > 0;
            if (highlight)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
            }

            base.Draw(configuration, columnConfiguration, searchResult, rowIndex, columnIndex);
            if (highlight)
            {
                ImGui.PopStyleColor();
            }
            return null;
        }
        public override FilterType AvailableIn { get; } = Logic.FilterType.CraftFilter;
        public override string Name { get; set; } = "Amount Missing".Loc();
        public override string RenderName => "Missing".Loc();
        public override float Width { get; set; } = 60;
        public override bool? CraftOnly => true;
        public override string HelpText { get; set; } =
            "This is the amount that needs to be sourced from MB/gathering excluding potential items to be withdrawn from retainers.";
        public override bool HasFilter { get; set; } = false;
        public override ColumnFilterType FilterType { get; set; } = ColumnFilterType.Text;
    }
}