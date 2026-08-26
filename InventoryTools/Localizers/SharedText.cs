namespace InventoryTools;

// 跨檔逐字重複的使用者可見字串收斂處。
//
// 🔴 為什麼要收斂:.Loc() 是**用英文原文當 key** 去查 LanguageChineseTraditional.ini,
//    而 ini 是字典、同一句只存一條。所以同一句被複製到兩個檔時,改動其中一份的英文
//    會讓**那一份**查不到翻譯而靜默退回英文,另一份照樣是中文 —— 看起來像「漏翻一句」
//    而不是「兩個複製品走散了」。集中成常數之後,改一次兩邊一起改,key 也永遠只有一個。
//
// ⚠️ 這裡只放**真的出現在兩個以上位置、且每一處都被 .Loc() 包住**的字串;
//    只用一次的字串留在使用處比較好讀。
// ⚠️ 字串裡的 ?? 是 Localization.Loc(params object?[]) 的位置參數佔位符,不是缺字。
public static class SharedText {
    public const string EquipmentSuggestSelectedItem = "The item you've selected from the list of recommendations";
    public const string CraftingLogBusy = "Could not open the crafting log, you are currently crafting.";
    public const string CharactersSearchedIn = "The following characters will be searched in: ";
    public const string MarketCheaperThanVendor = "The market price of this item is cheaper than buying it from a vendor and you prefer vendors over the current ingredient preference.";
    public const string DesynthesisClass = "What class is related to de-synthesising this item?";
    public const string RecipeCompleted = "Have the recipes that make this item been completed?";
    public const string OutdatedGear = "Will show any gear considered to be outdated. This will compare the item level of each item with the level of your classes. It will use the lowest level you have applicable to the weapon to determine if it's outdated. Any classes you do not have are not taken into consideration.";
    public const string RecipeTotal = "The number of recipes the item is a component of.";
    public const string AttackDelay = "The time it takes between each automatic attack while engaged with and in range of an enemy in seconds.";
    public const string MateriaCount = "How many materia does this item have or can it have?";
    public const string CraftListEmpty = "No items have been added to the list. Add items via the search menu button at the top right of the screen or by right clicking on an item anywhere within the plugin.";
    public const string NoScopesDefined = "No scopes defined yet. Press add to start.";
    public const string ScopeSpecificCharacter = "Match against a specific character(player character, retainer, free company, etc)";
    public const string ScopeCharacterTypes = "When 'All' or 'World' is selected, choose the types of characters you want to filter against. Select an item again to unselect it.";
    public const string ScopeInvertMatch = "When checked, match against the opposite of what is selected.";
    public const string AlphaIsZero = "The alpha is currently set to 0, this will be invisible.";
    public const string InvertHighlighting = "Should all the items not matching the filter be highlighted instead? If set to N/A will use the 'Invert Highlighting' setting inside the general configuration.";
    public const string GilShopSourceSummary = "?? items available for purchase with gil in ?? zones";
    public const string TooltipModifierKeyOnly = "Should the tooltip only be shown if a modifier key is pressed?";
    public const string EphemeralCraftListCompleted = "Ephemeral craft list '??' completed. List has been removed.";
    public const string ConfirmRemoveList = "Are you sure you want to remove this list?";
    public const string NewSearchListDescription = "This will create a new list that let's you search for specific items within your characters and retainers inventories.";
    public const string NewSortListDescription = "This will create a new list that let's you search for specific items within your characters and retainers inventories then determine where they should be moved to.";
    public const string NewGameItemListDescription = "This will create a list that lets you search for all items in the game.";
    public const string NewHistoryListDescription = "This will create a list that lets you view historical data of how your inventory has changed.";
    public const string ConfirmRemoveFilter = "Are you sure you want to remove this filter?";
    public const string CloseAndShowNextLoad = "Close (and show next time the plugin loads)";
    public const string CraftListContentsCopied = "The craft list's contents were copied to your clipboard.";
    public const string CraftListOutputsCopied = "The craft list's outputs were copied to your clipboard.";
    public const string CraftListGatherablesCopied = "The craft list's gatherables were copied to your clipboard.";
    public const string ClipboardCouldNotBeParsed = "The contents of your clipboard could not be parsed.";
    public const string ClipboardImported = "The contents of your clipboard were imported.";
    public const string GetStartedAddCraftList = "Get started by adding a craft list by hitting the + button on the bottom left.";
    public const string SubmarineExplorationPointIdPrefix = "Submarine Exploration Point with the ID ";

    // 以下三組原本是「一邊有 .Loc() 一邊沒有」:ini 裡三句都已經有繁中翻譯,
    // 但沒被 .Loc() 包住的那一份永遠顯示英文,看起來像漏翻。補上之後一併收斂。
    public const string ExpertDeliverySealCount = "The number of seals that are rewarded when handing this item in as an expert delivery.";
    public const string HasBeenGathered = "Has this gathering item been gathered at least once by the currently logged in character? This only supports mining and botany at present.";
    public const string HighlightDestinationColour = "The color to set any items in the destination that match your source filter(assuming highlight destination duplicates is on).";
}
