using CriticalCommonLib.Extensions;
using CriticalCommonLib.Models;

namespace InventoryTools.Extensions;

public static class InventoryCategoryExtensions
{
    /// <summary>
    /// The display name of an inventory category, localized, and named so that a retainer category
    /// cannot be mistaken for the character one.
    /// </summary>
    /// <remarks>
    /// CriticalCommonLib's <c>FormattedName</c> returns the bare "Bags"/"Equipped" for both the
    /// character and the retainer categories, which is ambiguous the moment a retainer shares a
    /// name with a character - and retainer names are player-chosen, so that is common. The two
    /// retainer-only enum values are given their own names here; every other category either has a
    /// name of its own already (retainer market, free company bags, houses) or is a single enum
    /// value genuinely shared between owners.
    ///
    /// This lives in InventoryTools rather than in CriticalCommonLib because <c>.Loc()</c> does:
    /// CriticalCommonLib has no access to the translation ini, so a name produced there can never
    /// be translated.
    ///
    /// 🔴 Known residual: <see cref="InventoryCategory.Currency"/> and
    /// <see cref="InventoryCategory.Crystals"/> are ONE enum value each, used for both a character's
    /// and a retainer's, so they cannot be disambiguated from the category alone - only the
    /// underlying InventoryType can tell those apart (see ItemLocalizer.SortedContainerName, which
    /// does). Callers that group by category and need the distinction have to fall back to the
    /// owner type.
    /// </remarks>
    public static string LocalizedName(this InventoryCategory category)
    {
        switch (category)
        {
            case InventoryCategory.RetainerBags:
                return "Retainer Bags".Loc();
            case InventoryCategory.RetainerEquipped:
                return "Retainer Equipped".Loc();
        }

        return category.FormattedName().Loc();
    }

    /// <inheritdoc cref="LocalizedName(InventoryCategory)"/>
    public static string LocalizedName(this InventoryCategory? category)
    {
        return category.HasValue ? LocalizedName(category.Value) : "Unknown".Loc();
    }
}
