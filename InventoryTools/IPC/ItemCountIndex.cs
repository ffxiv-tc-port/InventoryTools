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
/// ⇒ 對同一份清單，查這裡與逐件掃那份清單的結果相同；新清單裡同一個物件內容也沒變，所以可沿用上一份的彙總。
/// 結構＝一份「底」（某次整份重建）＋之後物件有變的持有者各自一小份；底裡變過或消失的持有者不採計。
/// 每個（物品、持有者、容器、分類、旗標）只有一組，組的先後順序不保證。
/// </summary>
internal sealed class ItemCountIndex
{
    public readonly struct ItemGroup(ulong retainerId, InventoryType container, InventoryCategory category, ItemFlags flags, int owner, long quantity)
    {
        public readonly ulong RetainerId = retainerId;
        public readonly InventoryType Container = container;
        public readonly InventoryCategory Category = category;
        public readonly ItemFlags Flags = flags;
        internal readonly int Owner = owner;
        public readonly long Quantity = quantity;
    }

    public readonly struct ContainerGroup(ulong retainerId, int owner, long quantity)
    {
        public readonly ulong RetainerId = retainerId;
        internal readonly int Owner = owner;
        public readonly long Quantity = quantity;
    }

    /// <summary>一批持有者的彙總。在底裡時各組的 Owner 是持有者序號，單一持有者那份為 -1。</summary>
    private sealed class Lookup(ItemGroup[] itemGroups, Dictionary<uint, (int Start, int Count)> itemRanges,
        ContainerGroup[] containerGroups, Dictionary<uint, (int Start, int Count)> containerRanges, int ownerCount)
    {
        public readonly ItemGroup[] ItemGroups = itemGroups;
        public readonly int OwnerCount = ownerCount;

        public ReadOnlySpan<ItemGroup> ForItem(uint itemId) =>
            itemRanges.TryGetValue(itemId, out var r) ? ItemGroups.AsSpan(r.Start, r.Count) : default;

        public ReadOnlySpan<ContainerGroup> ForContainer(uint container) =>
            containerRanges.TryGetValue(container, out var r) ? containerGroups.AsSpan(r.Start, r.Count) : default;
    }

    /// <summary>一個持有者在建彙總那一刻的物件（依清單順序），用來判斷新清單裡它有沒有變。</summary>
    private sealed class Owner(ModelItem[] items, int baseOrdinal, Lookup? own)
    {
        public readonly ModelItem[] Items = items;
        public readonly int BaseOrdinal = baseOrdinal;
        public readonly Lookup? Own = own;
    }

    /// <summary>建出這份索引的那份清單（參考相同＝索引仍是最新）。</summary>
    public readonly IEnumerable<ModelItem> Source;

    private readonly Lookup _base;
    private readonly bool[]? _baseLive;
    private readonly Lookup[] _overlay;

    private ItemCountIndex(IEnumerable<ModelItem> source, Lookup baseLookup, bool[]? baseLive, Lookup[] overlay)
    {
        Source = source;
        _base = baseLookup;
        _baseLive = baseLive;
        _overlay = overlay;
    }

    /// <summary>這個物品在各（角色或僱員、容器、分類、旗標）的數量；沒有就是空的。</summary>
    public ItemEnumerator ForItem(uint itemId) => new(this, itemId);

    /// <summary>這個容器（<c>(uint)SortedContainer</c>）裡各角色或僱員的數量合計（所有物品）。</summary>
    public ContainerEnumerator ForContainer(uint container) => new(this, container);

    public ref struct ItemEnumerator
    {
        private readonly ItemCountIndex _index;
        private readonly uint _key;
        private ReadOnlySpan<ItemGroup> _span;
        private int _pos;
        private int _part;

        internal ItemEnumerator(ItemCountIndex index, uint key)
        {
            _index = index;
            _key = key;
            _span = index._base.ForItem(key);
            _pos = -1;
            _part = -1;
        }

        public readonly ItemEnumerator GetEnumerator() => this;

        public readonly ref readonly ItemGroup Current => ref _span[_pos];

        public bool MoveNext()
        {
            while (true)
            {
                while (++_pos < _span.Length)
                {
                    if (_part >= 0 || _index._baseLive == null || _index._baseLive[_span[_pos].Owner]) return true;
                }

                if (_part + 1 >= _index._overlay.Length) return false;
                _span = _index._overlay[++_part].ForItem(_key);
                _pos = -1;
            }
        }
    }

    public ref struct ContainerEnumerator
    {
        private readonly ItemCountIndex _index;
        private readonly uint _key;
        private ReadOnlySpan<ContainerGroup> _span;
        private int _pos;
        private int _part;

        internal ContainerEnumerator(ItemCountIndex index, uint key)
        {
            _index = index;
            _key = key;
            _span = index._base.ForContainer(key);
            _pos = -1;
            _part = -1;
        }

        public readonly ContainerEnumerator GetEnumerator() => this;

        public readonly ref readonly ContainerGroup Current => ref _span[_pos];

        public bool MoveNext()
        {
            while (true)
            {
                while (++_pos < _span.Length)
                {
                    if (_part >= 0 || _index._baseLive == null || _index._baseLive[_span[_pos].Owner]) return true;
                }

                if (_part + 1 >= _index._overlay.Length) return false;
                _span = _index._overlay[++_part].ForContainer(_key);
                _pos = -1;
            }
        }
    }

    /// <summary>
    /// 暫存區重複使用，並記著上一份建好的索引；同一個 Builder 不可同時被兩條執行緒呼叫。
    /// 只重算物件有變的持有者；單獨成份的持有者太多或太大時整份重建成新的底。
    /// </summary>
    public sealed class Builder
    {
        private const int MaxOverlayOwners = 8;

        private readonly Dictionary<(uint ItemId, ulong RetainerId, InventoryType Container, InventoryCategory Category, ItemFlags Flags), long> _itemTotals = new();
        private readonly Dictionary<(uint Container, ulong RetainerId), long> _containerTotals = new();
        private uint[] _sortKeys = [];
        private readonly List<ModelItem> _copy = new();

        private ItemCountIndex? _last;
        private readonly Dictionary<ulong, Owner> _lastOwners = new();

        private readonly Dictionary<ulong, int> _slotOf = new();
        private Slot[] _slots = [];
        private int[] _itemSlot = [];

        private struct Slot
        {
            public ulong RetainerId;
            public Owner? Prev;
            public int Count;
            public bool Changed;
            public ModelItem[]? Items;
            public int Filled;
        }

        public ItemCountIndex Build(IEnumerable<ModelItem> source)
        {
            ReadOnlySpan<ModelItem> items;
            if (source is List<ModelItem> list)
            {
                items = CollectionsMarshal.AsSpan(list);
            }
            else
            {
                _copy.Clear();
                _copy.AddRange(source);
                items = CollectionsMarshal.AsSpan(_copy);
            }

            // 先作廢：中途擲例外時，下一次不得沿用只更新一半的持有者表。
            var last = _last;
            _last = null;
            try
            {
                var index = BuildCore(source, items, last);
                _last = index;
                return index;
            }
            finally
            {
                _copy.Clear();
                Array.Clear(_slots);
            }
        }

        private ItemCountIndex BuildCore(IEnumerable<ModelItem> source, ReadOnlySpan<ModelItem> items, ItemCountIndex? last)
        {
            var slotCount = ScanOwners(items, last != null);
            if (last == null) return BuildFull(source, items, slotCount);

            var anyChanged = false;
            var matched = 0;
            var overlayOwners = 0;
            long overlayItems = 0;
            for (var s = 0; s < slotCount; s++)
            {
                ref var slot = ref _slots[s];
                if (slot.Prev != null) matched++;
                anyChanged |= slot.Changed;
                if (slot.Changed || slot.Prev!.BaseOrdinal < 0)
                {
                    overlayOwners++;
                    overlayItems += slot.Count;
                }
            }

            if (!anyChanged && matched == _lastOwners.Count)
            {
                return new ItemCountIndex(source, last._base, last._baseLive, last._overlay);
            }

            if (overlayOwners > MaxOverlayOwners || overlayItems * 2 > last._base.ItemGroups.Length)
            {
                return BuildFull(source, items, slotCount);
            }

            return BuildIncremental(source, items, slotCount, last);
        }

        // 每個持有者一格（依第一次出現的順序），並逐件比對上一份同一持有者的物件。
        private int ScanOwners(ReadOnlySpan<ModelItem> items, bool compare)
        {
            _slotOf.Clear();
            if (_itemSlot.Length < items.Length) _itemSlot = new int[Math.Max(items.Length, _itemSlot.Length * 2)];
            var slotCount = 0;
            for (var i = 0; i < items.Length; i++)
            {
                var item = items[i];
                ref var idx = ref CollectionsMarshal.GetValueRefOrAddDefault(_slotOf, item.RetainerId, out var exists);
                if (!exists)
                {
                    idx = slotCount++;
                    if (_slots.Length < slotCount) Array.Resize(ref _slots, Math.Max(slotCount, _slots.Length * 2));
                    var prev = compare ? _lastOwners.GetValueOrDefault(item.RetainerId) : null;
                    _slots[idx] = new Slot { RetainerId = item.RetainerId, Prev = prev, Changed = prev == null };
                }

                _itemSlot[i] = idx;
                ref var slot = ref _slots[idx];
                if (!slot.Changed)
                {
                    var prevItems = slot.Prev!.Items;
                    if (slot.Count >= prevItems.Length || !ReferenceEquals(prevItems[slot.Count], item)) slot.Changed = true;
                }

                slot.Count++;
            }

            for (var s = 0; s < slotCount; s++)
            {
                ref var slot = ref _slots[s];
                if (!slot.Changed && slot.Count != slot.Prev!.Items.Length) slot.Changed = true;
            }

            return slotCount;
        }

        // 有變的持有者抄出物件；沒變的沿用上一份那個陣列（逐件相同）。
        private void CollectItems(ReadOnlySpan<ModelItem> items, int slotCount)
        {
            for (var s = 0; s < slotCount; s++)
            {
                ref var slot = ref _slots[s];
                slot.Items = slot.Changed ? new ModelItem[slot.Count] : slot.Prev!.Items;
            }

            for (var i = 0; i < items.Length; i++)
            {
                ref var slot = ref _slots[_itemSlot[i]];
                if (slot.Changed) slot.Items![slot.Filled++] = items[i];
            }
        }

        private ItemCountIndex BuildFull(IEnumerable<ModelItem> source, ReadOnlySpan<ModelItem> items, int slotCount)
        {
            CollectItems(items, slotCount);
            _itemTotals.Clear();
            _containerTotals.Clear();
            foreach (var item in items) Add(item);
            var lookup = Fill(slotCount, true);

            _lastOwners.Clear();
            for (var s = 0; s < slotCount; s++)
            {
                ref var slot = ref _slots[s];
                _lastOwners.Add(slot.RetainerId, new Owner(slot.Items!, s, null));
            }

            return new ItemCountIndex(source, lookup, null, []);
        }

        private ItemCountIndex BuildIncremental(IEnumerable<ModelItem> source, ReadOnlySpan<ModelItem> items, int slotCount, ItemCountIndex last)
        {
            CollectItems(items, slotCount);
            var baseLive = new bool[last._base.OwnerCount];
            var overlay = new List<Lookup>(MaxOverlayOwners);
            _lastOwners.Clear();
            for (var s = 0; s < slotCount; s++)
            {
                ref var slot = ref _slots[s];
                Owner owner;
                if (slot.Changed)
                {
                    _itemTotals.Clear();
                    _containerTotals.Clear();
                    foreach (var item in slot.Items!) Add(item);
                    owner = new Owner(slot.Items!, -1, Fill(1, false));
                }
                else
                {
                    owner = slot.Prev!;
                }

                if (owner.BaseOrdinal >= 0) baseLive[owner.BaseOrdinal] = true;
                else overlay.Add(owner.Own!);
                _lastOwners.Add(slot.RetainerId, owner);
            }

            return new ItemCountIndex(source, last._base, baseLive, overlay.ToArray());
        }

        private void Add(ModelItem item)
        {
            CollectionsMarshal.GetValueRefOrAddDefault(_itemTotals, (item.ItemId, item.RetainerId, item.SortedContainer, item.SortedCategory, item.Flags), out _) += item.Quantity;
            CollectionsMarshal.GetValueRefOrAddDefault(_containerTotals, ((uint)item.SortedContainer, item.RetainerId), out _) += item.Quantity;
        }

        private Lookup Fill(int ownerCount, bool withOrdinal)
        {
            var itemGroups = new ItemGroup[_itemTotals.Count];
            var keys = SortKeys(itemGroups.Length);
            var i = 0;
            foreach (var kv in _itemTotals)
            {
                keys[i] = kv.Key.ItemId;
                var owner = withOrdinal ? _slotOf[kv.Key.RetainerId] : -1;
                itemGroups[i] = new ItemGroup(kv.Key.RetainerId, kv.Key.Container, kv.Key.Category, kv.Key.Flags, owner, kv.Value);
                i++;
            }

            var keySpan = keys.AsSpan(0, itemGroups.Length);
            keySpan.Sort(itemGroups.AsSpan());
            var itemRanges = Ranges(keySpan);

            var containerGroups = new ContainerGroup[_containerTotals.Count];
            keys = SortKeys(containerGroups.Length);
            i = 0;
            foreach (var kv in _containerTotals)
            {
                keys[i] = kv.Key.Container;
                var owner = withOrdinal ? _slotOf[kv.Key.RetainerId] : -1;
                containerGroups[i] = new ContainerGroup(kv.Key.RetainerId, owner, kv.Value);
                i++;
            }

            keySpan = keys.AsSpan(0, containerGroups.Length);
            keySpan.Sort(containerGroups.AsSpan());
            return new Lookup(itemGroups, itemRanges, containerGroups, Ranges(keySpan), ownerCount);
        }

        private uint[] SortKeys(int count)
        {
            if (_sortKeys.Length < count) _sortKeys = new uint[Math.Max(count, _sortKeys.Length * 2)];
            return _sortKeys;
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
