namespace InventoryTools.Logic.Settings;

/// <summary>
/// Controls whether the owner of an item location in the in-game item tooltip is affixed with
/// what kind of owner it is (character/retainer/free company/residence).
/// </summary>
/// <remarks>
/// Off by default. The ordinary retainer-versus-character confusion is handled by naming the containers themselves
/// ("Retainer Bag 1" rather than "Bag 1"). This covers the residue that naming cannot reach: InventoryCategory.Currency
/// and InventoryCategory.Crystals are a single enum value shared by characters and retainers, and a free company or a
/// house may itself be named after one of your characters.
/// <para>
/// The zero value must stay <see cref="Never"/>. The configuration is deserialized with
/// <c>DefaultValueHandling.IgnoreAndPopulate</c>, so a property missing from an existing user's json is populated
/// with the DefaultValue attribute, falling back to default(T) - with the intended default at ordinal 0 both agree.
/// </para>
/// </remarks>
public enum TooltipOwnerTypeDisplayMode
{
    /// <summary>
    /// Never affix the owner type. The default: zero change to what the tooltip looked like.
    /// </summary>
    Never,

    /// <summary>
    /// Only affix the owner type when the same name is used by more than one kind of owner,
    /// e.g. a retainer named the same as one of your characters.
    /// </summary>
    WhenAmbiguous,

    /// <summary>
    /// Always affix the owner type.
    /// </summary>
    Always,
}
