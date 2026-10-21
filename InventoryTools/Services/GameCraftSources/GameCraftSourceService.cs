using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Component.GUI;
using InventoryItem = FFXIVClientStructs.FFXIV.Client.Game.InventoryItem;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace InventoryTools.Services.GameCraftSources;

public readonly struct GameCraftItem
{
    public GameCraftItem(uint itemId, uint quantity, InventoryItem.ItemFlags flags = InventoryItem.ItemFlags.None)
    {
        ItemId = itemId;
        Quantity = quantity;
        Flags = flags;
    }

    public uint ItemId { get; }
    public uint Quantity { get; }
    public InventoryItem.ItemFlags Flags { get; }
}

/// <summary>
/// 「從遊戲加入」選單的一項。GetItems 必須在按下的那一幀呼叫：它當場重查視窗／代理人，不保存任何原生指標。
/// </summary>
public class GameCraftCategory
{
    public GameCraftCategory(string name, Func<IReadOnlyList<GameCraftItem>> getItems)
    {
        Name = name;
        GetItems = getItems;
    }

    public string Name { get; }

    public Func<IReadOnlyList<GameCraftItem>> GetItems { get; }
}

/// <summary>契約：只在框架執行緒（繪製或框架更新）呼叫；每次呼叫都重查遊戲視窗。</summary>
public interface IGameCraftSource
{
    IReadOnlyList<GameCraftCategory> GetAvailableCategories();
}

public class GameCraftSourceService
{
    private readonly IEnumerable<IGameCraftSource> _sources;

    public GameCraftSourceService(IEnumerable<IGameCraftSource> sources)
    {
        _sources = sources;
    }

    public IReadOnlyList<GameCraftCategory> GetAvailableCategories()
    {
        List<GameCraftCategory>? categories = null;
        foreach (var source in _sources)
        {
            var found = source.GetAvailableCategories();
            if (found.Count == 0)
            {
                continue;
            }

            categories ??= new List<GameCraftCategory>();
            categories.AddRange(found);
        }

        return categories ?? (IReadOnlyList<GameCraftCategory>)Array.Empty<GameCraftCategory>();
    }
}

internal static class AtkValueReader
{
    /// <summary>Int／UInt 才回值（負的 Int 不算）；其他型別回 null。</summary>
    public static uint? GetUInt(AtkValue value)
    {
        return value.Type switch
        {
            ValueType.Int when value.Int >= 0 => (uint)value.Int,
            ValueType.UInt => value.UInt,
            _ => null,
        };
    }
}
