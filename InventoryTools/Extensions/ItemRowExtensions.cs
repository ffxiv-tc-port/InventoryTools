using System.Web;
using AllaganLib.GameSheets.Sheets.Rows;

namespace InventoryTools.Extensions;

public static class ItemRowExtensions
{
    public static string ToGarlandToolsUrl(this ItemRow itemRow)
    {
        return $"https://www.garlandtools.org/db/#item/{itemRow.GarlandToolsId}";
    }

    public static string ToTeamCraftUrl(this ItemRow itemRow)
    {
        return $"https://ffxivteamcraft.com/db/en/item/{itemRow.RowId}";
    }

    public static string ToUniversalisUrl(this ItemRow itemRow)
    {
        return $"https://universalis.app/market/{itemRow.RowId}";
    }

    public static string ToGamerEscapeUrl(this ItemRow itemRow)
    {
        var name = itemRow.NameString.Replace(' ', '_');
        name = name.Replace('–', '-');

        if (name.StartsWith("_")) // "level sync" icon
            name = name.Substring(2);
        return $"https://ffxiv.gamerescape.com/wiki/{HttpUtility.UrlEncode(name)}?useskin=Vector";
    }

    public static string ToHuijiWikiUrl(this ItemRow itemRow)
    {
        // 台服物品名是繁體,灰機wiki 的頁面標題是簡體;它的搜尋分析器能把繁體轉簡體,
        // 且限定「物品」命名空間(ns220)後精確物品會排第一,所以用搜尋 URL 比直連
        // 標題可靠(直連遇到繁簡差異或缺頁會 404,搜尋則優雅降級成相關結果)。
        return $"https://ff14.huijiwiki.com/index.php?search={HttpUtility.UrlEncode(itemRow.NameString)}&ns220=1";
    }

    public static string ToConsoleGamesWikiUrl(this ItemRow itemRow)
    {
        var name = itemRow.NameString.Replace("#"," ").Replace("  ", " ").Replace(' ', '_');
        name = name.Replace('–', '-');

        if (name.StartsWith("_")) // "level sync" icon
            name = name.Substring(2);
        return $"https://ffxiv.consolegameswiki.com/wiki/{HttpUtility.UrlEncode(name)}";
    }
}