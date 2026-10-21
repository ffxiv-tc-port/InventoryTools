using System.Collections.Generic;
using AllaganLib.GameSheets.Sheets.Rows;
using CriticalCommonLib.Models;
using InventoryTools.Logic.Filters.Abstract;
using InventoryTools.Services;
using Microsoft.Extensions.Logging;

namespace InventoryTools.Logic.Filters;

// 製作清單收集進度通知的清單層設定。總開關 CraftReportProgress 預設關（沿用移植前「不在聊天印進度」的行為）；
// 其餘預設照上游。繼承 BooleanFilter 而非 GenericBooleanFilter：後者被 IFilter 註冊排除，不會出現在清單設定頁。
public abstract class CraftReportBooleanFilter : BooleanFilter
{
    protected CraftReportBooleanFilter(ILogger logger, ImGuiService imGuiService) : base(logger, imGuiService)
    {
    }

    public override FilterType AvailableIn { get; set; } = FilterType.CraftFilter;
    public override FilterCategory FilterCategory { get; set; } = FilterCategory.Notifications;

    public override bool? FilterItem(FilterConfiguration configuration, InventoryItem item)
    {
        return null;
    }

    public override bool? FilterItem(FilterConfiguration configuration, ItemRow item)
    {
        return null;
    }
}

public class CraftReportProgressFilter : CraftReportBooleanFilter
{
    public CraftReportProgressFilter(ILogger<CraftReportProgressFilter> logger, ImGuiService imGuiService) : base(logger, imGuiService)
    {
    }

    public override bool? DefaultValue { get; set; } = false;
    public override string Key { get; set; } = "CraftReportProgress";
    public override string Name { get; set; } = "Report acquisition progress to chat?".Loc();
    public override string HelpText { get; set; } = "When acquiring items that are part of this craft list, print progress (e.g. \"Ironwood Log 30 remaining\") to chat. The craft list must be active.";
}

public class CraftReportCompletionOnlyFilter : CraftReportBooleanFilter
{
    public CraftReportCompletionOnlyFilter(ILogger<CraftReportCompletionOnlyFilter> logger, ImGuiService imGuiService) : base(logger, imGuiService)
    {
    }

    public override bool? DefaultValue { get; set; } = false;
    public override string Key { get; set; } = "CraftReportCompletionOnly";
    public override string Name { get; set; } = "Only report when an item is complete?".Loc();
    public override string HelpText { get; set; } = "Instead of reporting every acquisition, only print a message when an item reaches its required quantity.";
}

public class CraftReportPlaySoundFilter : CraftReportBooleanFilter
{
    public CraftReportPlaySoundFilter(ILogger<CraftReportPlaySoundFilter> logger, ImGuiService imGuiService) : base(logger, imGuiService)
    {
    }

    public override bool? DefaultValue { get; set; } = false;
    public override string Key { get; set; } = "CraftReportPlaySound";
    public override string Name { get; set; } = "Play a sound when an item is complete?".Loc();
    public override string HelpText { get; set; } = "Play a sound effect when an item in this list reaches its required quantity.";
}

public class CraftReportTrackGatheringFilter : CraftReportBooleanFilter
{
    public CraftReportTrackGatheringFilter(ILogger<CraftReportTrackGatheringFilter> logger, ImGuiService imGuiService) : base(logger, imGuiService)
    {
    }

    public override bool? DefaultValue { get; set; } = true;
    public override string Key { get; set; } = "CraftReportTrackGathering";
    public override string Name { get; set; } = "Report gathered items?".Loc();
    public override string HelpText { get; set; } = "Report progress when you gather an item that is part of this craft list.";
}

public class CraftReportTrackCraftingFilter : CraftReportBooleanFilter
{
    public CraftReportTrackCraftingFilter(ILogger<CraftReportTrackCraftingFilter> logger, ImGuiService imGuiService) : base(logger, imGuiService)
    {
    }

    public override bool? DefaultValue { get; set; } = false;
    public override string Key { get; set; } = "CraftReportTrackCrafting";
    public override string Name { get; set; } = "Report crafted items?".Loc();
    public override string HelpText { get; set; } = "Report progress when you craft an item that is part of this craft list.";
}

public class CraftReportTrackShoppingFilter : CraftReportBooleanFilter
{
    public CraftReportTrackShoppingFilter(ILogger<CraftReportTrackShoppingFilter> logger, ImGuiService imGuiService) : base(logger, imGuiService)
    {
    }

    public override bool? DefaultValue { get; set; } = false;
    public override string Key { get; set; } = "CraftReportTrackShopping";
    public override string Name { get; set; } = "Report shopped items?".Loc();
    public override string HelpText { get; set; } = "Report progress when you buy an item that is part of this craft list.";
}

public class CraftReportTrackCombatDropFilter : CraftReportBooleanFilter
{
    public CraftReportTrackCombatDropFilter(ILogger<CraftReportTrackCombatDropFilter> logger, ImGuiService imGuiService) : base(logger, imGuiService)
    {
    }

    public override bool? DefaultValue { get; set; } = false;
    public override string Key { get; set; } = "CraftReportTrackCombatDrop";
    public override string Name { get; set; } = "Report combat drops?".Loc();
    public override string HelpText { get; set; } = "Report progress when an item that is part of this craft list drops from combat.";
}

public class CraftReportTrackMarketBoardFilter : CraftReportBooleanFilter
{
    public CraftReportTrackMarketBoardFilter(ILogger<CraftReportTrackMarketBoardFilter> logger, ImGuiService imGuiService) : base(logger, imGuiService)
    {
    }

    public override bool? DefaultValue { get; set; } = false;
    public override string Key { get; set; } = "CraftReportTrackMarketBoard";
    public override string Name { get; set; } = "Report market board purchases?".Loc();
    public override string HelpText { get; set; } = "Report progress when you buy an item that is part of this craft list from the market board.";
}

public class CraftReportTrackOtherFilter : CraftReportBooleanFilter
{
    public CraftReportTrackOtherFilter(ILogger<CraftReportTrackOtherFilter> logger, ImGuiService imGuiService) : base(logger, imGuiService)
    {
    }

    public override bool? DefaultValue { get; set; } = false;
    public override string Key { get; set; } = "CraftReportTrackOther";
    public override string Name { get; set; } = "Report other acquisitions?".Loc();
    public override string HelpText { get; set; } = "Report progress when an item that is part of this craft list is acquired through any other means.";
}

public enum CraftReportPrefix
{
    Blank = 0,
    PluginName = 1,
    CraftListName = 2
}

public class CraftReportPrefixFilter : ChoiceFilter<CraftReportPrefix>
{
    public CraftReportPrefixFilter(ILogger<CraftReportPrefixFilter> logger, ImGuiService imGuiService) : base(logger, imGuiService)
    {
    }

    public override CraftReportPrefix CurrentValue(FilterConfiguration configuration)
    {
        return (CraftReportPrefix)(configuration.GetUintFilter(Key) ?? (uint)DefaultValue);
    }

    public override void ResetFilter(FilterConfiguration configuration)
    {
        configuration.UpdateUintFilter(Key, (uint)DefaultValue);
    }

    public override void UpdateFilterConfiguration(FilterConfiguration configuration, CraftReportPrefix newValue)
    {
        configuration.UpdateUintFilter(Key, (uint)newValue);
    }

    public override string Key { get; set; } = "CraftReportPrefix";
    public override string Name { get; set; } = "Notification Prefix".Loc();
    public override string HelpText { get; set; } = "What to prefix each acquisition progress message with. 'Plugin Name' uses [AT], 'Craft List Name' uses the name of this craft list.";
    public override FilterCategory FilterCategory { get; set; } = FilterCategory.Notifications;
    public override CraftReportPrefix DefaultValue { get; set; } = CraftReportPrefix.Blank;
    public override FilterType AvailableIn { get; set; } = FilterType.CraftFilter;

    public override bool? FilterItem(FilterConfiguration configuration, InventoryItem item)
    {
        return null;
    }

    public override bool? FilterItem(FilterConfiguration configuration, ItemRow item)
    {
        return null;
    }

    public override List<CraftReportPrefix> GetChoices(FilterConfiguration configuration)
    {
        return [CraftReportPrefix.Blank, CraftReportPrefix.PluginName, CraftReportPrefix.CraftListName];
    }

    public override string GetFormattedChoice(FilterConfiguration filterConfiguration, CraftReportPrefix choice)
    {
        return choice switch
        {
            CraftReportPrefix.Blank => "None".Loc(),
            CraftReportPrefix.PluginName => "Plugin Name".Loc(),
            CraftReportPrefix.CraftListName => "Craft List Name".Loc(),
            _ => choice.ToString()
        };
    }
}

public class CraftReportSoundFilter : ChoiceFilter<uint>
{
    // 遊戲聊天音效 <se.1>～<se.16>；UIGlobals.PlayChatSoundEffect 對範圍外的值擲例外。
    public const uint MinSound = 1;
    public const uint MaxSound = 16;

    public CraftReportSoundFilter(ILogger<CraftReportSoundFilter> logger, ImGuiService imGuiService) : base(logger, imGuiService)
    {
    }

    public override uint CurrentValue(FilterConfiguration configuration)
    {
        return configuration.GetUintFilter(Key) ?? DefaultValue;
    }

    public override void ResetFilter(FilterConfiguration configuration)
    {
        configuration.UpdateUintFilter(Key, DefaultValue);
    }

    public override void UpdateFilterConfiguration(FilterConfiguration configuration, uint newValue)
    {
        configuration.UpdateUintFilter(Key, newValue);
    }

    public override string Key { get; set; } = "CraftReportSound";
    public override string Name { get; set; } = "Completion Sound".Loc();
    public override string HelpText { get; set; } = "Which sound effect to play when an item in this list reaches its required quantity (when 'Play a sound when an item is complete?' is enabled).";
    public override FilterCategory FilterCategory { get; set; } = FilterCategory.Notifications;
    public override uint DefaultValue { get; set; } = 5;
    public override FilterType AvailableIn { get; set; } = FilterType.CraftFilter;

    public override bool? FilterItem(FilterConfiguration configuration, InventoryItem item)
    {
        return null;
    }

    public override bool? FilterItem(FilterConfiguration configuration, ItemRow item)
    {
        return null;
    }

    public override List<uint> GetChoices(FilterConfiguration configuration)
    {
        var choices = new List<uint>();
        for (var i = MinSound; i <= MaxSound; i++)
        {
            choices.Add(i);
        }

        return choices;
    }

    public override string GetFormattedChoice(FilterConfiguration filterConfiguration, uint choice)
    {
        return "<se." + choice + ">";
    }
}
