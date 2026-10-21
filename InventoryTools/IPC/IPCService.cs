using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CriticalCommonLib.Enums;
using CriticalCommonLib.Models;
using CriticalCommonLib.Services;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Plugin.Ipc;
using InventoryTools.Lists;
using InventoryTools.Logic;
using InventoryTools.Logic.Editors;
using InventoryTools.Services.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using InventoryItem = FFXIVClientStructs.FFXIV.Client.Game.InventoryItem;

namespace InventoryTools.IPC;

public class IPCService : IHostedService
{
    private ICallGateProvider<uint, ulong?, uint>? _inventoryCountByType;
    private ICallGateProvider<uint[], ulong?, uint>? _inventoryCountByTypes;
    private ICallGateProvider<uint, ulong, int, uint>? _itemCount;
    private ICallGateProvider<uint, ulong, int, uint>? _itemCountHQ;
    private ICallGateProvider<uint, bool, uint[], uint>? _itemCountOwned;
    private ICallGateProvider<uint, bool, uint[], bool, uint>? _itemCountOwnedByCategory;
    private ICallGateProvider<uint, bool, uint[], bool, Dictionary<ulong, uint>>? _getItemCountsByCharacter;
    private ICallGateProvider<string, bool>? _enableUiFilter;
    private ICallGateProvider<bool>? _disableUiFilter;
    private ICallGateProvider<string, bool>? _toggleUiFilter;
    private ICallGateProvider<string, bool>? _enableBackgroundFilter;
    private ICallGateProvider<bool>? _disableBackgroundFilter;
    private ICallGateProvider<string, bool>? _toggleBackgroundFilter;
    private ICallGateProvider<string, bool>? _enableCraftList;
    private ICallGateProvider<bool>? _disableCraftList;
    private ICallGateProvider<string, bool>? _toggleCraftList;
    private ICallGateProvider<string, uint, uint, bool>? _addItemToCraftList;
    private ICallGateProvider<string, uint, uint, bool>? _removeItemFromCraftList;
    private ICallGateProvider<string, Dictionary<uint, uint>>? _getFilterItems;
    private ICallGateProvider<string, Dictionary<uint, uint>>? _getCraftItems;
    private ICallGateProvider<Dictionary<uint, uint>>? _getRetrievalItems;
    private ICallGateProvider<ulong, HashSet<ulong[]>> _getCharacterItems;
    private ICallGateProvider<bool, HashSet<ulong>> _getCharactersOwnedByActive;
    private ICallGateProvider<ulong, uint, HashSet<ulong[]>> _getCharacterItemsByType;
    private ICallGateProvider<(uint, InventoryItem.ItemFlags, ulong, uint), bool>? _itemAdded;
    private ICallGateProvider<(uint, InventoryItem.ItemFlags, ulong, uint), bool>? _itemRemoved;
    private ICallGateProvider<Dictionary<string,string>>? _getCraftLists;
    private ICallGateProvider<Dictionary<string,string>>? _getSearchFilters;
    private ICallGateProvider<string, Dictionary<uint, uint>, string>? _addNewCraftList;
    private ICallGateProvider<ulong>? _currentCharacter;
    private ICallGateProvider<ulong?, bool>? _retainerChanged;
    private ICallGateProvider<bool>? _isInitialized;
    private ICallGateProvider<bool, bool>? _initialized;
    private bool _initalizedIpc;

    private readonly IDalamudPluginInterface _pluginInterface;
    private ILogger<IPCService> _logger;
    private ICharacterMonitor _characterMonitor;
    private IListService _listService;
    private IInventoryMonitor _inventoryMonitor;
    private ListFilterService _listFilterService;
    private ItemCountIndex? _countIndex;
    private readonly object _countIndexBuildLock = new();
    private readonly ItemCountIndex.Builder _countIndexBuilder = new();
    private readonly IFramework _framework;
    private readonly InventoryScopeCalculator _inventoryScopeCalculator;
    private static readonly TimeSpan IpcFrameworkWait = TimeSpan.FromSeconds(5);

    public IPCService(IDalamudPluginInterface pluginInterface, ILogger<IPCService> logger, ICharacterMonitor characterMonitor, IListService listService, IInventoryMonitor inventoryMonitor, ListFilterService listFilterService, IFramework framework, InventoryScopeCalculator inventoryScopeCalculator)
    {
        _framework = framework;
        _inventoryScopeCalculator = inventoryScopeCalculator;
        _pluginInterface = pluginInterface;
        _logger = logger;
        _characterMonitor = characterMonitor;
        _listService = listService;
        _inventoryMonitor = inventoryMonitor;
        _listFilterService = listFilterService;
    }

    private void InventoryMonitorOnOnInventoryChanged(List<InventoryChange> inventoryChanges, InventoryMonitor.ItemChanges? changedItems)
    {
        if (changedItems != null)
        {
            foreach (var changedItem in changedItems.NewItems)
            {
                if (changedItem.ItemId != 1)
                {
                    _itemAdded?.SendMessage((changedItem.ItemId, changedItem.Flags, changedItem.CharacterId,
                        (uint)changedItem.Quantity));
                }
            }

            foreach (var changedItem in changedItems.RemovedItems)
            {
                if (changedItem.ItemId != 1)
                {
                    _itemRemoved?.SendMessage((changedItem.ItemId, changedItem.Flags, changedItem.CharacterId,
                        (uint)changedItem.Quantity));
                }
            }
        }
    }

    // IPC 跑在呼叫端的執行緒（框架或執行緒池）。每次讀一次 AllItems，拿那份清單的索引：
    // 與「對那份清單逐件掃」結果相同；清單換新後第一次查詢才重建，讀端不加鎖。
    private ItemCountIndex CurrentCountIndex()
    {
        var items = _inventoryMonitor.AllItems;
        var index = Volatile.Read(ref _countIndex);
        if (index != null && ReferenceEquals(index.Source, items)) return index;
        lock (_countIndexBuildLock)
        {
            index = Volatile.Read(ref _countIndex);
            if (index == null || !ReferenceEquals(index.Source, items))
            {
                index = _countIndexBuilder.Build(items);
                Volatile.Write(ref _countIndex, index);
            }
            return index;
        }
    }

    private uint ItemCount(uint itemId, ulong characterId, int inventoryType)
    {
        long total = 0;
        foreach (ref readonly var g in CurrentCountIndex().ForItem(itemId))
        {
            if ((inventoryType == -1 || (uint)g.Container == inventoryType) && (g.RetainerId == characterId)) total += g.Quantity;
        }
        return (uint)total;
    }

    private uint ItemCountHQ(uint itemId, ulong characterId, int inventoryType)
    {
        long total = 0;
        foreach (ref readonly var g in CurrentCountIndex().ForItem(itemId))
        {
            if (g.Flags == InventoryItem.ItemFlags.HighQuality && (inventoryType == -1 || (uint)g.Container == inventoryType) && (g.RetainerId == characterId)) total += g.Quantity;
        }
        return (uint)total;
    }

    private uint ItemCountOwned(uint itemId, bool currentCharacterOnly, uint[] inventoryTypes)
    {
        long total = 0;
        foreach (ref readonly var g in CurrentCountIndex().ForItem(itemId))
        {
            if (inventoryTypes.Contains((uint)g.Container)
                && _characterMonitor.Characters.ContainsKey(g.RetainerId)
                && (!currentCharacterOnly || _characterMonitor.BelongsToActiveCharacter(g.RetainerId)))
            {
                total += g.Quantity;
            }
        }
        return (uint)total;
    }

    // 新端點的角色型別判斷要讀 CharacterMonitor 的一般 Dictionary（框架執行緒會寫），所以排到框架執行緒算；
    // 呼叫端已在框架執行緒就直接算。等不到（卸載中或逾時）回 fallback，不擲例外。
    private T OnFrameworkThread<T>(Func<T> compute, T fallback, string endpoint)
    {
        if (_framework.IsInFrameworkUpdateThread)
        {
            return compute();
        }

        if (_framework.IsFrameworkUnloading)
        {
            return fallback;
        }

        var task = _framework.RunOnFrameworkThread(compute);
        if (Task.WhenAny(task, Task.Delay(IpcFrameworkWait)).GetAwaiter().GetResult() != task)
        {
            _logger.LogWarning("IPC {Endpoint} timed out waiting for the framework thread, returning an empty result.", endpoint);
            return fallback;
        }

        return task.GetAwaiter().GetResult();
    }

    private static InventorySearchScope OwnedScope(bool currentCharacterOnly, uint[]? inventoryCategories, bool includeSharedStorage)
    {
        return new InventorySearchScope
        {
            ActiveCharacter = currentCharacterOnly ? true : null,
            Categories = inventoryCategories is not { Length: > 0 }
                ? null
                : inventoryCategories.Select(c => (InventoryCategory)c).ToHashSet(),
            CharacterTypes = includeSharedStorage ? null : [CharacterType.Character, CharacterType.Retainer],
        };
    }

    private bool InOwnedScope(InventorySearchScope scope, in ItemCountIndex.ItemGroup group)
    {
        return _characterMonitor.Characters.ContainsKey(group.RetainerId)
               && _inventoryScopeCalculator.Filter(scope, group.RetainerId, group.Category);
    }

    /// <summary>
    /// 一個物品在角色、僱員（可選含公會箱與房屋）身上的總數，NQ 與 HQ 合計。
    /// inventoryCategories 是 <see cref="InventoryCategory"/> 值，空陣列＝全部分類；includeSharedStorage=false 時一律排除公會與房屋。
    /// </summary>
    private uint ItemCountOwnedByCategory(uint itemId, bool currentCharacterOnly, uint[] inventoryCategories, bool includeSharedStorage)
    {
        return OnFrameworkThread(() => ItemCountOwnedByCategoryCore(itemId, currentCharacterOnly, inventoryCategories, includeSharedStorage),
            0u, "ItemCountOwnedByCategory");
    }

    private uint ItemCountOwnedByCategoryCore(uint itemId, bool currentCharacterOnly, uint[]? inventoryCategories, bool includeSharedStorage)
    {
        var scope = OwnedScope(currentCharacterOnly, inventoryCategories, includeSharedStorage);
        long total = 0;
        foreach (ref readonly var g in CurrentCountIndex().ForItem(itemId))
        {
            if (InOwnedScope(scope, g)) total += g.Quantity;
        }
        return (uint)Math.Clamp(total, 0, uint.MaxValue);
    }

    /// <summary>同 ItemCountOwnedByCategory，但依持有者（角色／僱員／公會／房屋 id）分開列；沒有持有的不列。</summary>
    private Dictionary<ulong, uint> GetItemCountsByCharacter(uint itemId, bool currentCharacterOnly, uint[] inventoryCategories, bool includeSharedStorage)
    {
        return OnFrameworkThread(() => GetItemCountsByCharacterCore(itemId, currentCharacterOnly, inventoryCategories, includeSharedStorage),
            new Dictionary<ulong, uint>(), "GetItemCountsByCharacter");
    }

    private Dictionary<ulong, uint> GetItemCountsByCharacterCore(uint itemId, bool currentCharacterOnly, uint[]? inventoryCategories, bool includeSharedStorage)
    {
        var scope = OwnedScope(currentCharacterOnly, inventoryCategories, includeSharedStorage);
        var totals = new Dictionary<ulong, long>();
        foreach (ref readonly var g in CurrentCountIndex().ForItem(itemId))
        {
            if (InOwnedScope(scope, g)) totals[g.RetainerId] = totals.GetValueOrDefault(g.RetainerId) + g.Quantity;
        }

        var itemCounts = new Dictionary<ulong, uint>(totals.Count);
        foreach (var (owner, total) in totals)
        {
            if (total > 0) itemCounts[owner] = (uint)Math.Min(total, uint.MaxValue);
        }
        return itemCounts;
    }

    private bool IsInitialized()
    {
        return _initalizedIpc;
    }

    private ulong CurrentCharacter()
    {
        return _characterMonitor.ActiveCharacterId;
    }

    private Dictionary<string, string> GetSearchFilters()
    {
        var filters = _listService.Lists.Where(c => c.FilterType == FilterType.SearchFilter);
        var keyNameDict = new Dictionary<string, string>();
        foreach (var filter in filters)
        {
            keyNameDict.Add(filter.Key, filter.Name);
        }
        return keyNameDict;
    }

    private string AddNewCraftList(string craftListName, Dictionary<uint, uint> items)
    {
        var newCraftFilter = _listService.AddNewCraftList(craftListName);
        foreach (var item in items)
        {
            newCraftFilter.CraftList.AddCraftItem(item.Key, item.Value);
        }

        return newCraftFilter.Key;
    }

    private Dictionary<string, string> GetCraftLists()
    {
        var craftLists = _listService.Lists.Where(c => c.FilterType == FilterType.CraftFilter && !c.CraftListDefault);
        var keyNameDict = new Dictionary<string, string>();
        foreach (var craftList in craftLists)
        {
            keyNameDict.Add(craftList.Key, craftList.Name);
        }

        return keyNameDict;
    }

    private Dictionary<uint, uint> GetCraftItems(string filterKey)
    {
        var filter = _listService.GetListByKeyOrName(filterKey);
        var craftItems = new Dictionary<uint, uint>();
        
        if (filter != null && filter.FilterType == FilterType.CraftFilter)
        {
            foreach (var craftItem in filter.CraftList.CraftItems)
            {
                if (craftItem.IsOutputItem)
                {
                    if (craftItems.ContainsKey(craftItem.ItemId))
                    {
                        craftItems[craftItem.ItemId] += craftItem.QuantityRequired;
                    }
                    else
                    {
                        craftItems.Add(craftItem.ItemId, craftItem.QuantityRequired);
                    }
                }
            }
        }

        return craftItems;
    }

    private Dictionary<uint, uint> GetRetrievalItems()
    {
        var filter = _listService.GetActiveCraftList();
        var retrievalItems = new Dictionary<uint, uint>();

        if (filter != null && filter.FilterType == FilterType.CraftFilter)
        {
            foreach (((var itemId, _), var quantity) in filter.CraftList.GetQuantityToRetrieveList())
            {
                if (quantity == 0) continue;

                if (retrievalItems.ContainsKey(itemId))
                {
                    retrievalItems[itemId] += quantity;
                } else {
                    retrievalItems.Add(itemId, quantity);
                }
            }
        }

        return retrievalItems;
    }

    private HashSet<ulong> GetCharactersOwnedByActive(bool includeOwner)
        => _inventoryMonitor.Inventories.Select(pair => pair.Key)
            .Where(characterId => _characterMonitor.BelongsToActiveCharacter(characterId) && (includeOwner || characterId != _characterMonitor.ActiveCharacterId))
            .ToHashSet();

    private HashSet<ulong[]> GetCharacterItems(ulong characterId)
    {
        if (!_inventoryMonitor.Inventories.TryGetValue(characterId, out var characterInventories))
        {
            return [];
        }

        var items = characterInventories
            .GetAllInventories().SelectMany(item => item)
            .Where(item => item != null);

        if (items == null) return new();
        return items.Select(item => item!.ToNumeric())
            .ToHashSet();
    }

    private HashSet<ulong[]> GetCharacterItemsByType(ulong characterId, uint inventoryType)
    {
        if (!_inventoryMonitor.Inventories.TryGetValue(characterId, out var characterInventories))
        {
            return [];
        }
        
        var items = (characterInventories
                .GetInventoryByType((InventoryType)inventoryType) ?? Array.Empty<CriticalCommonLib.Models.InventoryItem?>())
            .Where(item => item != null);

        return items.Select(item => item!.ToNumeric())
            .ToHashSet();
    }
    private Dictionary<uint, uint> GetFilterItems(string filterKey)
    {
        var filter = _listService.GetListByKeyOrName(filterKey);
        var filterItems = new Dictionary<uint, uint>();
        
        if (filter != null)
        {
            if (filter.FilterType == FilterType.CraftFilter)
            {
                foreach (var craftItem in filter.CraftList.CraftItems)
                {
                    if (craftItem.IsOutputItem)
                    {
                        if (filterItems.ContainsKey(craftItem.ItemId))
                        {
                            filterItems[craftItem.ItemId] += craftItem.QuantityRequired;
                        }
                        else
                        {
                            filterItems.Add(craftItem.ItemId, craftItem.QuantityRequired);
                        }
                    }
                }
            }
            if (filter.FilterType == FilterType.SearchFilter || filter.FilterType == FilterType.SortingFilter)
            {
                var filterResult = _listFilterService.RefreshList(filter);
                foreach (var sortedItem in filterResult)
                {
                    if (sortedItem.InventoryItem != null)
                    {
                        if (filterItems.ContainsKey(sortedItem.InventoryItem.ItemId))
                        {
                            filterItems[sortedItem.InventoryItem.ItemId] += sortedItem.InventoryItem.Quantity;
                        }
                        else
                        {
                            filterItems.Add(sortedItem.InventoryItem.ItemId, sortedItem.InventoryItem.Quantity);
                        }
                    }
                }
            }
            if (filter.FilterType == FilterType.GameItemFilter)
            {
                var filterResult = _listFilterService.RefreshList(filter);
                foreach (var sortedItem in filterResult)
                {
                    if (filterItems.ContainsKey(sortedItem.Item.RowId))
                    {
                        filterItems[sortedItem.Item.RowId] += 0;
                    }
                    else
                    {
                        filterItems.Add(sortedItem.Item.RowId, 0);
                    }
                }
            }
        }

        return filterItems;
    }

    private bool RemoveItemFromCraftList(string filterKey, uint itemId, uint quantity)
    {
        var filter = _listService.GetListByKeyOrName(filterKey);
        
        if (filter is { FilterType: FilterType.CraftFilter })
        {
            filter.CraftList.RemoveCraftItem(itemId, quantity, InventoryItem.ItemFlags.None);
            filter.NeedsRefresh = true;
        }

        return false;
    }

    private bool AddItemToCraftList(string filterKey, uint itemId, uint quantity)
    {
        var filter = _listService.GetListByKeyOrName(filterKey);
        
        if (filter is { FilterType: FilterType.CraftFilter })
        {
            filter.CraftList.AddCraftItem(itemId, quantity, InventoryItem.ItemFlags.None);
            filter.NeedsRefresh = true;
        }

        return false;
    }

    private bool ToggleUiFilter(string filterKey)
    {
        var filter = _listService.GetListByKeyOrName(filterKey);

        if (filter == null)
        {
            filter = _listService.GetList(filterKey);
        }
        
        if (filter != null)
        {
            _listService.ToggleActiveUiList(filter);
            return true;
        }

        return false;
    }

    private bool DisableUiFilter()
    {
        _listService.ClearActiveUiList();
        return true;
    }

    private bool EnableUiFilter(string filterKey)
    {
        var filter = _listService.GetListByKeyOrName(filterKey);
        
        if (filter != null)
        {
            _listService.SetActiveUiList(filter);
            return true;
        }

        return false;
    }

    private bool ToggleBackgroundFilter(string filterKey)
    {
        var filter = _listService.GetListByKeyOrName(filterKey);
        
        if (filter != null)
        {
            _listService.ToggleActiveBackgroundList(filter);
            return true;
        }

        return false;
    }

    private bool DisableBackgroundFilter()
    {
        _listService.ClearActiveBackgroundList();
        return true;
    }

    private bool EnableBackgroundFilter(string filterKey)
    {
        var filter = _listService.GetListByKeyOrName(filterKey);
        
        if (filter != null)
        {
            _listService.SetActiveBackgroundList(filter);
            return true;
        }

        return false;
    }

    private bool ToggleCraftList(string filterKey)
    {
        var filter = _listService.GetListByKeyOrName(filterKey);

        if (filter == null)
        {
            filter = _listService.GetList(filterKey);
        }
        
        if (filter != null)
        {
            _listService.ToggleActiveCraftList(filter);
            return true;
        }

        return false;
    }

    private bool DisableCraftList()
    {
        _listService.ClearActiveCraftList();
        return true;
    }

    private bool EnableCraftList(string filterKey)
    {
        var filter = _listService.GetListByKeyOrName(filterKey);
        
        if (filter != null)
        {
            _listService.SetActiveCraftList(filter);
            return true;
        }

        return false;
    }

    private void CharacterMonitorOnOnActiveRetainerChanged(ulong retainerId)
    {
        _retainerChanged?.SendMessage(retainerId);
    }

    private uint InventoryCountByTypes(uint[] inventoryTypes, ulong? characterId)
    {
        var index = CurrentCountIndex();
        long total = 0;
        foreach (var container in index.Containers)
        {
            if (!inventoryTypes.Contains(container)) continue;
            foreach (ref readonly var g in index.ForContainer(container))
            {
                if (characterId == null || g.RetainerId == characterId) total += g.Quantity;
            }
        }
        return (uint)total;
    }

    private uint InventoryCountByType(uint inventoryType, ulong? characterId)
    {
        long total = 0;
        foreach (ref readonly var g in CurrentCountIndex().ForContainer(inventoryType))
        {
            if (characterId == null || g.RetainerId == characterId) total += g.Quantity;
        }
        return (uint)total;
    }            
    
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogTrace("Starting service {type} ({this})", GetType().Name, this);
        _inventoryCountByType = _pluginInterface.GetIpcProvider<uint, ulong?, uint>("AllaganTools.InventoryCountByType");
        _inventoryCountByType.RegisterFunc(InventoryCountByType);
        
        _inventoryCountByTypes = _pluginInterface.GetIpcProvider<uint[], ulong?, uint>("AllaganTools.InventoryCountByTypes");
        _inventoryCountByTypes.RegisterFunc(InventoryCountByTypes);
        
        _itemCount = _pluginInterface.GetIpcProvider<uint, ulong, int, uint>("AllaganTools.ItemCount");
        _itemCount.RegisterFunc(ItemCount);

        _itemCountHQ = _pluginInterface.GetIpcProvider<uint, ulong, int, uint>("AllaganTools.ItemCountHQ");
        _itemCountHQ.RegisterFunc(ItemCountHQ);

        _itemCountOwned = _pluginInterface.GetIpcProvider<uint, bool, uint[], uint>("AllaganTools.ItemCountOwned");
        _itemCountOwned.RegisterFunc(ItemCountOwned);

        _itemCountOwnedByCategory = _pluginInterface.GetIpcProvider<uint, bool, uint[], bool, uint>("AllaganTools.ItemCountOwnedByCategory");
        _itemCountOwnedByCategory.RegisterFunc(ItemCountOwnedByCategory);

        _getItemCountsByCharacter = _pluginInterface.GetIpcProvider<uint, bool, uint[], bool, Dictionary<ulong, uint>>("AllaganTools.GetItemCountsByCharacter");
        _getItemCountsByCharacter.RegisterFunc(GetItemCountsByCharacter);

        _enableUiFilter = _pluginInterface.GetIpcProvider<string, bool>("AllaganTools.EnableUiFilter");
        _enableUiFilter.RegisterFunc(EnableUiFilter);

        _disableUiFilter = _pluginInterface.GetIpcProvider<bool>("AllaganTools.DisableUiFilter");
        _disableUiFilter.RegisterFunc(DisableUiFilter);

        _toggleUiFilter = _pluginInterface.GetIpcProvider<string, bool>("AllaganTools.ToggleUiFilter");
        _toggleUiFilter.RegisterFunc(ToggleUiFilter);

        _enableBackgroundFilter = _pluginInterface.GetIpcProvider<string, bool>("AllaganTools.EnableBackgroundFilter");
        _enableBackgroundFilter.RegisterFunc(EnableBackgroundFilter);

        _disableBackgroundFilter = _pluginInterface.GetIpcProvider<bool>("AllaganTools.DisableBackgroundFilter");
        _disableBackgroundFilter.RegisterFunc(DisableBackgroundFilter);

        _toggleBackgroundFilter = _pluginInterface.GetIpcProvider<string, bool>("AllaganTools.ToggleBackgroundFilter");
        _toggleBackgroundFilter.RegisterFunc(ToggleBackgroundFilter);

        _enableCraftList = _pluginInterface.GetIpcProvider<string, bool>("AllaganTools.EnableCraftList");
        _enableCraftList.RegisterFunc(EnableCraftList);

        _disableCraftList = _pluginInterface.GetIpcProvider<bool>("AllaganTools.DisableCraftList");
        _disableCraftList.RegisterFunc(DisableCraftList);

        _toggleCraftList = _pluginInterface.GetIpcProvider<string, bool>("AllaganTools.ToggleCraftList");
        _toggleCraftList.RegisterFunc(ToggleCraftList);

        _addItemToCraftList = _pluginInterface.GetIpcProvider<string, uint, uint, bool>("AllaganTools.AddItemToCraftList");
        _addItemToCraftList.RegisterFunc(AddItemToCraftList);

        _removeItemFromCraftList = _pluginInterface.GetIpcProvider<string, uint, uint, bool>("AllaganTools.RemoveItemFromCraftList");
        _removeItemFromCraftList.RegisterFunc(RemoveItemFromCraftList);

        _getFilterItems = _pluginInterface.GetIpcProvider<string, Dictionary<uint, uint>>("AllaganTools.GetFilterItems");
        _getFilterItems.RegisterFunc(GetFilterItems);

        _getCraftItems = _pluginInterface.GetIpcProvider<string, Dictionary<uint, uint>>("AllaganTools.GetCraftItems");
        _getCraftItems.RegisterFunc(GetCraftItems);

        _getRetrievalItems = _pluginInterface.GetIpcProvider<Dictionary<uint, uint>>("AllaganTools.GetRetrievalItems");
        _getRetrievalItems.RegisterFunc(GetRetrievalItems);

        _getCharacterItems = _pluginInterface.GetIpcProvider<ulong, HashSet<ulong[]>>("AllaganTools.GetCharacterItems");
        _getCharacterItems.RegisterFunc(GetCharacterItems);

        _getCharactersOwnedByActive = _pluginInterface.GetIpcProvider<bool, HashSet<ulong>>("AllaganTools.GetCharactersOwnedByActive");
        _getCharactersOwnedByActive.RegisterFunc(GetCharactersOwnedByActive);

        _getCharacterItemsByType = _pluginInterface.GetIpcProvider<ulong,uint, HashSet<ulong[]>>("AllaganTools.GetCharacterItemsByType");
        _getCharacterItemsByType.RegisterFunc(GetCharacterItemsByType);

        _getSearchFilters = _pluginInterface.GetIpcProvider<Dictionary<string,string>>("AllaganTools.GetSearchFilters");
        _getSearchFilters.RegisterFunc(GetSearchFilters);

        _getCraftLists = _pluginInterface.GetIpcProvider<Dictionary<string,string>>("AllaganTools.GetCraftLists");
        _getCraftLists.RegisterFunc(GetCraftLists);

        _addNewCraftList = _pluginInterface.GetIpcProvider<string, Dictionary<uint, uint>, string>("AllaganTools.AddNewCraftList");
        _addNewCraftList.RegisterFunc(AddNewCraftList);

        _currentCharacter = _pluginInterface.GetIpcProvider<ulong>("AllaganTools.CurrentCharacter");
        _currentCharacter.RegisterFunc(CurrentCharacter);

        _isInitialized = _pluginInterface.GetIpcProvider<bool>("AllaganTools.IsInitialized");
        _isInitialized.RegisterFunc(IsInitialized);
        
        //Events
        _retainerChanged = _pluginInterface.GetIpcProvider<ulong?, bool>("AllaganTools.RetainerChanged");
        _itemAdded = _pluginInterface.GetIpcProvider<(uint, InventoryItem.ItemFlags, ulong, uint), bool>("AllaganTools.ItemAdded");
        _itemRemoved = _pluginInterface.GetIpcProvider<(uint, InventoryItem.ItemFlags, ulong, uint), bool>("AllaganTools.ItemRemoved");
        _initialized = _pluginInterface.GetIpcProvider<bool, bool>("AllaganTools.Initialized");

        //External Events
        _characterMonitor.OnActiveRetainerChanged += CharacterMonitorOnOnActiveRetainerChanged;
        _inventoryMonitor.OnInventoryChanged += InventoryMonitorOnOnInventoryChanged;

        _initalizedIpc = true;
        
        _initialized.SendMessage(true);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogTrace("Stopping service {Type} ({This})", GetType().Name, this);
        _characterMonitor.OnActiveRetainerChanged -= CharacterMonitorOnOnActiveRetainerChanged;
        _inventoryMonitor.OnInventoryChanged -= InventoryMonitorOnOnInventoryChanged;
        _inventoryCountByType?.UnregisterFunc();
        _inventoryCountByTypes?.UnregisterFunc();
        _itemCount?.UnregisterFunc();
        _itemCountHQ?.UnregisterFunc();
        _itemCountOwned?.UnregisterFunc();
        _itemCountOwnedByCategory?.UnregisterFunc();
        _getItemCountsByCharacter?.UnregisterFunc();
        _enableUiFilter?.UnregisterFunc();
        _disableUiFilter?.UnregisterFunc();
        _toggleUiFilter?.UnregisterFunc();
        _enableBackgroundFilter?.UnregisterFunc();
        _disableBackgroundFilter?.UnregisterFunc();
        _toggleBackgroundFilter?.UnregisterFunc();
        _enableCraftList?.UnregisterFunc();
        _disableCraftList?.UnregisterFunc();
        _toggleCraftList?.UnregisterFunc();
        _addItemToCraftList?.UnregisterFunc();
        _removeItemFromCraftList?.UnregisterFunc();
        _getFilterItems?.UnregisterFunc();
        _getCraftItems?.UnregisterFunc();
        _getRetrievalItems?.UnregisterFunc();
        _getCraftLists?.UnregisterFunc();
        _getSearchFilters?.UnregisterFunc();
        _addNewCraftList?.UnregisterFunc();
        _currentCharacter?.UnregisterFunc();
        _getCharacterItems?.UnregisterFunc();
        _getCharacterItemsByType?.UnregisterFunc();
        _getCharactersOwnedByActive?.UnregisterFunc();
        _isInitialized?.UnregisterFunc();
        _logger.LogTrace("Stopped service {Type} ({This})", GetType().Name, this);
        return Task.CompletedTask;
    }
}
