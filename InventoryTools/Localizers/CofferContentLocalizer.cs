using System.Collections.Generic;
using System.Linq;
using AllaganLib.GameSheets.Caches;
using AllaganLib.GameSheets.Sheets;
using AllaganLib.GameSheets.Sheets.Rows;
using Lumina.Text.ReadOnly;

namespace InventoryTools.Localizers;

/// <summary>
/// Works out which classes and jobs the contents of a container item (a gear coffer, a weapon
/// box, a treasure map reward, ...) are for, and renders that as one short line.
/// </summary>
public class CofferContentLocalizer
{
    /// <summary>
    /// Item info types that mean "this item, when used, gives you these other items".
    /// </summary>
    /// <remarks>
    /// 🔴 Both are needed, and Loot is the one that matters. Offline count against the
    /// ItemSupplement rows LuminaSupplemental ships (2026-08-09, TC 7.20 data):
    ///   Coffer -> 227 rows, 4 distinct container items, exactly 1 of which has contents
    ///             carrying any class/job at all (無人島特殊配給貨箱).
    ///   Loot   -> 5547 rows, 791 distinct container items, 651 with class/job contents -
    ///             this is where 亞拉戈武器箱, 鑽石武器箱, 歐米茄裝備箱, 陳舊的地圖 etc. live.
    /// Filtering on Coffer alone would have produced an empty line on essentially every box a
    /// player actually hovers, which is the silent-zero failure this comment exists to prevent.
    /// </remarks>
    private static readonly ItemInfoType[] ContainerTypes = [ItemInfoType.Coffer, ItemInfoType.Loot];

    /// <summary>
    /// Past this many jobs the line is truncated and the total is stated instead. A weapon coffer
    /// can cover 40+ jobs; spelling all of them out would be several wrapped lines of tooltip.
    /// </summary>
    private const int MaxNamesShown = 8;

    private readonly ClassJobCategorySheet _classJobCategorySheet;
    private Dictionary<string, string>? _categoryNameBySignature;

    public CofferContentLocalizer(ClassJobCategorySheet classJobCategorySheet)
    {
        _classJobCategorySheet = classJobCategorySheet;
    }

    /// <summary>
    /// True when the item is known to contain other items.
    /// </summary>
    public bool IsContainer(ItemRow item)
    {
        return item.HasUsesByType(ContainerTypes);
    }

    /// <summary>
    /// The class/job ids covered by everything inside this container, in game order.
    /// Empty when the contents are not equippable by anyone (materials, currency, minions...).
    /// </summary>
    public List<uint> ContainedClassJobIds(ItemRow item)
    {
        var found = new HashSet<uint>();
        foreach (var use in item.Uses)
        {
            if (use.Type != ItemInfoType.Coffer && use.Type != ItemInfoType.Loot)
            {
                continue;
            }

            // ItemSource.Item is the reward for a "use" relation and ItemSource.CostItem is the
            // container; the plugin's own ItemSupplementUseRenderer displays source.Item for
            // exactly this reason.
            var contained = use.Item;

            // AllaganLib hands back a ClassJobCategoryRow with the boolean columns already
            // unpacked into ClassJobIds, so nothing here has to know the column order.
            var category = _classJobCategorySheet.GetRowOrDefault(contained.Base.ClassJobCategory.RowId);
            if (category == null)
            {
                continue;
            }

            foreach (var classJobId in category.ClassJobIds)
            {
                // Row 0 is the nameless "adventurer" pseudo job; every "all classes" category
                // sets it and it has no name to print.
                if (classJobId != 0)
                {
                    found.Add(classJobId);
                }
            }
        }

        return found.OrderBy(c => c).ToList();
    }

    /// <summary>
    /// The class/job coverage of a container's contents as one line, or null when the contents
    /// are not tied to any class or job.
    /// </summary>
    public string? FormattedContainedClassJobs(ItemRow item)
    {
        var classJobIds = ContainedClassJobIds(item);
        if (classJobIds.Count == 0)
        {
            return null;
        }

        // The game already has a name for a lot of these combinations (所有職業, 戰鬥精英,
        // 能工巧匠 大地使者, ...). Prefer it - it is shorter than any list we could build and it
        // is the wording the player already sees elsewhere in the UI.
        var exact = ExactCategoryName(classJobIds);
        if (exact != null)
        {
            return exact;
        }

        var names = new List<string>();
        foreach (var classJobId in classJobIds)
        {
            if (names.Count >= MaxNamesShown)
            {
                break;
            }

            var name = ClassJobName(classJobId);
            if (name.Length != 0)
            {
                names.Add(name);
            }
        }

        if (names.Count == 0)
        {
            return null;
        }

        var joined = string.Join(", ", names);
        if (classJobIds.Count > names.Count)
        {
            joined = "?? and ?? more classes/jobs".Loc(joined, classJobIds.Count - names.Count);
        }

        return joined;
    }

    private string ClassJobName(uint classJobId)
    {
        var classJob = _classJobCategorySheet.GetClassJobSheet().GetRowOrDefault(classJobId);
        return classJob == null ? "" : ((ReadOnlySeString)classJob.Base.Name).ExtractText();
    }

    /// <summary>
    /// The name of the ClassJobCategory row whose class/job set is exactly this one, if there is
    /// such a row. Built once and cached; the sheet is ~200 rows and never changes.
    /// </summary>
    private string? ExactCategoryName(List<uint> classJobIds)
    {
        if (_categoryNameBySignature == null)
        {
            var lookup = new Dictionary<string, string>();
            foreach (var category in _classJobCategorySheet)
            {
                var ids = category.ClassJobIds.Where(c => c != 0).OrderBy(c => c).ToList();
                if (ids.Count == 0)
                {
                    continue;
                }

                var name = ((ReadOnlySeString)category.Base.Name).ExtractText();
                if (name.Length == 0)
                {
                    continue;
                }

                // First row wins: several categories share a class/job set and differ only in
                // wording, and the lower row id is the more general/older one.
                lookup.TryAdd(Signature(ids), name);
            }

            _categoryNameBySignature = lookup;
        }

        return _categoryNameBySignature.GetValueOrDefault(Signature(classJobIds));
    }

    private static string Signature(IEnumerable<uint> classJobIds)
    {
        return string.Join(",", classJobIds);
    }
}
