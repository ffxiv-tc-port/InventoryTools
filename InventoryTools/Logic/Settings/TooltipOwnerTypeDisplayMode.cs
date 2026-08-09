namespace InventoryTools.Logic.Settings;

/// <summary>
/// Controls whether the owner of an item location in the in-game item tooltip is affixed with
/// what kind of owner it is (character/retainer/free company/residence).
/// </summary>
/// <remarks>
/// The zero value must stay <see cref="WhenAmbiguous"/>. The configuration is deserialized with
/// <c>DefaultValueHandling.IgnoreAndPopulate</c>, so a property missing from an existing user's
/// json is populated with the DefaultValue attribute, falling back to default(T) - keeping the
/// intended default at ordinal 0 means the setting behaves the same either way.
/// </remarks>
public enum TooltipOwnerTypeDisplayMode
{
    /// <summary>
    /// Only affix the owner type when the same name is used by more than one kind of owner,
    /// e.g. a retainer named the same as one of your characters. This is the default: for anyone
    /// without a name clash the tooltip looks exactly as it did before.
    /// </summary>
    WhenAmbiguous,

    /// <summary>
    /// Always affix the owner type.
    /// </summary>
    Always,

    /// <summary>
    /// Never affix the owner type, even when the name is ambiguous.
    /// </summary>
    Never,
}
