using AllaganLib.GameSheets.Sheets.Rows;
using CriticalCommonLib.Models;
using CriticalCommonLib.Services;
using Dalamud.Plugin.Services;
using InventoryTools.Logic.Filters.Abstract;
using InventoryTools.Services;
using Microsoft.Extensions.Logging;

namespace InventoryTools.Logic.Filters
{
    public class AcquiredFilter : BooleanFilter
    {
        // API13 把 IClientState.LocalContentId 標為過時。這裡改用 ICharacterMonitor.LocalContentId，
        // 它在 CriticalCommonLib 裡就是 `=> _clientState.LocalContentId` 的純轉發屬性（同一個值，
        // 沒有快取），本 repo 其他地方（RetainerListOverlay、CharacterDebuggerPane）也是這樣取值。
        // CriticalCommonLib 是子模組、包裝層另案處理，故不在此改動它。
        private readonly ICharacterMonitor _characterMonitor;
        private readonly InventoryToolsConfiguration _configuration;
        private readonly IGameInterface _gameInterface;

        public AcquiredFilter(ILogger<AcquiredFilter> logger, ImGuiService imGuiService, ICharacterMonitor characterMonitor, InventoryToolsConfiguration configuration) : base(logger, imGuiService)
        {
            _characterMonitor = characterMonitor;
            _configuration = configuration;
        }
        public override string Key { get; set; } = "Acquired";
        public override string Name { get; set; } = "Is Acquired?".Loc();
        public override string HelpText { get; set; } = "Has this item be acquired by your active character?".Loc();
        public override FilterCategory FilterCategory { get; set; } = FilterCategory.Acquisition;

        public override bool? FilterItem(FilterConfiguration configuration,InventoryItem item)
        {

            return FilterItem(configuration, item.Item);
        }

        public override bool? FilterItem(FilterConfiguration configuration, ItemRow item)
        {
            var currentValue = CurrentValue(configuration);
            if (currentValue == null)
            {
                return true;
            }
            var action = item.Base.ItemAction.ValueNullable;
            if (!ActionTypeExt.IsValidAction(action)) {
                return false;
            }

            var isUnlocked = false;
            if(this._configuration.AcquiredItems.TryGetValue(_characterMonitor.LocalContentId, out var value))
            {
                if (value.Contains(item.RowId))
                {
                    isUnlocked = true;
                }
            }

            return currentValue.Value && isUnlocked || !currentValue.Value && !isUnlocked;
        }
    }
}