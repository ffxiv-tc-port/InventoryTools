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

    private static readonly Dictionary<uint, short> NoLevels = new();

    // 表格重整可能在背景執行緒：讀寫都經區域變數，別處剛好清掉欄位時才不會回 null。
    private Dictionary<uint, short> GetClassJobLevels()
    {
        var levels = _jobClassLevels;
        if (levels != null)
        {
            return levels;
        }

        var current = _gameInteropService.GetClassJobLevels();
        // 未登入不快取：快取了空表，登入後要等到下一次表格重整才看得到等級。
        if (current == null)
        {
            return NoLevels;
        }

        levels = current.ToDictionary(c => c.Key.RowId, c => c.Value);
        _jobClassLevels = levels;
        return levels;
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