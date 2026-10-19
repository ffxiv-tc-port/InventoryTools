using System.Collections.Generic;
using System.Linq;
using AllaganLib.GameSheets.Caches;
using AllaganLib.GameSheets.Sheets;
using AllaganLib.GameSheets.Sheets.Rows;
using InventoryTools.Extensions;

namespace InventoryTools.Services;

public class IngredientPatchService(ItemSheet itemSheet)
{
    private Dictionary<uint, decimal>? _ingredientPatches;

    public Dictionary<uint, decimal> IngredientPatches => _ingredientPatches ??= CalculateHighestPatch();

    private Dictionary<uint, decimal> CalculateHighestPatch()
    {
        Dictionary<uint, decimal> ingredientPatches = new();
        Dictionary<uint, decimal?> highestBelow = new();
        foreach (var item in itemSheet)
        {
            if (!item.HasAnyUseOfType(ItemInfoType.CraftRecipe))
            {
                continue;
            }

            var highest = HighestPatchBelow(highestBelow, item);
            if (highest != null)
            {
                ingredientPatches[item.RowId] = highest.Value;
            }
        }
        return ingredientPatches;
    }

    /// <summary>
    /// Highest patch among everything crafted from <paramref name="item"/>, all the way down; null when nothing is.
    /// Memoised per item: many materials share the same sub-trees, which a fresh walk per starting item revisited
    /// once per path. Ties keep the first value met in recipe order, so the decimal's scale matches a full walk.
    /// </summary>
    private static decimal? HighestPatchBelow(Dictionary<uint, decimal?> highestBelow, ItemRow item)
    {
        if (highestBelow.TryGetValue(item.RowId, out var cached))
        {
            return cached;
        }

        decimal? highest = null;
        if (item.HasAnyUseOfType(ItemInfoType.CraftRecipe, ItemInfoType.FreeCompanyCraftRecipe))
        {
            foreach (var recipe in item.RecipesAsRequirement)
            {
                var result = recipe.ItemResult;
                if (result == null)
                {
                    continue;
                }

                if (highest == null || highest.Value < result.Patch)
                {
                    highest = result.Patch;
                }

                var below = HighestPatchBelow(highestBelow, result);
                if (below != null && highest.Value < below.Value)
                {
                    highest = below;
                }
            }
        }

        highestBelow[item.RowId] = highest;
        return highest;
    }
}