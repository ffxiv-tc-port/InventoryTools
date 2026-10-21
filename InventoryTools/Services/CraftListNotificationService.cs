using System;
using AllaganLib.GameSheets.Sheets;
using AllaganLib.Monitors.Enums;
using CriticalCommonLib.Services;
using Dalamud.Game.Text.SeStringHandling;
using InventoryTools.Logic;
using InventoryTools.Logic.Filters;
using InventoryTools.Logic.Filters.Abstract;
using Microsoft.Extensions.Logging;
using InventoryItem = FFXIVClientStructs.FFXIV.Client.Game.InventoryItem;

namespace InventoryTools.Services;

// 契約：由 PluginLogic 在「已確定這次取得要計入作用中製作清單」之後、MarkCrafted 之前呼叫（要拿扣帳前的缺少數量），
// 跑在追蹤器發事件的框架執行緒上。只印聊天與播音效，不改清單。
public class CraftListNotificationService
{
    private const int PrefixColor = 31;
    private const int ItemNameColor = 504;
    private const int AmountColor = 546;

    private readonly ILogger<CraftListNotificationService> _logger;
    private readonly IChatUtilities _chatUtilities;
    private readonly ItemSheet _itemSheet;
    private readonly IGameInteropService _gameInteropService;
    private readonly CraftReportProgressFilter _progressFilter;
    private readonly CraftReportCompletionOnlyFilter _completionOnlyFilter;
    private readonly CraftReportPlaySoundFilter _playSoundFilter;
    private readonly CraftReportSoundFilter _soundFilter;
    private readonly CraftReportPrefixFilter _prefixFilter;
    private readonly CraftReportTrackGatheringFilter _reportGatheringFilter;
    private readonly CraftReportTrackCraftingFilter _reportCraftingFilter;
    private readonly CraftReportTrackShoppingFilter _reportShoppingFilter;
    private readonly CraftReportTrackCombatDropFilter _reportCombatDropFilter;
    private readonly CraftReportTrackMarketBoardFilter _reportMarketBoardFilter;
    private readonly CraftReportTrackOtherFilter _reportOtherFilter;

    public CraftListNotificationService(ILogger<CraftListNotificationService> logger, IChatUtilities chatUtilities,
        ItemSheet itemSheet, IGameInteropService gameInteropService, CraftReportProgressFilter progressFilter,
        CraftReportCompletionOnlyFilter completionOnlyFilter, CraftReportPlaySoundFilter playSoundFilter,
        CraftReportSoundFilter soundFilter, CraftReportPrefixFilter prefixFilter,
        CraftReportTrackGatheringFilter reportGatheringFilter, CraftReportTrackCraftingFilter reportCraftingFilter,
        CraftReportTrackShoppingFilter reportShoppingFilter, CraftReportTrackCombatDropFilter reportCombatDropFilter,
        CraftReportTrackMarketBoardFilter reportMarketBoardFilter, CraftReportTrackOtherFilter reportOtherFilter)
    {
        _logger = logger;
        _chatUtilities = chatUtilities;
        _itemSheet = itemSheet;
        _gameInteropService = gameInteropService;
        _progressFilter = progressFilter;
        _completionOnlyFilter = completionOnlyFilter;
        _playSoundFilter = playSoundFilter;
        _soundFilter = soundFilter;
        _prefixFilter = prefixFilter;
        _reportGatheringFilter = reportGatheringFilter;
        _reportCraftingFilter = reportCraftingFilter;
        _reportShoppingFilter = reportShoppingFilter;
        _reportCombatDropFilter = reportCombatDropFilter;
        _reportMarketBoardFilter = reportMarketBoardFilter;
        _reportOtherFilter = reportOtherFilter;
    }

    private BooleanFilter? ReasonToggle(AcquisitionReason reason)
    {
        return reason switch
        {
            AcquisitionReason.Gathering => _reportGatheringFilter,
            AcquisitionReason.Crafting => _reportCraftingFilter,
            AcquisitionReason.Shopping => _reportShoppingFilter,
            AcquisitionReason.CombatDrop => _reportCombatDropFilter,
            AcquisitionReason.Marketboard => _reportMarketBoardFilter,
            AcquisitionReason.Other => _reportOtherFilter,
            _ => null,
        };
    }

    public void Notify(FilterConfiguration craftList, uint itemId, InventoryItem.ItemFlags itemFlags, uint qtyIncrease,
        AcquisitionReason reason)
    {
        if (qtyIncrease == 0 || _progressFilter.CurrentValue(craftList) != true)
        {
            return;
        }

        var reasonToggle = ReasonToggle(reason);
        if (reasonToggle == null || reasonToggle.CurrentValue(craftList) != true)
        {
            return;
        }

        var craftItem = craftList.CraftList.GetItemById(itemId, itemFlags);
        if (craftItem == null)
        {
            return;
        }

        var missing = craftItem.QuantityMissingOverall;
        var remaining = (uint)Math.Max(0, (long)missing - qtyIncrease);
        var complete = remaining == 0;
        if (!complete && _completionOnlyFilter.CurrentValue(craftList) == true)
        {
            return;
        }

        _logger.LogTrace("Reporting {ItemId} ({Reason}): missing {Missing}, complete={Complete}", itemId, reason, missing, complete);
        var itemName = _itemSheet.GetRowOrDefault(itemId)?.NameString ?? itemId.ToString();
        var amountText = complete ? "completed".Loc() : "?? remaining".Loc(remaining);
        PrintProgress(GetPrefix(craftList), itemName, amountText);

        if (complete && _playSoundFilter.CurrentValue(craftList) == true)
        {
            var soundId = _soundFilter.CurrentValue(craftList);
            if (soundId is >= CraftReportSoundFilter.MinSound and <= CraftReportSoundFilter.MaxSound)
            {
                _gameInteropService.PlayChatSoundEffect(soundId);
            }
        }
    }

    private string GetPrefix(FilterConfiguration list)
    {
        return _prefixFilter.CurrentValue(list) switch
        {
            CraftReportPrefix.PluginName => "[AT]",
            CraftReportPrefix.CraftListName => $"[{list.Name}]",
            _ => string.Empty,
        };
    }

    private void PrintProgress(string prefix, string itemName, string amount)
    {
        var builder = new SeStringBuilder();
        if (!string.IsNullOrEmpty(prefix))
        {
            ChatUtilities.AddColoredText(builder, prefix, PrefixColor).AddText(" ");
        }

        ChatUtilities.AddColoredText(builder, itemName, ItemNameColor).AddText(" ");
        ChatUtilities.AddColoredText(builder, amount, AmountColor);
        _chatUtilities.Print(builder.BuiltString);
    }
}
