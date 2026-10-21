using InventoryTools.Logic.Settings.Abstract;
using InventoryTools.Logic.Settings.Abstract.Generic;
using InventoryTools.Services;
using Microsoft.Extensions.Logging;

namespace InventoryTools.Logic.WizardSettings;

// 只給設定精靈的「製作清單通知」步驟用：值不存檔（存在欄位裡、初值 false），
// 精靈結束時由 CraftNotificationsFeature.OnFinish 寫進每份製作清單。SettingCategory.None ＝不出現在設定視窗。
public abstract class CraftNotificationsWizardSetting : GenericBooleanSetting
{
    private bool _value;

    protected CraftNotificationsWizardSetting(string key, string name, string helpText, bool defaultValue,
        ILogger logger, ImGuiService imGuiService) : base(key, name, helpText, defaultValue, SettingCategory.None,
        SettingSubCategory.None, "15.0.8", logger, imGuiService)
    {
    }

    public override bool CurrentValue(InventoryToolsConfiguration configuration)
    {
        return _value;
    }

    public override void UpdateFilterConfiguration(InventoryToolsConfiguration configuration, bool newValue)
    {
        _value = newValue;
    }
}

public class CraftNotificationsReportToChatSetting : CraftNotificationsWizardSetting
{
    public CraftNotificationsReportToChatSetting(ILogger<CraftNotificationsReportToChatSetting> logger,
        ImGuiService imGuiService) : base("CraftNotificationsReportToChat",
        "Report acquisition to chat".Loc(),
        "When acquiring items that are part of a craft list, print progress to chat. The craft list must be active for notifications to occur.".Loc(),
        true, logger, imGuiService)
    {
    }
}

public class CraftNotificationsPlaySoundSetting : CraftNotificationsWizardSetting
{
    public CraftNotificationsPlaySoundSetting(ILogger<CraftNotificationsPlaySoundSetting> logger,
        ImGuiService imGuiService) : base("CraftNotificationsPlaySound",
        "Play sound when an item is complete".Loc(),
        "Play a sound effect when an item in a craft list reaches its required quantity. The craft list must be active for notifications to occur.".Loc(),
        false, logger, imGuiService)
    {
    }
}

public class CraftNotificationsCompletionOnlySetting : CraftNotificationsWizardSetting
{
    public CraftNotificationsCompletionOnlySetting(ILogger<CraftNotificationsCompletionOnlySetting> logger,
        ImGuiService imGuiService) : base("CraftNotificationsCompletionOnly",
        "Only report when an item is complete?".Loc(),
        "Instead of reporting every acquisition, only print a message when an item reaches its required quantity. The craft list must be active for notifications to occur.".Loc(),
        false, logger, imGuiService)
    {
    }
}
