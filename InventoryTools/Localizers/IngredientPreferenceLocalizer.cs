using AllaganLib.GameSheets.Sheets;
using CriticalCommonLib.Crafting;
using CriticalCommonLib.Extensions;
using CriticalCommonLib.Models;

namespace InventoryTools.Localizers;

public class IngredientPreferenceLocalizer
{
    private readonly ItemSheet _itemSheet;
    private readonly CraftTypeSheet _craftTypeSheet;

    public IngredientPreferenceLocalizer(ItemSheet itemSheet, CraftTypeSheet craftTypeSheet)
    {
        _itemSheet = itemSheet;
        _craftTypeSheet = craftTypeSheet;
    }

    // AllaganLib's ItemSheet.GetRow does NOT return null for a missing row: it fabricates an
    // empty ItemRow, caches it, and hands it back. The null-conditional therefore never short
    // circuits, and ItemRow.NameString goes on to read ItemRow.Base, which is a plain Lumina
    // ExcelSheet<Item>.GetRow and throws ArgumentOutOfRangeException when the row is absent.
    // IngredientPreference item ids survive a round trip through the saved list configuration
    // and can also arrive from an imported list string, so they are not guaranteed to exist on
    // this client (TC ships Item rows 0..49200). Throwing here would take out the whole ImGui
    // draw path. GetRowOrDefault does the HasRow check and really can return null; it also
    // avoids poisoning the sheet's row cache, which is a SingleInstance shared with every other
    // GetRowOrDefault caller (e.g. CraftSettingsColumn) for the lifetime of the plugin.
    private string ItemName(uint itemId)
    {
        return _itemSheet.GetRowOrDefault(itemId)?.NameString
               ?? ("Unknown Item".Loc() + " (#" + itemId + ")");
    }

    public string FormattedName(IngredientPreference ingredientPreference)
    {
        switch (ingredientPreference.Type)
        {
            case IngredientPreferenceType.Item:
                if (ingredientPreference.LinkedItemId != null && ingredientPreference.LinkedItemQuantity != null)
                {
                    string? itemName2 = null;
                    string? itemName3 = null;
                    if (ingredientPreference.LinkedItem2Id != null && ingredientPreference.LinkedItem2Quantity != null)
                    {
                        if (ingredientPreference.LinkedItem3Id != null &&
                            ingredientPreference.LinkedItem3Quantity != null)
                        {
                            itemName3 = ItemName(ingredientPreference.LinkedItem3Id.Value) + " - " +
                                        ingredientPreference.LinkedItem3Quantity.Value;
                        }

                        itemName2 = ItemName(ingredientPreference.LinkedItem2Id.Value) + " - " +
                                    ingredientPreference.LinkedItem2Quantity.Value;
                    }

                    var itemName = ItemName(ingredientPreference.LinkedItemId.Value);
                    if (itemName3 != null)
                    {
                        itemName = itemName + "," + itemName2 + "," + itemName3;
                    }
                    else if (itemName2 != null)
                    {
                        itemName = itemName + "," + itemName2;
                    }

                    return itemName + " - " + ingredientPreference.LinkedItemQuantity.Value;
                }

                return "No item selected".Loc();
            case IngredientPreferenceType.Reduction:
                if (ingredientPreference.LinkedItemId != null && ingredientPreference.LinkedItemQuantity != null)
                {
                    var itemName = ItemName(ingredientPreference.LinkedItemId.Value);
                    return "Reduction (".Loc() + itemName + " - " + ingredientPreference.LinkedItemQuantity.Value + ")";
                }

                return "No item selected".Loc();
            case IngredientPreferenceType.Desynthesis:
                if (ingredientPreference.LinkedItemId != null && ingredientPreference.LinkedItemQuantity != null)
                {
                    var itemName = ItemName(ingredientPreference.LinkedItemId.Value);
                    return "Desynthesis (".Loc() + itemName + " - " + ingredientPreference.LinkedItemQuantity.Value + ")";
                }

                return "No item selected".Loc();
        }

        return ingredientPreference.Type.FormattedName().Loc();
    }

    public int? SourceIcon(IngredientPreference ingredientPreference)
    {
        return ingredientPreference.Type switch
        {
            IngredientPreferenceType.Buy => Icons.BuyIcon,
            IngredientPreferenceType.HouseVendor => Icons.BuyIcon,
            IngredientPreferenceType.Botany => Icons.BotanyIcon,
            IngredientPreferenceType.Crafting => _craftTypeSheet
                .GetRow(ingredientPreference.RecipeCraftTypeId ?? 0)?.Icon ?? Icons.CraftIcon,
            IngredientPreferenceType.Desynthesis => Icons.DesynthesisIcon,
            IngredientPreferenceType.Fishing => Icons.FishingIcon,
            IngredientPreferenceType.SpearFishing => Icons.Spearfishing,
            IngredientPreferenceType.Item => ingredientPreference.LinkedItemId != null
                ? _itemSheet.GetRowOrDefault(ingredientPreference.LinkedItemId.Value)?.Icon ??
                  Icons.SpecialItemIcon
                : Icons.SpecialItemIcon,
            IngredientPreferenceType.Marketboard => Icons.MarketboardIcon,
            IngredientPreferenceType.Mining => Icons.MiningIcon,
            IngredientPreferenceType.Mobs => Icons.MobIcon,
            IngredientPreferenceType.None => null,
            IngredientPreferenceType.Reduction => Icons.ReductionIcon,
            IngredientPreferenceType.Venture => Icons.VentureIcon,
            IngredientPreferenceType.Duty => Icons.DutyIcon,
            IngredientPreferenceType.ExplorationVenture => Icons.VentureIcon,
            IngredientPreferenceType.Empty => Icons.RedXIcon,
            IngredientPreferenceType.ResourceInspection => Icons.SkybuildersScripIcon,
            IngredientPreferenceType.Gardening => Icons.SproutIcon,
            _ => Icons.QuestionMarkIcon
        };
    }
}