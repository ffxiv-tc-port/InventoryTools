using System;
using InventoryTools.Logic.Filters;

namespace InventoryTools.Extensions;

public static class FilterCategoryExtensions
{
    public static string FormattedName(this FilterCategory filterCategory)
    {
        return filterCategory switch
        {
            FilterCategory.SourceCategories => "Source (Categories)".Loc(),
            FilterCategory.UseCategories => "Use (Categories)".Loc(),
            _ => filterCategory.ToString().ToSentence().Loc()
        };
    }
}