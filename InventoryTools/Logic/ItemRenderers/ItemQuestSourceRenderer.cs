using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AllaganLib.GameSheets.Caches;
using AllaganLib.GameSheets.ItemSources;
using AllaganLib.GameSheets.Sheets;
using AllaganLib.Shared.Extensions;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Dalamud.Bindings.ImGui;
using Lumina.Excel.Sheets;
using LuminaSupplemental.Excel.Model;

namespace InventoryTools.Logic.ItemRenderers;

public class ItemQuestUseRenderer : ItemQuestSourceRenderer
{
    public ItemQuestUseRenderer(ITextureProvider textureProvider, ItemSheet itemSheet, MapSheet mapSheet,
        List<FestivalName> festivalNames, IDalamudPluginInterface dalamudPluginInterface) : base(textureProvider, itemSheet, mapSheet, festivalNames, dalamudPluginInterface)
    {
    }

    public override string HelpText { get; } = "Is this item required for a quest?".Loc();

    public override RendererType RendererType { get; } = RendererType.Use;
}

public class ItemQuestSourceRenderer : ItemInfoRenderer<ItemQuestSource>
{
    private readonly ITextureProvider _textureProvider;
    private readonly ItemSheet _itemSheet;
    private readonly Dictionary<uint,string> _festivalNames;
    public override RendererType RendererType { get; } = RendererType.Source;
    public override ItemInfoType Type { get; } = ItemInfoType.Quest;
    public override string SingularName { get; } = "Quest".Loc();
    public override string HelpText { get; } = "Does this item come from a quest?".Loc();
    public override bool ShouldGroup { get; } = true;

    public ItemQuestSourceRenderer(ITextureProvider textureProvider, ItemSheet itemSheet, MapSheet mapSheet,
        List<FestivalName> festivalNames, IDalamudPluginInterface dalamudPluginInterface) : base(textureProvider, dalamudPluginInterface, itemSheet, mapSheet)
    {
        _textureProvider = textureProvider;
        _itemSheet = itemSheet;
        _festivalNames = festivalNames.ToDictionary(c => c.FestivalId, c => c.Name);
    }

    public override Action<ItemSource> DrawTooltip => source =>
    {
        var asSource = AsSource(source);
        var quest = asSource.Quest.Value;

        var questName = quest.Name.ToImGuiString();
        ImGui.Text("Name: ".Loc() + questName);
        ImGui.Text("Expansion: ".Loc() + quest.Expansion.Value.Name.ToImGuiString());
        if (quest.BeastTribe.RowId != 0)
        {
            ImGui.Text("Allied Society: ".Loc() + quest.BeastTribe.Value.Name.ToImGuiString());
        }
        if (quest.Festival.RowId != 0 && _festivalNames.ContainsKey(quest.Festival.RowId))
        {
            ImGui.PushTextWrapPos();
            ImGui.Text("Only available from ".Loc() + _festivalNames[quest.Festival.RowId]);
            ImGui.PopTextWrapPos();
        }

        DrawItems("Required Items: ".Loc(), asSource.CostItems);
        DrawItems("Rewards: ".Loc(), asSource.RewardItems);
    };

    public override Func<ItemSource, string> GetName => source =>
    {
        var asSource = AsSource(source);
        var questName = asSource.Quest.Value.Name.ToImGuiString();
        return questName;
    };
    public override Func<ItemSource, int> GetIcon => source =>
    {
        var asSource = AsSource(source);
        return (int)asSource.QuestIcon;
    };
    public override Func<ItemSource, string> GetDescription => source =>
    {
        var asSource = AsSource(source);
        var questName = asSource.Quest.Value.Name.ToImGuiString();
        var expansionName = asSource.Quest.Value.Expansion.Value.Name.ToImGuiString();
        return questName + " (" + expansionName + ")";
    };
}