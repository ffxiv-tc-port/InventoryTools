using System;
using System.Collections.Generic;
using System.Linq;
using InventoryTools.Services;
using Microsoft.Extensions.Logging;
using CclInventoryItem = CriticalCommonLib.Models.InventoryItem;

namespace InventoryTools
{
    // inventories.csv 的節流存檔：變動只記髒，FrameworkOnUpdate 每幀問節流器該不該寫；登出／換角與卸載必寫。
    public partial class PluginLogic
    {
        /// <summary>
        /// 卸載時等「正在寫的那一份」加重試的上界；逾時就放棄最後一次寫入，正式檔仍是完整的舊內容。
        /// 卸載在框架執行緒上同步跑，這段時間遊戲畫面是停住的 ⇒ 上界壓在 2 秒。
        /// </summary>
        private static readonly TimeSpan StopFlushMaxWait = TimeSpan.FromSeconds(2);

        private readonly ThrottledSnapshotSaver<List<CclInventoryItem>> _inventorySaver;

        private ThrottledSnapshotSaver<List<CclInventoryItem>> CreateInventorySaver()
        {
            return new ThrottledSnapshotSaver<List<CclInventoryItem>>(
                new InventorySaveThrottle(),
                SnapshotAllItems,
                _configurationManagerService.TrySaveInventories,
                OnInventorySaveFailed);
        }

        // InventoryMonitor 每次重算都換一份新清單、交出去的不再改 ⇒ 直接拿它當快照，不在框架執行緒上另抄一份。
        private List<CclInventoryItem> SnapshotAllItems()
        {
            var items = _inventoryMonitor.AllItems;
            return items as List<CclInventoryItem> ?? items.ToList();
        }

        private void TickInventorySave()
        {
            _inventorySaver.Tick();
            if (_inventorySaver.Throttle.TryTakeReport(false, out var line))
            {
                _logger.LogInformation("{InvSaveDiag}", line);
            }
        }

        private void CharacterMonitorOnCharacterSwitched(ulong characterId)
        {
            _inventorySaver.RequestFlush();
        }

        private void FlushInventoriesOnStop()
        {
            _inventorySaver.FlushOnStop(StopFlushMaxWait);
            if (_inventorySaver.Throttle.TryTakeReport(true, out var line))
            {
                _logger.LogInformation("{InvSaveDiag}", line);
            }
        }

        // 可能在執行緒池上；consecutiveFailures 為 0＝卸載時那一次。持續失敗只在第 1 次與第 3 次各記一行。
        private void OnInventorySaveFailed(Exception error, int consecutiveFailures)
        {
            if (consecutiveFailures == 0)
            {
                _logger.LogWarning("[InvSaveDiag] inventories.csv was not saved on unload, the previous file is kept: {Type}: {Message}", error.GetType().Name, error.Message);
            }
            else if (consecutiveFailures == 1)
            {
                _logger.LogInformation("[InvSaveDiag] inventories.csv save failed, the previous file is kept and the save will be retried: {Type}: {Message}", error.GetType().Name, error.Message);
            }
            else if (consecutiveFailures == 3)
            {
                _logger.LogWarning("[InvSaveDiag] inventories.csv save keeps failing ({Count} in a row), still retrying: {Type}: {Message}", consecutiveFailures, error.GetType().Name, error.Message);
            }
        }
    }
}
