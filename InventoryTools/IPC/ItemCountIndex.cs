using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using CriticalCommonLib.Enums;
using InventoryCategory = CriticalCommonLib.Models.InventoryCategory;
using ItemFlags = FFXIVClientStructs.FFXIV.Client.Game.InventoryItem.ItemFlags;
using ModelItem = CriticalCommonLib.Models.InventoryItem;

namespace InventoryTools.IPC;

/// <summary>
/// 一份 <c>IInventoryMonitor.AllItems</c> 清單的彙總，建好就不再改，可跨執行緒共讀。
/// 前提：AllItems 每次重算都換一份新清單，交出去的清單與其中物件之後不再被改
/// ⇒ 對同一份清單，查這裡與逐件掃那份清單的結果相同。
/// </summary>
internal sealed class ItemCountIndex
{
    public readonly struct ItemGroup(ulong retainerId, InventoryType container, InventoryCategory category, ItemFlags flags, long quantity)
    {
        public readonly ulong RetainerId = retainerId;
        public readonly InventoryType Container = container;
        public readonly InventoryCategory Category = category;
        public readonly ItemFlags Flags = flags;
        public readonly long Quantity = quantity;
    }

    public readonly struct ContainerGroup(ulong retainerId, long quantity)
    {
        public readonly ulong RetainerId = retainerId;
        public readonly long Quantity = quantity;
    }

    /// <summary>建出這份索引的那份清單（參考相同＝索引仍是最新）。</summary>
    public readonly IEnumerable<ModelItem> Source;

    private readonly ItemGroup[] _itemGroups;
    private readonly Dictionary<uint, (int Start, int Count)> _itemRanges;
    private readonly ContainerGroup[] _containerGroups;
    private readonly Dictionary<uint, (int Start, int Count)> _containerRanges;
    private readonly uint[] _containers;

    private ItemCountIndex(IEnumerable<ModelItem> source, ItemGroup[] itemGroups, Dictionary<uint, (int, int)> itemRanges,
        ContainerGroup[] containerGroups, Dictionary<uint, (int, int)> containerRanges, uint[] containers)
    {
        Source = source;
        _itemGroups = itemGroups;
        _itemRanges = itemRanges;
        _containerGroups = containerGroups;
        _containerRanges = containerRanges;
        _containers = containers;
    }

    /// <summary>這個物品在各（角色或僱員、容器、分類、旗標）的數量；沒有就是空的。</summary>
    public ReadOnlySpan<ItemGroup> ForItem(uint itemId) =>
        _itemRanges.TryGetValue(itemId, out var r) ? _itemGroups.AsSpan(r.Start, r.Count) : default;

    /// <summary>出現過的容器（以 <c>(uint)SortedContainer</c> 表示），每個一次。</summary>
    public ReadOnlySpan<uint> Containers => _containers;

    /// <summary>這個容器裡各角色或僱員的數量合計（所有物品）。</summary>
    public ReadOnlySpan<ContainerGroup> ForContainer(uint container) =>
        _containerRanges.TryGetValue(container, out var r) ? _containerGroups.AsSpan(r.Start, r.Count) : default;

    /// <summary>暫存字典重複使用；同一個 Builder 不可同時被兩條執行緒呼叫。</summary>
    public sealed class Builder
    {
        private readonly Dictionary<(uint ItemId, ulong RetainerId, InventoryType Container, InventoryCategory Category, ItemFlags Flags), long> _itemTotals = new();
        private readonly Dictionary<(uint Container, ulong RetainerId), long> _containerTotals = new();
        private uint[] _sortKeys = [];

        public ItemCountIndex Build(IEnumerable<ModelItem> source)
        {
            _itemTotals.Clear();
            _containerTotals.Clear();
            if (source is List<ModelItem> list)
            {
                foreach (var item in CollectionsMarshal.AsSpan(list)) Add(item);
            }
            else
            {
                foreach (var item in source) Add(item);
            }

            var itemGroups = new ItemGroup[_itemTotals.Count];
            var itemRanges = Fill(itemGroups);

            var containerGroups = new ContainerGroup[_containerTotals.Count];
            var containerRanges = FillContainers(containerGroups);
            var containers = new uint[containerRanges.Count];
            containerRanges.Keys.CopyTo(containers, 0);
            return new ItemCountIndex(source, itemGroups, itemRanges, containerGroups, containerRanges, containers);
        }

        private void Add(ModelItem item)
        {
            CollectionsMarshal.GetValueRefOrAddDefault(_itemTotals, (item.ItemId, item.RetainerId, item.SortedContainer, item.SortedCategory, item.Flags), out _) += item.Quantity;
            CollectionsMarshal.GetValueRefOrAddDefault(_containerTotals, ((uint)item.SortedContainer, item.RetainerId), out _) += item.Quantity;
        }

        private uint[] SortKeys(int count)
        {
            if (_sortKeys.Length < count) _sortKeys = new uint[Math.Max(count, _sortKeys.Length * 2)];
            return _sortKeys;
        }

        private Dictionary<uint, (int, int)> Fill(ItemGroup[] groups)
        {
            var keys = SortKeys(groups.Length);
            var i = 0;
            foreach (var kv in _itemTotals)
            {
                keys[i] = kv.Key.ItemId;
                groups[i] = new ItemGroup(kv.Key.RetainerId, kv.Key.Container, kv.Key.Category, kv.Key.Flags, kv.Value);
                i++;
            }

            var keySpan = keys.AsSpan(0, groups.Length);
            keySpan.Sort(groups.AsSpan());
            return Ranges(keySpan);
        }

        private Dictionary<uint, (int, int)> FillContainers(ContainerGroup[] groups)
        {
            var keys = SortKeys(groups.Length);
            var i = 0;
            foreach (var kv in _containerTotals)
            {
                keys[i] = kv.Key.Container;
                groups[i] = new ContainerGroup(kv.Key.RetainerId, kv.Value);
                i++;
            }

            var keySpan = keys.AsSpan(0, groups.Length);
            keySpan.Sort(groups.AsSpan());
            return Ranges(keySpan);
        }

        private static Dictionary<uint, (int, int)> Ranges(ReadOnlySpan<uint> sortedKeys)
        {
            var distinct = sortedKeys.IsEmpty ? 0 : 1;
            for (var i = 1; i < sortedKeys.Length; i++)
            {
                if (sortedKeys[i] != sortedKeys[i - 1]) distinct++;
            }

            var ranges = new Dictionary<uint, (int, int)>(distinct);
            var start = 0;
            for (var i = 1; i <= sortedKeys.Length; i++)
            {
                if (i == sortedKeys.Length || sortedKeys[i] != sortedKeys[start])
                {
                    ranges.Add(sortedKeys[start], (start, i - start));
                    start = i;
                }
            }

            return ranges;
        }
    }
}
