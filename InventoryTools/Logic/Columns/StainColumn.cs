using System.Collections.Generic;
using CriticalCommonLib.Services.Mediator;
using DalaMock.Host.Mediator;
using Dalamud.Bindings.ImGui;
using InventoryTools.Logic.Columns.Abstract;
using InventoryTools.Logic.Columns.ColumnSettings;
using InventoryTools.Services;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.Logging;

namespace InventoryTools.Logic.Columns;

public class StainColumn : TextColumn
{
    private readonly StainColumnSetting _stainColumnSetting;

    public StainColumn(ILogger<StainColumn> logger, StainColumnSetting stainColumnSetting, ImGuiService imGuiService) : base(logger, imGuiService)
    {
        _stainColumnSetting = stainColumnSetting;
    }
    public override ColumnCategory ColumnCategory => ColumnCategory.Basic;
    public override string? CurrentValue(ColumnConfiguration columnConfiguration, SearchResult searchResult)
    {
        if (searchResult.InventoryItem == null)
        {
            return null;
        }

        var item = searchResult.InventoryItem;

        switch (_stainColumnSetting.CurrentValue(columnConfiguration))
        {
            case StainColumnSettingEnum.FirstStain:
                return StainName(item.StainEntry) ?? "";
            case StainColumnSettingEnum.SecondStain:
                return StainName(item.Stain2Entry) ?? "";
            case StainColumnSettingEnum.Both:
                var firstStain = StainName(item.StainEntry) ?? "No Dye";
                var secondStain = StainName(item.Stain2Entry) ?? "No Dye";
                return firstStain + " / " + secondStain;
        }

        return "";
    }

    // Stain.Name 是句中用的名稱欄，台服 row 0 為空字串，「無染色」只在 Name2。
    private static string? StainName(Stain? stain)
    {
        if (stain == null)
        {
            return null;
        }

        var name = stain.Value.Name.ExtractText();
        return name.Length != 0 ? name : stain.Value.Name2.ExtractText();
    }

    public override List<MessageBase>? DrawEditor(ColumnConfiguration columnConfiguration,
        FilterConfiguration configuration)
    {
        ImGui.NewLine();
        ImGui.Separator();
        _stainColumnSetting.Draw(columnConfiguration, null);
        base.DrawEditor(columnConfiguration, configuration);
        return null;
    }

    public override string Name { get; set; } = "Dye".Loc();
    public override float Width { get; set; } = 100;
    public override string HelpText { get; set; } = "The current dye of the item".Loc();
    public override bool HasFilter { get; set; } = true;
    public override ColumnFilterType FilterType { get; set; } = ColumnFilterType.Text;
}