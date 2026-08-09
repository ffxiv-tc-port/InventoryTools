using System.Collections.Generic;
using CriticalCommonLib.Enums;
using CriticalCommonLib.Models;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace InventoryTools.Localizers;

public class ItemLocalizer
{
    private readonly ExcelSheet<Addon> _addonSheet;
    private Dictionary<uint, string> _cabinetNames;

    public ItemLocalizer(ExcelSheet<Addon> addonSheet)
    {
        _addonSheet = addonSheet;
        _cabinetNames = new();
    }

    public string CabinetName(InventoryItem inventoryItem)
    {
        if (inventoryItem.SortedContainer != InventoryType.Armoire)
        {
            return "";
        }

        var cabinetCategory = inventoryItem.Item.CabinetCategory;
        if (cabinetCategory == null)
        {
            return "Unknown Cabinet";
        }

        if (_cabinetNames.TryGetValue(cabinetCategory.Base.Category.RowId, out string? cabinetName))
        {
            return cabinetName;
        }

        cabinetName = _addonSheet.GetRowOrDefault(cabinetCategory.Base.Category.RowId)?.Text.ExtractText() ??
                      "Addon Text Not Found";

        _cabinetNames[cabinetCategory.Base.Category.RowId] = cabinetName;

        return cabinetName;
    }

    public string ItemDescription(InventoryItem inventoryItem)
    {
        if (inventoryItem.IsEmpty)
        {
            return "Empty";
        }

        var _item = inventoryItem.Item.NameString.ToString();
        if (inventoryItem.IsHQ)
        {
            _item += " (HQ)";
        }
        else if (inventoryItem.IsCollectible)
        {
            _item += " (Collectible)";
        }
        else
        {
            _item += " (NQ)";
        }

        if (inventoryItem.SortedCategory == InventoryCategory.Currency)
        {
            _item += " - " + SortedContainerName(inventoryItem);
        }
        else
        {
            _item += " - " + SortedContainerName(inventoryItem) + " - " + (inventoryItem.SortedSlotIndex + 1);
        }


        return _item;
    }

    public string FormattedBagLocation(InventoryItem inventoryItem)
    {
        if (inventoryItem.SortedContainer is InventoryType.GlamourChest or InventoryType.Currency or InventoryType.RetainerGil or InventoryType.FreeCompanyGil or InventoryType.Crystal or InventoryType.RetainerCrystal)
        {
            return SortedContainerName(inventoryItem);
        }
        return SortedContainerName(inventoryItem) + " - " + (inventoryItem.SortedSlotIndex + 1);
    }

    /// <summary>
    /// The display name of the container an item sits in.
    /// </summary>
    /// <remarks>
    /// Retainer containers get names of their own rather than sharing the character ones. A
    /// retainer may be given the same name as a character, so "Foo - Bag 1" was genuinely
    /// ambiguous: it read identically whether the item was in the character's first bag or in the
    /// same-named retainer's. The five retainer bags, retainer equipped gear, retainer gil and
    /// retainer crystals are the containers that collided; the retainer market always had a name
    /// of its own and is left alone.
    /// Retainer bag 5 has no character counterpart to collide with, but it is renamed alongside
    /// the other four regardless - "Retainer Bag 1..4" next to a bare "Bag 5" would read as a
    /// character bag.
    /// </remarks>
    public string SortedContainerName(InventoryItem inventoryItem)
    {
        if(inventoryItem.SortedContainer is InventoryType.Bag0)
        {
            return "Bag 1".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.Bag1)
        {
            return "Bag 2".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.Bag2)
        {
            return "Bag 3".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.Bag3)
        {
            return "Bag 4".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.RetainerBag0)
        {
            return "Retainer Bag 1".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.RetainerBag1)
        {
            return "Retainer Bag 2".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.RetainerBag2)
        {
            return "Retainer Bag 3".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.RetainerBag3)
        {
            return "Retainer Bag 4".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.RetainerBag4)
        {
            return "Retainer Bag 5".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.SaddleBag0)
        {
            return "Saddlebag Left".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.SaddleBag1)
        {
            return "Saddlebag Right".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.PremiumSaddleBag0)
        {
            return "Premium Saddlebag Left".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.PremiumSaddleBag1)
        {
            return "Premium Saddlebag Right".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.ArmoryBody)
        {
            return "Armory - Body".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.ArmoryEar)
        {
            return "Armory - Ear".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.ArmoryFeet)
        {
            return "Armory - Feet".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.ArmoryHand)
        {
            return "Armory - Hand".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.ArmoryHead)
        {
            return "Armory - Head".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.ArmoryLegs)
        {
            return "Armory - Legs".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.ArmoryMain)
        {
            return "Armory - Main".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.ArmoryNeck)
        {
            return "Armory - Neck".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.ArmoryOff)
        {
            return "Armory - Offhand".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.ArmoryRing)
        {
            return "Armory - Ring".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.ArmoryWaist)
        {
            return "Armory - Waist".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.ArmoryWrist)
        {
            return "Armory - Wrist".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.ArmorySoulCrystal)
        {
            return "Armory - Soul Crystal".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.GearSet0)
        {
            return "Equipped Gear".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.RetainerEquippedGear)
        {
            return "Retainer Equipped Gear".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.FreeCompanyBag0)
        {
            return "Free Company Chest".Loc() + " - 1";
        }
        if(inventoryItem.SortedContainer is InventoryType.FreeCompanyBag1)
        {
            return "Free Company Chest".Loc() + " - 2";
        }
        if(inventoryItem.SortedContainer is InventoryType.FreeCompanyBag2)
        {
            return "Free Company Chest".Loc() + " - 3";
        }
        if(inventoryItem.SortedContainer is InventoryType.FreeCompanyBag3)
        {
            return "Free Company Chest".Loc() + " - 4";
        }
        if(inventoryItem.SortedContainer is InventoryType.FreeCompanyBag4)
        {
            return "Free Company Chest".Loc() + " - 5";
        }
        if(inventoryItem.SortedContainer is InventoryType.FreeCompanyBag5)
        {
            return "Free Company Chest".Loc() + " - 6";
        }
        if(inventoryItem.SortedContainer is InventoryType.FreeCompanyBag6)
        {
            return "Free Company Chest".Loc() + " - 7";
        }
        if(inventoryItem.SortedContainer is InventoryType.FreeCompanyBag7)
        {
            return "Free Company Chest".Loc() + " - 8";
        }
        if(inventoryItem.SortedContainer is InventoryType.FreeCompanyBag8)
        {
            return "Free Company Chest".Loc() + " - 9";
        }
        if(inventoryItem.SortedContainer is InventoryType.FreeCompanyBag9)
        {
            return "Free Company Chest".Loc() + " - 10";
        }
        if(inventoryItem.SortedContainer is InventoryType.FreeCompanyBag10)
        {
            return "Free Company Chest".Loc() + " - 11";
        }
        if(inventoryItem.SortedContainer is InventoryType.RetainerMarket)
        {
            return "Market".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.GlamourChest)
        {
            return "Glamour Chest".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.Armoire)
        {
            return "Armoire".Loc() + " - " + CabinetName(inventoryItem);
        }
        if(inventoryItem.SortedContainer is InventoryType.Currency)
        {
            return "Currency".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.FreeCompanyGil)
        {
            return "Free Company - Gil".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.RetainerGil)
        {
            return "Retainer Currency".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.FreeCompanyCrystal)
        {
            return "Free Company - Crystals".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.FreeCompanyCurrency)
        {
            return "Free Company - Currency".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.RetainerCrystal)
        {
            return "Retainer Crystals".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.Crystal)
        {
            return "Crystals".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.HousingExteriorAppearance)
        {
            return "Housing Exterior Appearance".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.HousingInteriorAppearance)
        {
            return "Housing Interior Appearance".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.HousingExteriorStoreroom)
        {
            return "Housing Exterior Storeroom".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.HousingInteriorStoreroom1 or InventoryType.HousingInteriorStoreroom2 or InventoryType.HousingInteriorStoreroom2 or InventoryType.HousingInteriorStoreroom3 or InventoryType.HousingInteriorStoreroom4 or InventoryType.HousingInteriorStoreroom5 or InventoryType.HousingInteriorStoreroom6 or InventoryType.HousingInteriorStoreroom7 or InventoryType.HousingInteriorStoreroom8)
        {
            return "Housing Interior Storeroom".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.HousingInteriorPlacedItems1 or InventoryType.HousingInteriorPlacedItems2 or InventoryType.HousingInteriorPlacedItems2 or InventoryType.HousingInteriorPlacedItems3 or InventoryType.HousingInteriorPlacedItems4 or InventoryType.HousingInteriorPlacedItems5 or InventoryType.HousingInteriorPlacedItems6 or InventoryType.HousingInteriorPlacedItems7 or InventoryType.HousingInteriorPlacedItems8)
        {
            return "Housing Interior Placed Items".Loc();
        }
        if(inventoryItem.SortedContainer is InventoryType.HousingExteriorPlacedItems)
        {
            return "Housing Exterior Placed Items".Loc();
        }

        return inventoryItem.SortedContainer.ToString();
    }
}