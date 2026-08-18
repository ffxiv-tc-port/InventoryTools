using System.Collections.Generic;
using System.Linq;
using InventoryTools.Logic.Columns.Abstract;
using InventoryTools.Services;
using Microsoft.Extensions.Logging;

namespace InventoryTools.Logic.Columns;

public class OutdatedGearColumn : CheckboxColumn
{
    private readonly IGameInteropService _gameInteropService;
    private Dictionary<uint, short>? _jobClassLevels = null;

    public OutdatedGearColumn(ILogger<OutdatedGearColumn> logger, ImGuiService imGuiService, IGameInteropService gameInteropService) : base(logger, imGuiService)
    {
        _gameInteropService = gameInteropService;
    }

    public override string Name { get; set; } = "Outdated Gear?".Loc();
    public override float Width { get; set; } = 50;
    public override string HelpText { get; set; } = SharedText.OutdatedGear.Loc();
    public override ColumnCategory ColumnCategory { get; } = ColumnCategory.Tools;
    public override bool HasFilter { get; set; } = true;
    public override ColumnFilterType FilterType { get; set; } = ColumnFilterType.Boolean;

    private Dictionary<uint, short> GetClassJobLevels()
    {
        if (_jobClassLevels == null)
        {
            _jobClassLevels = _gameInteropService.GetClassJobLevels()?.ToDictionary(c => c.Key.RowId, c=> c.Value) ?? new();
        }

        return _jobClassLevels;
    }


    public override bool? CurrentValue(ColumnConfiguration columnConfiguration, SearchResult searchResult)
    {
        var item = searchResult.Item;
        var isOutdated = false;
        var jobClassLevels = GetClassJobLevels();

        if (item.ClassJobCategory != null)
        {
            int? lowestJobLevel = null;

            foreach (var job in item.ClassJobCategory.ClassJobIds)
            {
                if (jobClassLevels.TryGetValue(job, out var jobLevel))
                {
                    if (lowestJobLevel == null || lowestJobLevel > jobLevel)
                    {
                        lowestJobLevel = jobLevel;
                    }
                }
            }

            if (lowestJobLevel != null && lowestJobLevel > item.Base.LevelEquip)
            {
                isOutdated = true;
            }
        }
        else
        {
            return false;
        }

        return isOutdated;
    }

    public override void InvalidateSearchCache()
    {
        this._jobClassLevels = null;
    }
}