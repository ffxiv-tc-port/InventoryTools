using System.Collections.Generic;
using System.Linq;
using CriticalCommonLib.MarketBoard;
using CriticalCommonLib.Services;
using InventoryTools.Services.Interfaces;

namespace InventoryTools.Services;

public class MarketBoardService : IMarketBoardService
{
    private readonly ICharacterMonitor _characterMonitor;
    private readonly InventoryToolsConfiguration _configuration;
    private readonly UniversalisAvailability _universalisAvailability;

    public MarketBoardService(ICharacterMonitor characterMonitor, InventoryToolsConfiguration configuration, UniversalisAvailability universalisAvailability)
    {
        _characterMonitor = characterMonitor;
        _configuration = configuration;
        _universalisAvailability = universalisAvailability;
    }
    
    public List<uint> GetDefaultWorlds()
    {
        var useActiveWorld = _configuration.MarketBoardUseActiveWorld;
        var useHomeWorld = _configuration.MarketBoardUseHomeWorld;
        HashSet<uint> worldIds = _configuration.MarketBoardWorldIds.ToHashSet();

        var activeCharacter = _characterMonitor.ActiveCharacter;
        if (activeCharacter != null)
        {
            if (useActiveWorld && activeCharacter.ActiveWorldId != 0)
            {
                worldIds.Add(activeCharacter.ActiveWorldId);
            }
            if (useHomeWorld && activeCharacter.WorldId != 0)
            {
                worldIds.Add(activeCharacter.WorldId);
            }
        }

        // 被排除的伺服器不當預設查價目標。查價引擎那一層也擋(UniversalisAvailability),
        // 這裡先濾掉是為了讓顯示端不要先畫出一排永遠不會有資料的伺服器。
        worldIds.RemoveWhere(c => _universalisAvailability.IsWorldExcluded(c));

        return worldIds.ToList();
    }
}