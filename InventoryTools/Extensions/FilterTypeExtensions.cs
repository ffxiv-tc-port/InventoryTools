using InventoryTools.Logic;

namespace InventoryTools.Extensions;

public static class FilterTypeExtensions
{
    public static string FormattedName(this FilterType filterType)
    {
        return filterType switch
        {
            FilterType.None => "None".Loc(),
            FilterType.SearchFilter => "Search List".Loc(),
            FilterType.SortingFilter => "Sort List".Loc(),
            FilterType.GameItemFilter => "Game Item List".Loc(),
            FilterType.CraftFilter => "Craft List".Loc(),
            FilterType.HistoryFilter => "History List".Loc(),
            FilterType.CuratedList => "Curated List".Loc(),
            _ => "Unknown"
        };
    }
}