using Lumina.Excel.Sheets;

namespace InventoryTools.Misc;

/// <summary>
/// 世界選擇器共用的公開性判定 helper。做法比照 Lifestream/PublicWorlds.cs:
/// rowid 範圍白名單,而不是每個世界選擇器各自維護一份判斷邏輯。
/// InventoryTools 沒有依賴 ECommons(用的是 CriticalCommonLib),所以這裡是獨立實作。
/// </summary>
public static class PublicWorlds
{
    // 台服(陸行鳥 DataCenter=151)現役的 8 個世界,官方 EXD 把 World.IsPublic 全部填 False
    // (2026-08 台服 7.20 實測,不是資料缺漏,官方本來就這樣填)。只放行這個固定的 RowId
    // 範圍,不放行整個 DataCenter——同一個 DataCenter 底下還混了測試/內部伺服器
    // (例如 4000-4002 測試區、4020/4021/4023/4024 t-/td- 內部環境等非公開伺服器)。
    public const uint TaiwanFirstWorldId = 4028;
    public const uint TaiwanLastWorldId = 4035;

    public static bool IsTaiwanWorld(uint worldId)
        => worldId is >= TaiwanFirstWorldId and <= TaiwanLastWorldId;

    public static bool IsTaiwanWorld(this World world)
        => IsTaiwanWorld(world.RowId);

    /// <summary>
    /// 世界選擇器用的公開性判定:官方 IsPublic 欄位再加台服例外。僅用於「讓世界出現在
    /// 可選清單」,不牽涉任何自動查價/自動上架邏輯。
    /// </summary>
    public static bool IsPublicWorld(this World world)
        => world.IsPublic || world.IsTaiwanWorld();
}
