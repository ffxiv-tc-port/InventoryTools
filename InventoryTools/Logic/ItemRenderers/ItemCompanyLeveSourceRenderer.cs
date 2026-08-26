using System;
using System.Collections.Generic;
using AllaganLib.GameSheets.Caches;
using AllaganLib.GameSheets.ItemSources;
using AllaganLib.GameSheets.Sheets;
using CriticalCommonLib.Models;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;

namespace InventoryTools.Logic.ItemRenderers;

public class ItemCompanyLeveSourceRenderer : ItemInfoRenderer<ItemCompanyLeveSource>
{
    public ItemCompanyLeveSourceRenderer(ItemSheet itemSheet, MapSheet mapSheet, ITextureProvider textureProvider,
        IDalamudPluginInterface dalamudPluginInterface) : base(textureProvider, dalamudPluginInterface, itemSheet, mapSheet)
    {
    }

    public override RendererType RendererType => RendererType.Source;
    public override ItemInfoType Type => ItemInfoType.CompanyLeve;
    public override string SingularName => "Company Leve".Loc();
    public override string PluralName => "Company Leves".Loc();
    public override string HelpText => "Is this item obtained from a company leve?".Loc();
    public override bool ShouldGroup => true;
    public override IReadOnlyList<ItemInfoRenderCategory> Categories => [ItemInfoRenderCategory.Leve];
    public override Action<ItemSource> DrawTooltip => source =>
    {
        var asSource = AsSource(source);
        var leveRow = asSource.Leve.Value;
        ImGui.TextUnformatted("Leve: ".Loc() + leveRow.Name.ExtractText());
        ImGui.TextUnformatted("Class: ".Loc() + leveRow.ClassJobCategory.Value.Name.ExtractText());
        ImGui.TextUnformatted("EXP Reward: ".Loc() + asSource.ExpReward);
        ImGui.TextUnformatted("Seals Rewarded: ".Loc() + asSource.SealsRewarded);
        ImGui.TextUnformatted("Allowance Cost: ".Loc() + leveRow.AllowanceCost);
    };

    public override Func<ItemSource, string> GetName => source =>
    {
        var asSource = AsSource(source);
        var leveRow = asSource.Leve.Value;
        return leveRow.Name.ExtractText();
    };

    public override Func<ItemSource, int> GetIcon => _ => Icons.LeveIcon;

    public override Func<ItemSource, string> GetDescription => source =>
    {
        var asSource = AsSource(source);
        var leveRow = asSource.Leve.Value;
        return
            "?? (??) (?? xp) (?? allowances)".Loc(leveRow.Name.ExtractText(), leveRow.ClassJobCategory.Value.Name.ExtractText(), leveRow.ExpReward, leveRow.AllowanceCost);
    };
}