using System;
using System.Globalization;
using System.Linq;
using AllaganLib.GameSheets.Caches;
using AllaganLib.GameSheets.ItemSources;
using AllaganLib.GameSheets.Sheets;
using CriticalCommonLib.Models;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;

namespace InventoryTools.Logic.ItemRenderers;

public class ItemCashShopSourceRenderer : ItemInfoRenderer<ItemCashShopSource>
{
    public ItemCashShopSourceRenderer(ItemSheet itemSheet, MapSheet mapSheet, ITextureProvider textureProvider,
        IDalamudPluginInterface dalamudPluginInterface) : base(textureProvider, dalamudPluginInterface, itemSheet, mapSheet)
    {
    }

    public override RendererType RendererType => RendererType.Source;
    public override ItemInfoType Type => ItemInfoType.CashShop;
    public override string SingularName => "Bought on SQ Store(real money)".Loc();
    public override bool ShouldGroup => true;
    public override string HelpText => "Can the item be purchased through the mogstation?".Loc();

    public override Action<ItemSource> DrawTooltip => source =>
    {
        var asSource = AsSource(source);
        var priceUsd = asSource.PriceUsd.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
        ImGui.TextUnformatted("Price(USD): ??".Loc(priceUsd));
        if (asSource.FittingShopItemSetRow?.Items.Count > 1)
        {
            ImGui.TextUnformatted("Set: ??".Loc(asSource.FittingShopItemSetRow.Base.Unknown6.ExtractText()));
            ImGui.TextUnformatted("Contains:".Loc());
            using (ImRaii.PushIndent())
            {
                foreach (var item in asSource.FittingShopItemSetRow.Items)
                {
                    ImGui.TextUnformatted(item.NameString);
                }
            }
        }
    };

    public override Func<ItemSource, string> GetName => source =>
    {
        var asSource = AsSource(source);
        return (asSource.FittingShopItemSetRow?.Base.Unknown6.ExtractText() ?? "Not in a set".Loc());
    };

    public override Func<ItemSource, int> GetIcon => source => Icons.BagStar;

    public override Func<ItemSource, string> GetDescription => source =>
    {
        var asSource = AsSource(source);
        var priceUsd = asSource.PriceUsd.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
        var description = "Price(USD): ??".Loc(priceUsd);
        if (asSource.FittingShopItemSetRow != null)
        {
            description += " (Part of ?? set)".Loc(asSource.FittingShopItemSetRow.Base.Unknown6.ExtractText());
            description += $" (Contains {String.Join(", ", asSource.FittingShopItemSetRow.Items.Select(c => c.NameString))}";
        }
        return description;
    };
}