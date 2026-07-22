using InventoryTools.Localizers;
using InventoryTools.Logic.Columns.Abstract;
using InventoryTools.Services;
using Microsoft.Extensions.Logging;

namespace InventoryTools.Logic.Columns
{
    public class LocationColumn : TextColumn
    {
        private readonly ItemLocalizer _itemLocalizer;

        public LocationColumn(ILogger<LocationColumn> logger, ImGuiService imGuiService, ItemLocalizer itemLocalizer) : base(logger, imGuiService)
        {
            _itemLocalizer = itemLocalizer;
        }
        public override ColumnCategory ColumnCategory => ColumnCategory.Inventory;

        public override FilterType AvailableIn => Logic.FilterType.SearchFilter | Logic.FilterType.SortingFilter | Logic.FilterType.CraftFilter;

        public override string? CurrentValue(ColumnConfiguration columnConfiguration, SearchResult searchResult)
        {
            if (searchResult.InventoryItem != null)
            {
                return _itemLocalizer.FormattedBagLocation(searchResult.InventoryItem);
            }

            return null;
        }
        public override string Name { get; set; } = "Inventory Location".Loc();
        public override string RenderName => "Location".Loc();
        public override float Width { get; set; } = 100.0f;
        public override string HelpText { get; set; } = "Shows the location of the item in your inventory.".Loc();
        public override bool HasFilter { get; set; } = true;
        public override ColumnFilterType FilterType { get; set; } = ColumnFilterType.Text;
        public override FilterType DefaultIn => Logic.FilterType.SearchFilter | Logic.FilterType.SortingFilter | Logic.FilterType.CraftFilter | Logic.FilterType.HistoryFilter;
    }
}