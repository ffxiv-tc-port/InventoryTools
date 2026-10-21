using System;
using System.Collections.Generic;
using CriticalCommonLib.Addons;
using CriticalCommonLib.Services.Ui;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace InventoryTools.Services.GameCraftSources;

/// <summary>
/// 公會工坊合建零件選單（SubmarinePartsMenu）。台服 7.25 的排法（OnRefresh／ReceiveEvent 反組譯）：
/// [11]＝零件數，第 i 個零件 [12+i] 道具、[60+i] 每組數量、[108+i] 已交組數、[120+i] 需要組數。
/// 讀之前先驗格數與型別，對不上就不提供這一項。
/// </summary>
public class SubmarinePartsCraftSource : IGameCraftSource
{
    private const int PartCountIndex = 11;
    private const int MaxParts = 6;
    private const int LastIndexRead = 120 + MaxParts - 1;

    private readonly IGameUiManager _gameUiManager;

    public SubmarinePartsCraftSource(IGameUiManager gameUiManager)
    {
        _gameUiManager = gameUiManager;
    }

    public unsafe IReadOnlyList<GameCraftCategory> GetAvailableCategories()
    {
        if (!_gameUiManager.IsWindowVisible(WindowName.SubmarinePartsMenu) || !HasExpectedShape(GetAddon()))
        {
            return Array.Empty<GameCraftCategory>();
        }

        return [new GameCraftCategory("Company Workshop Parts".Loc(), GetItems)];
    }

    private unsafe AtkUnitBase* GetAddon()
    {
        var addon = _gameUiManager.GetWindow("SubmarinePartsMenu");
        return addon != null && addon->IsVisible ? addon : null;
    }

    private static unsafe bool HasExpectedShape(AtkUnitBase* addon)
    {
        return addon != null && addon->AtkValues != null && addon->AtkValuesCount > LastIndexRead
               && AtkValueReader.GetUInt(addon->AtkValues[PartCountIndex]) != null;
    }

    private unsafe IReadOnlyList<GameCraftItem> GetItems()
    {
        var items = new List<GameCraftItem>();
        var addon = GetAddon();
        if (!HasExpectedShape(addon))
        {
            return items;
        }

        var partCount = Math.Min(AtkValueReader.GetUInt(addon->AtkValues[PartCountIndex]) ?? 0, MaxParts);
        var menu = (SubmarinePartsMenuAddon*)addon;
        for (byte i = 0; i < partCount; i++)
        {
            var part = menu->GetItem(i);
            if (part == null || part.Value.ItemId == 0 || part.Value.SetsRequired < part.Value.SetsSubmitted)
            {
                continue;
            }

            var remaining = part.Value.QtyRemaining;
            if (remaining > 0)
            {
                items.Add(new GameCraftItem(part.Value.ItemId, remaining));
            }
        }

        return items;
    }
}
