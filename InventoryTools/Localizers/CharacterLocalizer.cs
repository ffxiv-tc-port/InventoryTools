using System.Collections.Generic;
using CriticalCommonLib.Models;
using CriticalCommonLib.Services;
using InventoryTools.Logic.Settings;

namespace InventoryTools.Localizers;

/// <summary>
/// Formats the *owner* of an inventory (character, retainer, free company chest, residence) for
/// display. Retainer names are chosen by the player and are allowed to collide with character
/// names, so a bare name is not always enough to tell you where an item actually is.
/// </summary>
public class CharacterLocalizer
{
    private readonly ICharacterMonitor _characterMonitor;

    public CharacterLocalizer(ICharacterMonitor characterMonitor)
    {
        _characterMonitor = characterMonitor;
    }

    /// <summary>
    /// The short word for what kind of owner this is. These four keys are already in the
    /// translation ini (角色 / 僱員 / 公會 / 房屋), reused here rather than adding synonyms.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT CriticalCommonLib's CharacterType.FormattedName(): that returns
    /// "Free Company Chest" (公會儲物櫃), which names the *container*. The bag location printed
    /// next to it already says that, so the owner marker uses the shorter "Free Company" (公會).
    /// </remarks>
    public string TypeName(CharacterType characterType)
    {
        switch (characterType)
        {
            case CharacterType.Character:
                return "Character".Loc();
            case CharacterType.Retainer:
                return "Retainer".Loc();
            case CharacterType.FreeCompanyChest:
                return "Free Company".Loc();
            case CharacterType.Housing:
                return "Residence".Loc();
        }

        return "Unknown".Loc();
    }

    /// <summary>
    /// Every display name that is used by more than one kind of owner. A name in this set cannot
    /// be resolved to a single inventory without also knowing the owner type.
    /// </summary>
    /// <remarks>
    /// Recomputed by the caller once per tooltip generation rather than cached: the character
    /// monitor's dictionary is small (characters + retainers + free companies + houses), and the
    /// game only regenerates an item tooltip when the hovered item changes, not every frame.
    /// Caching would need OnCharacterUpdated/OnCharacterRemoved subscriptions and matching
    /// unsubscription on dispose, which is not worth it for a few dozen string comparisons.
    /// </remarks>
    public HashSet<string> GetAmbiguousNames()
    {
        var firstTypeSeen = new Dictionary<string, CharacterType>();
        var ambiguous = new HashSet<string>();
        foreach (var character in _characterMonitor.Characters)
        {
            var name = character.Value.FormattedName;
            if (name.Length == 0)
            {
                continue;
            }

            if (firstTypeSeen.TryGetValue(name, out var seenType))
            {
                if (seenType != character.Value.CharacterType)
                {
                    ambiguous.Add(name);
                }
            }
            else
            {
                firstTypeSeen[name] = character.Value.CharacterType;
            }
        }

        return ambiguous;
    }

    /// <summary>
    /// The owner's name, affixed with its type when <paramref name="displayMode"/> asks for it.
    /// </summary>
    /// <param name="characterId">The owning character/retainer/free company/house id.</param>
    /// <param name="displayMode">When to affix the type.</param>
    /// <param name="ambiguousNames">
    /// The result of <see cref="GetAmbiguousNames"/>, or null when the caller has not computed it
    /// (in which case WhenAmbiguous affixes nothing).
    /// </param>
    public string FormattedOwnerName(ulong characterId, TooltipOwnerTypeDisplayMode displayMode,
        HashSet<string>? ambiguousNames)
    {
        var name = _characterMonitor.GetCharacterNameById(characterId);
        if (displayMode == TooltipOwnerTypeDisplayMode.Never)
        {
            return name;
        }

        if (displayMode == TooltipOwnerTypeDisplayMode.WhenAmbiguous &&
            (ambiguousNames == null || !ambiguousNames.Contains(name)))
        {
            return name;
        }

        // GetCharacterNameById returns "Unknown" for an id the monitor has never seen; there is no
        // type to report in that case, so leave the name alone rather than affixing "[Unknown]".
        var character = _characterMonitor.GetCharacterById(characterId);
        if (character == null)
        {
            return name;
        }

        return name + " [" + TypeName(character.CharacterType) + "]";
    }
}
