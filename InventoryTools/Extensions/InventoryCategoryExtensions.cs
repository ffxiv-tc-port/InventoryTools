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
    /// CriticalCommonLib's <c>FormattedName</c> returns the bare "Bags"/"Equipped" for both the character and the
    /// retainer categories, which is ambiguous as soon as a retainer shares a name with a character, so the two
    /// retainer-only enum values are given their own names here. This lives in InventoryTools rather than in
    /// CriticalCommonLib because <c>.Loc()</c> does: CriticalCommonLib has no access to the translation ini.
    /// <para>
    /// 🔴 Known residual: <see cref="InventoryCategory.Currency"/> and <see cref="InventoryCategory.Crystals"/> are ONE
    /// enum value each, used for both a character's and a retainer's - only the underlying InventoryType can tell
    /// those apart (see ItemLocalizer.SortedContainerName). Callers that need the distinction fall back to the owner type.
    /// </para>
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
