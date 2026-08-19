using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Dalamud.Bindings.ImGui;
using InventoryTools.Logic.Settings;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace InventoryTools.Highlighting;

public class ShopHighlighting : IDisposable
{
    private readonly IGameGui gameGui;
    private readonly IAddonLifecycle addonLifecycle;
    private readonly IPluginLog pluginLog;
    private readonly ShopHighlightingDisableItemsSetting _shopHighlightingDisableItemsSetting;
    private readonly InventoryToolsConfiguration _configuration;
    private uint shopItemsAtkIndex = 441;
    private uint shopCountAtkIndex = 2;
    private HashSet<uint> highlightedItems = new HashSet<uint>();
    private Dictionary<int, uint>? itemIndexMap = null;


    public ShopHighlighting(IGameGui gameGui,
        IAddonLifecycle addonLifecycle,
        IPluginLog pluginLog,
        ShopHighlightingDisableItemsSetting shopHighlightingDisableItemsSetting,
        InventoryToolsConfiguration configuration)
    {
        this.gameGui = gameGui;
        this.addonLifecycle = addonLifecycle;
        this.pluginLog = pluginLog;
        _shopHighlightingDisableItemsSetting = shopHighlightingDisableItemsSetting;
        _configuration = configuration;
        addonLifecycle.RegisterListener(AddonEvent.PostSetup, "Shop", AddonSetup);
        addonLifecycle.RegisterListener(AddonEvent.PostDraw, "Shop", AddonPostDraw);
    }

    public void AddItem(uint itemId)
    {
        highlightedItems.Add(itemId);
    }

    public void RemoveItem(uint itemId)
    {
        highlightedItems.Remove(itemId);
    }

    public void SetItems(List<uint> items)
    {
        highlightedItems = [..items];
    }

    public void SetItems(HashSet<uint> items)
    {
        highlightedItems = items;
    }

    public void ClearItems()
    {
        highlightedItems.Clear();
    }

    private string itemIdString = "";
    private uint itemId;

    public unsafe void DrawDebug()
    {
        var addon = gameGui.GetAddonByName("Shop");
        if (addon != IntPtr.Zero)
        {
            var atkUnitBase = (AtkUnitBase*)addon.Address;
            var atkComponentBase = atkUnitBase->GetComponentByNodeId(16);
            if (atkComponentBase != null)
            {
                var listNode = (AtkComponentList*)atkComponentBase;

                // 🔴 原本是 `listNode->ItemRendererList->AtkComponentListItemRenderer->ListItemIndex`
                //    兩層全裸。`ItemRendererList` 是 [FieldOffset(0xF0)] 的指標欄位（清單還沒建好
                //    時為 null），每一格的 `AtkComponentListItemRenderer` 同樣是指標。外層那個
                //    `atkComponentBase != null` 守不到中間任何一跳 —— 這是假守衛的「層數不足」形。
                //    解參考 null 是 AccessViolationException（corrupted-state exception，
                //    try/catch 攔不到）。偵錯面板是每影格繪製路徑 ⇒ 取不到就印 ?，不寫 log。
                //    ⚠️ 印 ? 不印 0：0 是合法的 ListItemIndex，把「不知道」畫成 0 會直接誤導。
                AtkComponentListItemRenderer* renderer = null;
                if (listNode->ItemRendererList != null && listNode->ListLength > 0)
                {
                    renderer = listNode->ItemRendererList->AtkComponentListItemRenderer;
                }

                ImGui.TextUnformatted(renderer == null
                    ? "List Item Index: ?"
                    : $"List Item Index: {renderer->ListItemIndex}");
                if (itemIndexMap != null)
                {
                    foreach (var item in itemIndexMap)
                    {
                        ImGui.TextUnformatted(item.Key + ": " + item.Value);
                    }
                }

                if (ImGui.InputText("Item", ref itemIdString, 128))
                {
                    if (uint.TryParse(itemIdString, out itemId))
                    {
                        itemId = itemId;
                    }
                    itemIdString = itemId.ToString();
                }
                if (ImGui.Button("Add Item".Loc()))
                {
                    if (uint.TryParse(itemIdString, out itemId))
                    {
                        highlightedItems.Add(itemId);
                    }
                }
                if (ImGui.Button("Remove Item".Loc()))
                {
                    if (uint.TryParse(itemIdString, out itemId))
                    {
                        highlightedItems.Remove(itemId);
                    }
                }
            }
        }
    }


    private unsafe void AddonPostDraw(AddonEvent type, AddonArgs args)
    {
        if (args.Addon != IntPtr.Zero)
        {
            var atkUnitBase = (AtkUnitBase*)args.Addon.Address;
            var atkComponentBase = atkUnitBase->GetComponentByNodeId(16);
            if (atkComponentBase != null)
            {
                var listNode = (AtkComponentList*)atkComponentBase;
                if (this.itemIndexMap == null)
                {
                    CalculateItemIndexMap(atkUnitBase);
                }

                for (int i = 0; i < listNode->ListLength; i++)
                {
                    if (!highlightedItems.Any())
                    {
                        if (_shopHighlightingDisableItemsSetting.CurrentValue(_configuration))
                        {
                            listNode->SetItemDisabledState(i, false);
                        }
                        listNode->SetItemHighlightedState(i, false);
                    }
                    // ⚠️ 原本是 `itemIndexMap!.ContainsKey(i)`。那個 ! 的前提是
                    //    「CalculateItemIndexMap 一定會指派」，而值還沒填好時它現在會**刻意
                    //    不指派**（好讓下一影格重試），前提不再成立 ⇒ 改成真的判空。
                    //    仍為 null 時這一影格什麼都不做，與「表裡沒有這個索引」的既有行為相同。
                    else if (this.itemIndexMap != null && this.itemIndexMap.ContainsKey(i))
                    {
                        if (highlightedItems.Contains(itemIndexMap[i]))
                        {
                            if (!listNode->GetItemHighlightedState(i))
                            {
                                listNode->SetItemHighlightedState(i, true);
                            }

                            if (_shopHighlightingDisableItemsSetting.CurrentValue(_configuration))
                            {
                                if (listNode->GetItemDisabledState(i))
                                {
                                    listNode->SetItemDisabledState(i, false);
                                }
                            }
                        }
                        else
                        {
                            if (_shopHighlightingDisableItemsSetting.CurrentValue(_configuration))
                            {
                                if (!listNode->GetItemDisabledState(i))
                                {
                                    listNode->SetItemDisabledState(i, true);
                                }
                            }

                            if (listNode->GetItemHighlightedState(i))
                            {
                                listNode->SetItemHighlightedState(i, false);
                            }
                        }
                    }
                }
            }
        }
    }

    private unsafe void CalculateItemIndexMap(AtkUnitBase* atkUnitBase)
    {
        // 🔴 原本這裡兩處都是無界讀。`AtkValues` 是 [FieldOffset(0x178)] 的指標欄位，
        //    PostSetup 那一刻值不保證已經填好，此時它是 null ⇒ 解參考 null+索引就是
        //    AccessViolationException（corrupted-state exception，try/catch 攔不到）。
        //    `shopCountAtkIndex`(2) / `shopItemsAtkIndex`(441) 又都是**寫死的版面索引**，
        //    在台服一律要假設是錯的：索引出界時讀到的是陣列外的記憶體，shopLength 變成
        //    一個垃圾大數，接著那個迴圈會一路往後掃過去 —— 靠「型別不是 UInt 就 break」
        //    當終止條件，等於拿隨機記憶體內容當邊界。
        //    ⚠️ 取不到時**刻意不指派 this.itemIndexMap**，讓它維持 null：AddonPostDraw 的
        //    `if (this.itemIndexMap == null)` 下一影格會再試一次。若在這裡塞一份空表，
        //    高亮功能就會安靜地永久失效（比崩潰更難查）。
        if (atkUnitBase == null || atkUnitBase->AtkValues == null
         || shopCountAtkIndex >= atkUnitBase->AtkValuesCount)
        {
            return;
        }

        var itemIndexMap = new Dictionary<int, uint>();
        var shopLength = atkUnitBase->AtkValues[shopCountAtkIndex].UInt;
        for (var i = shopItemsAtkIndex; i < shopItemsAtkIndex + shopLength; i++)
        {
            // 上界：迴圈次數來自遊戲送來的 shopLength，它自己不受 AtkValuesCount 約束。
            if (i >= atkUnitBase->AtkValuesCount)
            {
                break;
            }

            var atkValue = atkUnitBase->AtkValues[i];
            if (atkValue.Type != ValueType.UInt)
            {
                break;
            }

            itemIndexMap[(int)(i - shopItemsAtkIndex)] = atkValue.UInt;
        }
        this.itemIndexMap = itemIndexMap;
    }

    private unsafe void AddonSetup(AddonEvent type, AddonArgs args)
    {
        if (args.Addon != IntPtr.Zero)
        {
            var atkUnitBase = (AtkUnitBase*)args.Addon.Address;
            CalculateItemIndexMap(atkUnitBase);
        }
    }

    public void Dispose()
    {
        addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "Shop", AddonSetup);
        addonLifecycle.UnregisterListener(AddonEvent.PostDraw, "Shop", AddonPostDraw);
    }
}