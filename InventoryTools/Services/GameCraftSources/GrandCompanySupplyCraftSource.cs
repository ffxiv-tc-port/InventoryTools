using System;
using System.Collections.Generic;
using AllaganLib.GameSheets.Sheets;
using CriticalCommonLib.Services.Ui;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Microsoft.Extensions.Logging;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace InventoryTools.Services.GameCraftSources;

/// <summary>
/// 軍隊籌備：籌備軍需品（製作）與籌備補給品（採集）。兩個來源：
/// ①軍隊籌備清單視窗（GrandCompanySupplyList）開著時讀 AgentGrandCompanySupply 的 ItemArray——
///   台服 7.25 填值函式反組譯：+0x68 陣列、+0x78 筆數、每筆 0xA0、+0x84 道具、+0x8C 位置、+0x90 需要數只寫低 16 位元。
/// ②時間表詳細視窗（ContentsInfoDetail）的 AtkValues：格號照上游、台服沒有驗到，
///   每格先驗型別、道具存在且可製作／可採集、數量 1～99，任何一格不符整組不提供。
/// </summary>
public class GrandCompanySupplyCraftSource : IGameCraftSource
{
    private const int FirstProvisioningPosition = 8;
    private const int FirstExpertDeliveryPosition = 11;
    private const int MaxAgentItems = 256;
    private const int MaxRequested = 99;

    private const int ContentsInfoDetailSupplyFirstItemIdIndex = 233;
    private const int ContentsInfoDetailSupplyLastItemIdIndex = 240;
    private const int ContentsInfoDetailSupplyRequestedOffset = 16;
    private const int ContentsInfoDetailProvisioningFirstItemIdIndex = 292;
    private const int ContentsInfoDetailProvisioningLastItemIdIndex = 294;
    private const int ContentsInfoDetailProvisioningRequestedOffset = 6;

    private readonly IGameUiManager _gameUiManager;
    private readonly ItemSheet _itemSheet;
    private readonly ILogger<GrandCompanySupplyCraftSource> _logger;
    private bool _loggedSupplyMismatch;
    private bool _loggedProvisioningMismatch;

    public GrandCompanySupplyCraftSource(IGameUiManager gameUiManager, ItemSheet itemSheet,
        ILogger<GrandCompanySupplyCraftSource> logger)
    {
        _gameUiManager = gameUiManager;
        _itemSheet = itemSheet;
        _logger = logger;
    }

    private string SupplyName => "Supply Missions".Loc();
    private string ProvisioningName => "Provisioning Missions".Loc();

    public unsafe IReadOnlyList<GameCraftCategory> GetAvailableCategories()
    {
        if (GetVisibleAddon("GrandCompanySupplyList") != null)
        {
            List<GameCraftCategory>? categories = null;
            if (GetAgentItems(0, FirstProvisioningPosition).Count > 0)
            {
                (categories ??= []).Add(new GameCraftCategory(SupplyName,
                    () => GetAgentItems(0, FirstProvisioningPosition)));
            }

            if (GetAgentItems(FirstProvisioningPosition, FirstExpertDeliveryPosition).Count > 0)
            {
                (categories ??= []).Add(new GameCraftCategory(ProvisioningName,
                    () => GetAgentItems(FirstProvisioningPosition, FirstExpertDeliveryPosition)));
            }

            if (categories != null)
            {
                return categories;
            }
        }

        if (GetVisibleAddon("ContentsInfoDetail") != null)
        {
            List<GameCraftCategory>? categories = null;
            if (GetSupplyDetailItems().Count > 0)
            {
                (categories ??= []).Add(new GameCraftCategory(SupplyName, GetSupplyDetailItems));
            }

            if (GetProvisioningDetailItems().Count > 0)
            {
                (categories ??= []).Add(new GameCraftCategory(ProvisioningName, GetProvisioningDetailItems));
            }

            if (categories != null)
            {
                return categories;
            }
        }

        return Array.Empty<GameCraftCategory>();
    }

    private unsafe AtkUnitBase* GetVisibleAddon(string name)
    {
        var addon = _gameUiManager.GetWindow(name);
        return addon != null && addon->IsVisible ? addon : null;
    }

    // 只在 GrandCompanySupplyList 開著時呼叫（代理人活著）；指標當場取、不保存。
    private unsafe IReadOnlyList<GameCraftItem> GetAgentItems(int minPosition, int maxPosition)
    {
        if (GetVisibleAddon("GrandCompanySupplyList") == null)
        {
            return Array.Empty<GameCraftItem>();
        }

        var agent = AgentGrandCompanySupply.Instance();
        if (agent == null || agent->ItemArray == null || agent->NumItems <= 0 || agent->NumItems > MaxAgentItems)
        {
            return Array.Empty<GameCraftItem>();
        }

        List<GameCraftItem>? items = null;
        for (var i = 0; i < agent->NumItems; i++)
        {
            var item = agent->ItemArray[i];
            var requested = (uint)(item.NumRequested & 0xFFFF);
            if (item.Position >= minPosition && item.Position < maxPosition && item.ItemId != 0 && requested > 0)
            {
                (items ??= []).Add(new GameCraftItem(item.ItemId, requested));
            }
        }

        return items ?? (IReadOnlyList<GameCraftItem>)Array.Empty<GameCraftItem>();
    }

    private IReadOnlyList<GameCraftItem> GetSupplyDetailItems()
    {
        return GetContentsInfoDetailItems(ContentsInfoDetailSupplyFirstItemIdIndex,
            ContentsInfoDetailSupplyLastItemIdIndex, ContentsInfoDetailSupplyRequestedOffset, true,
            ref _loggedSupplyMismatch);
    }

    private IReadOnlyList<GameCraftItem> GetProvisioningDetailItems()
    {
        return GetContentsInfoDetailItems(ContentsInfoDetailProvisioningFirstItemIdIndex,
            ContentsInfoDetailProvisioningLastItemIdIndex, ContentsInfoDetailProvisioningRequestedOffset, false,
            ref _loggedProvisioningMismatch);
    }

    private unsafe IReadOnlyList<GameCraftItem> GetContentsInfoDetailItems(int firstItemIdIndex, int lastItemIdIndex,
        int requestedOffset, bool crafted, ref bool loggedMismatch)
    {
        var addon = GetVisibleAddon("ContentsInfoDetail");
        if (addon == null || addon->AtkValues == null || addon->AtkValuesCount <= lastItemIdIndex + requestedOffset)
        {
            return Array.Empty<GameCraftItem>();
        }

        List<GameCraftItem>? items = null;
        for (var i = firstItemIdIndex; i <= lastItemIdIndex; i++)
        {
            var itemCell = addon->AtkValues[i];
            if (itemCell.Type == ValueType.Undefined)
            {
                continue;
            }

            var itemId = AtkValueReader.GetUInt(itemCell);
            if (itemId == 0)
            {
                continue;
            }

            var requested = AtkValueReader.GetUInt(addon->AtkValues[i + requestedOffset]);
            var row = itemId == null ? null : _itemSheet.GetRowOrDefault(itemId.Value);
            var obtainable = row != null && (crafted
                ? row.CanBeCrafted
                : row.ObtainedGathering || row.ObtainedFishing || row.ObtainedSpearFishing);
            if (!obtainable || requested is not (> 0 and <= MaxRequested))
            {
                if (!loggedMismatch)
                {
                    loggedMismatch = true;
                    _logger.LogInformation(
                        "Add From Game: ContentsInfoDetail cell {Index} does not look like a {Kind} item (type {Type}, item {ItemId}, requested {Requested}); this category is hidden.",
                        i, crafted ? "supply" : "provisioning", itemCell.Type, itemId, requested);
                }

                return Array.Empty<GameCraftItem>();
            }

            (items ??= []).Add(new GameCraftItem(itemId!.Value, requested!.Value));
        }

        return items ?? (IReadOnlyList<GameCraftItem>)Array.Empty<GameCraftItem>();
    }
}
