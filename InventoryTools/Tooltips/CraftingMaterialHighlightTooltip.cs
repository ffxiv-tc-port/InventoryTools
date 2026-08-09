using System.Collections.Generic;
using System.Linq;
using AllaganLib.GameSheets.Sheets;
using CriticalCommonLib.Services;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using InventoryTools.Logic.Settings;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.Logging;

namespace InventoryTools.Tooltips;

/// <summary>
/// Recolours the game's own "crafting material" line in an item tooltip so it stands out.
/// </summary>
/// <remarks>
/// Unlike every other tweak in this folder this one does not append anything - it rewrites text
/// the game already put there. The phrase is read from the Addon sheet at row
/// <see cref="CraftingMaterialAddonRow"/> rather than written into the source, so it follows
/// whatever language the client is in; on TC that row is "\n製作用素材" (the leading newline is
/// part of the row, hence the Trim).
/// </remarks>
public class CraftingMaterialHighlightTooltip : BaseTooltip
{
    /// <summary>
    /// Addon row holding the "crafting material" phrase. Verified against the TC 7.20 Addon sheet
    /// dump (2026-08-09): row 996 = "\n製作用素材".
    /// </summary>
    private const uint CraftingMaterialAddonRow = 996;

    /// <summary>
    /// String array fields to search, in order. The phrase is composed into the tooltip by the
    /// game rather than stored in Item.Description, and which field it lands in is not something
    /// this plugin can pin down offline, so the whole plausible set is searched and the first
    /// field that actually contains it is the one rewritten. A field that is absent or too short
    /// is handled by GetTooltipString, which bounds-checks and returns null.
    /// </summary>
    private static readonly TooltipService.ItemTooltipField[] CandidateFields =
    [
        TooltipService.ItemTooltipField.ItemUiCategory,
        TooltipService.ItemTooltipField.ItemDescription,
        TooltipService.ItemTooltipField.Effects,
        TooltipService.ItemTooltipField.ExtractableProjectableDesynthesizable,
        TooltipService.ItemTooltipField.Param0,
        TooltipService.ItemTooltipField.Param1,
        TooltipService.ItemTooltipField.Param2,
        TooltipService.ItemTooltipField.Param3,
        TooltipService.ItemTooltipField.Param4,
        TooltipService.ItemTooltipField.Param5,
        TooltipService.ItemTooltipField.ControlsDisplay,
    ];

    private readonly TooltipCraftingMaterialHighlightColorSetting _colorSetting;
    private readonly TooltipCraftingMaterialHighlightSetting _enabledSetting;
    private readonly ShowTooltipsSetting _showTooltipsSetting;
    private readonly string _needle;

    public CraftingMaterialHighlightTooltip(ILogger<CraftingMaterialHighlightTooltip> logger,
        TooltipCraftingMaterialHighlightColorSetting colorSetting,
        TooltipCraftingMaterialHighlightSetting enabledSetting,
        ShowTooltipsSetting showTooltipsSetting,
        ExcelSheet<Addon> addonSheet,
        ItemSheet itemSheet,
        InventoryToolsConfiguration configuration,
        IGameGui gameGui,
        IChatGui chatGui) : base(6910, logger, itemSheet, configuration, gameGui, chatGui)
    {
        _colorSetting = colorSetting;
        _enabledSetting = enabledSetting;
        _showTooltipsSetting = showTooltipsSetting;
        _needle = addonSheet.GetRowOrDefault(CraftingMaterialAddonRow)?.Text.ExtractText().Trim() ?? "";

        // Information, not Debug: this is the one fact needed to tell "the setting is off" apart
        // from "the phrase never resolved", and users run at log level 2.
        if (_needle.Length == 0)
        {
            logger.LogInformation(
                "CraftingMaterialHighlightTooltip: Addon row {Row} is empty on this client, the crafting material highlight will do nothing.",
                CraftingMaterialAddonRow);
        }
        else
        {
            logger.LogInformation("CraftingMaterialHighlightTooltip: highlighting the phrase {Needle} (Addon row {Row}).",
                _needle, CraftingMaterialAddonRow);
        }
    }

    public override bool IsEnabled =>
        _needle.Length != 0 && _showTooltipsSetting.CurrentValue(Configuration) &&
        _enabledSetting.CurrentValue(Configuration);

    public override unsafe void OnGenerateItemTooltip(NumberArrayData* numberArrayData,
        StringArrayData* stringArrayData)
    {
        if (!ShouldShow()) return;
        if (HoverItem == null) return;

        foreach (var field in CandidateFields)
        {
            var seStr = GetTooltipString(stringArrayData, field);
            if (seStr == null || seStr.Payloads.Count == 0)
            {
                continue;
            }

            // Idempotence marker, the same one the appending tweaks use: the detour runs the
            // whole tweak list every time a tooltip is generated, and without this a second pass
            // would split the phrase out of its own payload and wrap it again.
            if (seStr.Payloads.Any(payload =>
                    payload is DalamudLinkPayload linkPayload && linkPayload.CommandId == TooltipIdentifier))
            {
                return;
            }

            if (!TryHighlight(seStr, out var rewritten))
            {
                continue;
            }

            rewritten.Payloads.Add(GetLinkPayload());
            rewritten.Payloads.Add(RawPayload.LinkTerminator);
            SetTooltipString(stringArrayData, field, rewritten);
            return;
        }
    }

    /// <summary>
    /// Rebuilds <paramref name="seStr"/> with the phrase wrapped in the configured colour.
    /// Returns false, leaving the string untouched, when the phrase is not in it.
    /// </summary>
    private bool TryHighlight(SeString seStr, out SeString rewritten)
    {
        rewritten = seStr;
        var colour = (ushort)(_colorSetting.CurrentValue(Configuration) ?? Configuration.TooltipColor ?? 1);
        var payloads = new List<Payload>();
        var matched = false;

        foreach (var payload in seStr.Payloads)
        {
            if (matched || payload is not TextPayload textPayload || textPayload.Text == null ||
                !textPayload.Text.Contains(_needle))
            {
                payloads.Add(payload);
                continue;
            }

            var text = textPayload.Text;
            var index = text.IndexOf(_needle, System.StringComparison.Ordinal);
            var before = text[..index];
            var after = text[(index + _needle.Length)..];

            if (before.Length != 0)
            {
                payloads.Add(new TextPayload(before));
            }

            payloads.Add(new UIForegroundPayload(colour));
            payloads.Add(new TextPayload(_needle));
            payloads.Add(new UIForegroundPayload(0));

            if (after.Length != 0)
            {
                payloads.Add(new TextPayload(after));
            }

            matched = true;
        }

        if (!matched)
        {
            return false;
        }

        rewritten = new SeString(payloads);
        return true;
    }

    public override uint Order => 1;
}
