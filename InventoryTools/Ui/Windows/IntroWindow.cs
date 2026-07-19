using System.Numerics;
using CriticalCommonLib.Services.Mediator;
using DalaMock.Host.Mediator;
using ImGuiNET;
using InventoryTools.Logic;
using Dalamud.Interface.Utility.Raii;
using InventoryTools.Mediator;
using InventoryTools.Services;
using Microsoft.Extensions.Logging;

namespace InventoryTools.Ui
{
    public class IntroWindow : GenericWindow
    {
        public IntroWindow(ILogger<IntroWindow> logger, MediatorService mediator, ImGuiService imGuiService, InventoryToolsConfiguration configuration, string name = "Intro Window") : base(logger, mediator, imGuiService, configuration, name)
        {
        }
        public override void Initialize()
        {
            WindowName = "Allagan Tools";
            Flags =
                ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar;
            Key = "intro";
        }


        public override void Invalidate()
        {
        }

        public override FilterConfiguration? SelectedConfiguration => null;
        public override string GenericKey { get; } = "intro";
        public override string GenericName { get; } = "Intro";
        public override bool DestroyOnClose => true;

        public override void Draw()
        {
            using (var leftChild = ImRaii.Child("Left", new Vector2(200, 0)))
            {
                if (leftChild.Success)
                {
                    ImGui.SetCursorPosY(40);
                    ImGui.Image(ImGuiService.GetImageTexture("icon-hor").ImGuiHandle, new Vector2(200, 200) * ImGui.GetIO().FontGlobalScale);
                }
            }
            ImGui.SameLine();
            using (var rightChild = ImRaii.Child("Right", new Vector2(0, 0), false, ImGuiWindowFlags.NoScrollbar))
            {
                if (rightChild.Success)
                {
                    using (var textChild = ImRaii.Child("Text", new Vector2(0, -32)))
                    {
                        if (textChild.Success)
                        {
                            ImGui.TextWrapped("歡迎使用 Allagan Tools。");
                            ImGui.TextWrapped(
                                "Allagan Tools 是一款 Final Fantasy XIV 外掛，提供以下功能：");
                            using (ImRaii.PushIndent())
                            {
                                ImGui.Bullet();
                                ImGui.Text("追蹤你的倉庫");
                                ImGui.Bullet();
                                ImGui.Text("規劃你的製作");
                                ImGui.Bullet();
                                ImGui.Text("提供道具、怪物、任務等各種資訊");
                            }

                            ImGui.TextWrapped(
                                "你可以透過指令快捷鍵（主篩選）或主視窗開啟各種新視窗。");
                            ImGui.TextWrapped(
                                "若不確定該怎麼做，可以右鍵點擊道具或表格列以查看更多選項！");
                            ImGui.TextWrapped(
                                "若想了解各項功能，建議前往設定區域並閱讀 ? 圖示提供的說明資訊。");
                        }
                    }

                    using (var buttonsChild = ImRaii.Child("Buttons", new Vector2(0, 32)))
                    {
                        if (buttonsChild.Success)
                        {
                            if (ImGui.Button("關閉"))
                            {
                                Close();
                            }

                            ImGui.SameLine(0, 4);
                            if (ImGui.Button("關閉並開啟主視窗"))
                            {
                                Close();
                                MediatorService.Publish(new OpenGenericWindowMessage(typeof(FiltersWindow)));
                            }
                        }
                    }
                }
            }
        }

        public override Vector2? DefaultSize { get; } = new Vector2(800, 300);
        public override Vector2? MaxSize { get; } = new Vector2(800, 300);
        public override Vector2? MinSize { get; } = new Vector2(800, 300);
        public override bool SaveState => false;
    }
}