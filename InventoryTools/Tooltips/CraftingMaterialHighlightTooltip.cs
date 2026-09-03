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
    /// dump (2026-08-09): row 996 = "\n製作用素材". Row 997 is the matching "\n製作用觸媒"
    /// (crafting catalyst) and both sit in the run of item-tooltip attribute lines around
    /// 991 效果 / 995 簡易修理, which is why the leading newline is there: the game appends them
    /// to an existing field rather than giving them one of their own.
    /// </summary>
    private const uint CraftingMaterialAddonRow = 996;

    /// <summary>
    /// Field 0 is the item name and field 1 the glamour name; recolouring either would be wrong,
    /// so the scan starts after them.
    /// </summary>
    private const int FirstScannedField = 2;

    private readonly TooltipCraftingMaterialHighlightColorSetting _colorSetting;
    private readonly TooltipCraftingMaterialHighlightSetting _enabledSetting;
    private readonly ShowTooltipsSetting _showTooltipsSetting;
    private readonly string _needle;
    private bool _loggedActive;
    private bool _loggedMatch;

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

        // Information, not Debug: users run at log level 1, so Debug is captured but drowned
        // by the 100k+ Debug lines a single log file holds. Note this fires from the constructor,
        // which happens whether or not the setting is on - so on its own it only proves the Addon
        // row resolved, NOT that the tweak ever ran. The pair of one-shot logs below is what
        // distinguishes those two.
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

        // TooltipService.ItemTooltipField is byte-backed and SetTooltipString only accepts it, so
        // the scan cannot go past 255 without the cast wrapping around and writing to the wrong
        // field. The item tooltip's array is far smaller than that, but silently corrupting a
        // different string is not a failure worth risking on an assumption.
        var fieldCount = System.Math.Min(stringArrayData->AtkArrayData.Size, 256);

        // Diagnostic one of two. Fires the first time the tweak actually runs, which only happens
        // once both the master tooltip switch and this module's own setting are on. If this line
        // is absent from a log, the module was never enabled and nothing below is worth
        // investigating.
        if (!_loggedActive)
        {
            _loggedActive = true;
            Logger.LogInformation(
                "CraftingMaterialHighlightTooltip: active, scanning tooltip string fields {First}..{Last} for {Needle}.",
                FirstScannedField, fieldCount - 1, _needle);
        }

        for (var field = FirstScannedField; field < fieldCount; field++)
        {
            var seStr = GetTooltipString(stringArrayData, field);
            if (seStr == null || seStr.Payloads.Count == 0)
            {
                continue;
            }

            // Idempotence marker, the same one the appending tweaks use: the detour runs the
            // whole tweak list every time a tooltip is generated, and without this a second pass
            // would split the phrase out of its own payload and wrap it again. Checked before the
            // phrase test because after a rewrite the field still contains the phrase.
            if (seStr.Payloads.Any(payload =>
                    payload is DalamudLinkPayload linkPayload && linkPayload.CommandId == TooltipIdentifier))
            {
                return;
            }

            if (!TryHighlight(seStr, out var rewritten))
            {
                continue;
            }

            // Diagnostic two of two. Fires once, on the first item that actually carries the
            // phrase, and names the field it was in. "active" without this line means the scan
            // is running but never matching; both lines present means the rewrite happened and
            // anything still wrong is visual.
            if (!_loggedMatch)
            {
                _loggedMatch = true;
                Logger.LogInformation(
                    "CraftingMaterialHighlightTooltip: found {Needle} in tooltip string field {Field}; recolouring it from here on.",
                    _needle, field);
            }

            rewritten.Payloads.Add(GetLinkPayload());
            rewritten.Payloads.Add(RawPayload.LinkTerminator);
            SetTooltipString(stringArrayData, (TooltipService.ItemTooltipField)field, rewritten);
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
