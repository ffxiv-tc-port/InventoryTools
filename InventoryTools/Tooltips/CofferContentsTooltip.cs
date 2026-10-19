using System.Collections.Generic;
using System.Linq;
using AllaganLib.GameSheets.Sheets;
using CriticalCommonLib.Enums;
using CriticalCommonLib.Services;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using InventoryTools.Localizers;
using InventoryTools.Logic.Settings;
using Microsoft.Extensions.Logging;

namespace InventoryTools.Tooltips;

/// <summary>
/// Adds a line to a container item's tooltip saying which classes and jobs the things inside it
/// are for, so you can tell at a glance whether a coffer is worth opening on this character.
/// </summary>
public class CofferContentsTooltip : BaseTooltip
{
    private readonly TooltipCofferContentsColorSetting _colorSetting;
    private readonly TooltipDisplayCofferContentsSetting _enabledSetting;
    private readonly ShowTooltipsSetting _showTooltipsSetting;
    private readonly CofferContentLocalizer _cofferContentLocalizer;

    public CofferContentsTooltip(ILogger<CofferContentsTooltip> logger,
        TooltipCofferContentsColorSetting colorSetting,
        TooltipDisplayCofferContentsSetting enabledSetting,
        ShowTooltipsSetting showTooltipsSetting,
        CofferContentLocalizer cofferContentLocalizer,
        ItemSheet itemSheet,
        InventoryToolsConfiguration configuration,
        IGameGui gameGui,
        IChatGui chatGui) : base(6909, logger, itemSheet, configuration, gameGui, chatGui)
    {
        _colorSetting = colorSetting;
        _enabledSetting = enabledSetting;
        _showTooltipsSetting = showTooltipsSetting;
        _cofferContentLocalizer = cofferContentLocalizer;
    }

    public override bool IsEnabled =>
        _showTooltipsSetting.CurrentValue(Configuration) && _enabledSetting.CurrentValue(Configuration);

    public override unsafe void OnGenerateItemTooltip(NumberArrayData* numberArrayData,
        StringArrayData* stringArrayData)
    {
        if (!ShouldShow()) return;

        var item = HoverItem;
        if (item == null || !_cofferContentLocalizer.IsContainer(item))
        {
            return;
        }

        var classJobs = _cofferContentLocalizer.FormattedContainedClassJobs(item);
        if (classJobs == null)
        {
            return;
        }

        TooltipService.ItemTooltipField itemTooltipField = TooltipService.ItemTooltipField.ItemDescription;
        SeString? seStr = null;
        if (GetTooltipVisibility(ItemTooltipFieldVisibility.Description))
        {
            itemTooltipField = TooltipService.ItemTooltipField.ItemDescription;
            seStr = GetTooltipString(stringArrayData, itemTooltipField);
        }

        if (seStr == null && GetTooltipVisibility(ItemTooltipFieldVisibility.Effects))
        {
            itemTooltipField = TooltipService.ItemTooltipField.Effects;
            seStr = GetTooltipString(stringArrayData, itemTooltipField);
        }

        if (seStr == null && GetTooltipVisibility(ItemTooltipFieldVisibility.Levels))
        {
            itemTooltipField = TooltipService.ItemTooltipField.Levels;
            seStr = GetTooltipString(stringArrayData, itemTooltipField);
        }

        if (seStr == null)
        {
            return;
        }

        if (seStr.Payloads.Any(payload =>
                payload is DalamudLinkPayload linkPayload && linkPayload.CommandId == TooltipIdentifier))
        {
            return;
        }

        seStr.Payloads.Add(GetLinkPayload());
        seStr.Payloads.Add(RawPayload.LinkTerminator);

        var newText = "\n" + "Contents usable by: ??".Loc(classJobs);

        var lines = new List<Payload>()
        {
            new UIForegroundPayload((ushort)(_colorSetting.CurrentValue(Configuration) ?? Configuration.TooltipColor ?? 1)),
            new UIGlowPayload(0),
            new TextPayload(newText),
            new UIGlowPayload(0),
            new UIForegroundPayload(0),
        };
        foreach (var line in lines)
        {
            seStr.Payloads.Add(line);
        }

        SetTooltipString(stringArrayData, itemTooltipField, seStr);
    }

    public override uint Order => 1;
}
