using System;
using AllaganLib.Shared.Interfaces;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace InventoryTools.Debuggers;

public class ArmoireDebuggerPane : IDebugPane
{
    private readonly IGameGui _gameGui;

    public ArmoireDebuggerPane(IGameGui gameGui)
    {
        _gameGui = gameGui;
    }

    public string Name => "Armoire";
    public unsafe void Draw()
    {
        var uiState = UIState.Instance();
        if (uiState == null)
        {
            ImGui.Text("UIState not found.");
        }
        else
        {
            ImGui.Text(uiState->Cabinet.IsCabinetLoaded() ? "Cabinet Loaded" : "Cabinet Not Loaded");
        }

        var addon = this._gameGui.GetAddonByName("CabinetWithdraw");
        if (addon != IntPtr.Zero)
        {
            var cabinetWithdraw = (AddonCabinetWithdraw*)addon.Address;
            if (cabinetWithdraw != null)
            {
                // 🔴 這九顆原本都是裸解參考。它們是 AddonCabinetWithdraw 上的
                //    `AtkComponentRadioButton*` 指標欄位（0x6040~0x6080）：視窗還在 setup、
                //    或台服的版面根本沒建出這個節點時就是 null。外層的 `cabinetWithdraw != null`
                //    守不到任何一顆 —— 假守衛的「層數不足」形。解參考 null 是
                //    AccessViolationException，corrupted-state exception，try/catch 攔不到。
                //    偵錯面板是每影格繪製路徑 ⇒ 取不到就印 ?，不寫 log。
                //    ⚠️ 刻意不印成 "no"：把「取不到」畫成「已確認未選取」會直接誤導看面板的人。
                static string Checked(AtkComponentRadioButton* button)
                    => button == null ? "?" : (button->IsChecked ? "yes" : "no");

                ImGui.Text($"Artifact Armor Selected: { Checked(cabinetWithdraw->ArtifactArmorRadioButton) }");
                ImGui.Text($"Seasonal Gear 1 Selected: { Checked(cabinetWithdraw->SeasonalGear1RadioButton) }");
                ImGui.Text($"Seasonal Gear 2 Selected: { Checked(cabinetWithdraw->SeasonalGear2RadioButton) }");
                ImGui.Text($"Seasonal Gear 3 Selected: { Checked(cabinetWithdraw->SeasonalGear3RadioButton) }");
                ImGui.Text($"Seasonal Gear 4 Selected: { Checked(cabinetWithdraw->SeasonalGear4RadioButton) }");
                ImGui.Text($"Seasonal Gear 5 Selected: { Checked(cabinetWithdraw->SeasonalGear5RadioButton) }");
                ImGui.Text($"Achievements Selected: { Checked(cabinetWithdraw->AchievementsRadioButton) }");
                ImGui.Text($"Exclusive Extras Selected: { Checked(cabinetWithdraw->ExclusiveExtrasRadioButton) }");
                ImGui.Text($"Search Selected: { Checked(cabinetWithdraw->SearchRadioButton) }");
            }
        }
    }
}