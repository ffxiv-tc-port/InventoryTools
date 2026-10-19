using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AllaganLib.GameSheets.Caches;
using AllaganLib.GameSheets.ItemSources;
using AllaganLib.GameSheets.Model;
using AllaganLib.GameSheets.Sheets;
using AllaganLib.GameSheets.Sheets.Rows;
using AllaganLib.Shared.Time;
using CriticalCommonLib;
using CriticalCommonLib.Crafting;
using CriticalCommonLib.Extensions;
using CriticalCommonLib.Services;
using CriticalCommonLib.Services.Mediator;
using DalaMock.Host.Mediator;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;
using InventoryTools.Logic.Columns.Abstract;
using InventoryTools.Mediator;
using InventoryTools.Services;
using Lumina.Extensions;
using Microsoft.Extensions.Logging;
using OtterGui;
using ImGuiTable = OtterGui.ImGuiTable;

namespace InventoryTools.Logic.Columns.Buttons
{
    public class CraftGatherColumn : CheckboxColumn
    {
        private readonly IChatUtilities _chatUtilities;
        private readonly ISeTime _seTime;
        private readonly MapSheet _mapSheet;
        private readonly ICommandManager _commandManager;
        private readonly TeleporterService _teleporterService;
        private readonly VendorLookupService _vendorLookupService;

        public CraftGatherColumn(ILogger<CraftGatherColumn> logger, ImGuiService imGuiService, IChatUtilities chatUtilities, ISeTime seTime, MapSheet mapSheet, ICommandManager commandManager, TeleporterService teleporterService, VendorLookupService vendorLookupService) : base(logger, imGuiService)
        {
            _chatUtilities = chatUtilities;
            _seTime = seTime;
            _mapSheet = mapSheet;
            _commandManager = commandManager;
            _teleporterService = teleporterService;
            _vendorLookupService = vendorLookupService;
        }
        public override ColumnCategory ColumnCategory => ColumnCategory.Buttons;

        public override bool? CurrentValue(ColumnConfiguration columnConfiguration, SearchResult searchResult)
        {
            if (searchResult.CraftItem != null)
            {
                return true;
            }
            return searchResult.Item.ObtainedGathering || searchResult.Item.ObtainedFishing || searchResult.Item.ObtainedSpearFishing;
        }

        List<(IShop shop, ENpcBaseRow? npc, ILocation? location)> GetLocations(ItemRow item)
        {
            var vendors = new List<(IShop shop, ENpcBaseRow? npc, ILocation? location)>();
            var shops = item.GetSourcesByCategory<ItemShopSource>(ItemInfoCategory.Shop).Select(c => c.Shop);
            foreach (var vendor in shops)
            {
                if (vendor.Name == "")
                {
                    continue;
                }
                if (!vendor.ENpcs.Any())
                {
                    vendors.Add(new (vendor, null, null));
                }
                else
                {
                    foreach (var npc in vendor.ENpcs)
                    {
                        if (!npc.Locations.Any())
                        {
                            vendors.Add(new (vendor, npc, null));
                        }
                        else
                        {
                            foreach (var location in npc.Locations)
                            {
                                vendors.Add(new (vendor, npc, location));
                            }
                        }
                    }
                }
            }

            vendors = vendors.OrderByDescending(c => c.npc != null && c.location != null).ToList();
            return vendors;
        }

        void DrawSupplierRow(ItemRow item,(IShop shop, ENpcBaseRow? npc, ILocation? location) tuple, List<MessageBase> messages)
        {
            ImGui.TableNextColumn();
            if (ImGui.TableGetColumnFlags().HasFlag(ImGuiTableColumnFlags.IsEnabled))
            {
                ImGui.TextWrapped(tuple.shop.Name);
            }

            if (tuple.npc != null)
            {
                ImGui.TableNextColumn();
                if (ImGui.TableGetColumnFlags().HasFlag(ImGuiTableColumnFlags.IsEnabled))
                {
                    ImGui.TextWrapped(tuple.npc?.Resident.Value.Singular.ExtractText() ?? "");
                }
            }
            if (tuple.npc != null && tuple.location != null)
            {
                ImGui.TableNextColumn();
                if (ImGui.TableGetColumnFlags().HasFlag(ImGuiTableColumnFlags.IsEnabled))
                {
                    ImGui.TextWrapped(tuple.location + " ( " + Math.Round(tuple.location.MapX, 2) + "/" +
                                      Math.Round(tuple.location.MapY, 2) + ")");
                }

                ImGui.TableNextColumn();
                if (ImGui.TableGetColumnFlags().HasFlag(ImGuiTableColumnFlags.IsEnabled))
                {
                    if (ImGui.Button("Teleport".Loc() + "##" + tuple.shop.RowId + "_" + tuple.npc.RowId + "_" +
                                     tuple.location.Map.RowId))
                    {
                        var nearestAetheryte = _teleporterService.GetNearestAetheryte(tuple.location);
                        if (nearestAetheryte != null)
                        {
                            messages.Add(new RequestTeleportMessage(nearestAetheryte.Value.RowId));
                        }

                        _chatUtilities.PrintFullMapLink(tuple.location, item.NameString);
                        ImGui.CloseCurrentPopup();
                    }
                }
            }
            else
            {
                ImGui.TableNextColumn();
                ImGui.TableNextColumn();
            }
        }

        public override List<MessageBase>? Draw(FilterConfiguration configuration,
            ColumnConfiguration columnConfiguration,
            SearchResult searchResult, int rowIndex, int columnIndex)
        {
            ImGui.TableNextColumn();
            if (!ImGui.TableGetColumnFlags().HasFlag(ImGuiTableColumnFlags.IsEnabled)) return null;

            return DrawButton(columnConfiguration, searchResult, rowIndex);
        }

        public List<MessageBase> DrawButton(ColumnConfiguration columnConfiguration, SearchResult searchResult, int rowIndex)
        {
            var messages = new List<MessageBase>();
            this.DrawGatherAndBuy(columnConfiguration, searchResult, rowIndex, messages);
            this.DrawVendorHint(searchResult, rowIndex);
            return messages;
        }

        private void DrawGatherAndBuy(ColumnConfiguration columnConfiguration, SearchResult searchResult, int rowIndex, List<MessageBase> messages)
        {
            if (CurrentValue(columnConfiguration, searchResult) == true)
            {
                bool hasVendors;
                bool hasGather;
                if (searchResult.CraftItem != null && searchResult.CraftItem.IngredientPreference.Type is IngredientPreferenceType.Buy or IngredientPreferenceType.HouseVendor)
                {
                    hasVendors = DrawVendorButton(searchResult, rowIndex, messages, false);
                    hasGather = DrawGatherButtons(searchResult, rowIndex, hasVendors);
                }
                else
                {
                    hasGather = DrawGatherButtons(searchResult, rowIndex, false);
                    hasVendors = DrawVendorButton(searchResult, rowIndex, messages, hasGather);
                }

                var gatheringUptimes = searchResult.Item.GatheringUpTimes;
                if (gatheringUptimes.Count != 0)
                {
                    var firstUptime = gatheringUptimes.Select(c => c.NextUptime(_seTime.ServerTime)).Where(c => !c.Equals(TimeInterval.Always) && !c.Equals(TimeInterval.Invalid) && !c.Equals(TimeInterval.Never)).OrderBy(c => c).FirstOrNull();

                    if (firstUptime == null)
                    {
                        return;
                    }

                    if (hasGather || hasVendors)
                    {
                        ImGui.SameLine();
                    }

                    if (firstUptime.Value.Start > TimeStamp.UtcNow)
                    {
                        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed))
                        {
                            ImGui.Text(" (Up in ".Loc() +
                                       TimeInterval.DurationString(firstUptime.Value.Start, TimeStamp.UtcNow,
                                           true) + ")");
                        }
                    }
                    else
                    {
                        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.HealerGreen))
                        {
                            ImGui.Text(" (Up for ".Loc() +
                                       TimeInterval.DurationString(firstUptime.Value.End, TimeStamp.UtcNow,
                                           true) + ")");
                        }
                    }

                    ImGui.SameLine();
                    var wrap = ImGuiService.TextureProvider.GetFromGameIcon(new GameIconLookup(66317)).GetWrapOrEmpty();
                    ImGui.Image(wrap.Handle, new(16, 16));

                    if (ImGui.IsItemHovered())
                    {
                        using (var tooltip = ImRaii.Tooltip())
                        {
                            if (tooltip.Success)
                            {
                                var pointsWithUpTimes = searchResult.Item.GatheringPoints.Where(c => c.GatheringPointTransient.GetGatheringUptime() != null).DistinctBy(c => c.GatheringPointTransient.GetGatheringUptime());
                                foreach (var nextUptime in pointsWithUpTimes.Select(row => (row, row.GatheringPointTransient.GetGatheringUptime()!.Value.NextUptime(_seTime.ServerTime))).Where(c => !c.Item2.Equals(TimeInterval.Always) && !c.Item2.Equals(TimeInterval.Invalid) && !c.Item2.Equals(TimeInterval.Never)).OrderBy(c => c.Item2))
                                {
                                    var map = _mapSheet.GetRow(nextUptime.row.Base.TerritoryType.Value.Map.RowId);
                                    ImGui.Text(map.FormattedName + ": ");
                                    ImGui.SameLine();
                                    if (nextUptime.Item2.Start > TimeStamp.UtcNow)
                                    {
                                        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed))
                                        {
                                            ImGui.Text( " (Up in ".Loc() +
                                                        TimeInterval.DurationString(nextUptime.Item2.Start, TimeStamp.UtcNow,
                                                            true) + ")");
                                        }
                                    }
                                    else
                                    {
                                        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.HealerGreen))
                                        {
                                            ImGui.Text(" (Up for ".Loc() +
                                                       TimeInterval.DurationString(nextUptime.Item2.End, TimeStamp.UtcNow,
                                                           true) + ")");
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 「哪裡買」：缺料的那一列標出一個 NPC 商人，並提供「前往」。
        /// </summary>
        /// <remarks>
        /// 🔴 <b>「不知道」與「沒有」一定要分得出來。</b>ItemVendorLocation 沒安裝時畫的是灰色的
        /// <c>NPC 可買：?</c>（滑鼠移上去說明原因），<b>不是</b>什麼都不畫——
        /// 什麼都不畫等於告訴使用者「這東西沒有 NPC 賣」。
        /// 反過來，問到了而且答案就是「沒有 NPC 賣」時才什麼都不畫，那一列不需要多一行雜訊。
        /// <para>
        /// 📌 只畫在<b>缺料</b>的列上（成品列與已經湊齊的列不畫）——製作清單真正要問「哪裡買」的
        /// 就是那些列，其餘都畫等於洗版。
        /// </para>
        /// </remarks>
        private void DrawVendorHint(SearchResult searchResult, int rowIndex)
        {
            var craftItem = searchResult.CraftItem;
            if (craftItem == null || craftItem.IsOutputItem || craftItem.QuantityMissingOverall == 0)
            {
                return;
            }

            var (state, hint) = _vendorLookupService.Lookup(craftItem.ItemId);
            if (state == VendorLookupState.NoVendor)
            {
                return;
            }

            if (state == VendorLookupState.Unknown || hint == null)
            {
                using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey))
                {
                    ImGui.TextWrapped("Vendor: ?".Loc());
                }

                ImGuiUtil.HoverTooltip("ItemVendorLocation is not installed, so whether an NPC sells this is unknown.".Loc());
                return;
            }

            var npcName = string.IsNullOrEmpty(hint.NpcName) ? "?" : hint.NpcName;
            var placeName = string.IsNullOrEmpty(hint.PlaceName) ? "?" : hint.PlaceName;
            using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey))
            {
                ImGui.TextWrapped("Vendor: ?? - ??".Loc(npcName, placeName));
            }

            // 🔴 MapCoordinatesKnown 為 false 時那兩個座標欄位是 0，
            //    把它印出來等於告訴使用者商人在地圖原點。
            var tooltipLines = new List<string>
            {
                hint.MapCoordinatesKnown
                    ? "?? sells this at ?? (??, ??).".Loc(npcName, placeName,
                        Math.Round(hint.MapX, 1), Math.Round(hint.MapY, 1))
                    : "?? sells this at ??.".Loc(npcName, placeName),
            };

            if (!string.IsNullOrEmpty(hint.ShopName))
            {
                tooltipLines.Add("Shop: ??".Loc(hint.ShopName));
            }

            if (!string.IsNullOrEmpty(hint.CostSummary))
            {
                tooltipLines.Add("Cost: ??".Loc(hint.CostSummary));
            }

            ImGuiUtil.HoverTooltip(string.Join("\n", tooltipLines));

            var canTravel = _vendorLookupService.IsTravelAvailable();
            using (ImRaii.Disabled(!canTravel))
            {
                if (ImGui.Button("Travel".Loc() + "##vendorTravel" + rowIndex))
                {
                    _vendorLookupService.Travel(hint, searchResult.Item.NameString);
                }
            }

            // ⚠️ 按鈕被 Disabled 包住時預設不算 hover，「為什麼是灰的」那一則會永遠不出現
            // ——所以要帶 AllowWhenDisabled。
            ImGuiUtil.HoverTooltip(
                canTravel
                    ? "Have Lifestream teleport and take you to this vendor.".Loc()
                    : "Lifestream is not installed or is busy, so travelling automatically is unavailable.".Loc(),
                ImGuiHoveredFlags.AllowWhenDisabled);
        }

        private bool DrawGatherButtons(SearchResult searchResult, int rowIndex, bool needsSameLine)
        {
            if (searchResult.Item.ObtainedGathering)
            {
                if (needsSameLine)
                {
                    ImGui.SameLine();
                }
                if (ImGui.Button("Gather".Loc() + "##Gather" + rowIndex))
                {
                    _commandManager.ProcessCommand("/gather " + searchResult.Item.Base.Name.ExtractText());
                }

                return true;
            }
            else if (searchResult.Item.ObtainedFishing || searchResult.Item.ObtainedSpearFishing)
            {
                if (needsSameLine)
                {
                    ImGui.SameLine();
                }
                if (ImGui.Button("Gather".Loc() + "##Gather" + rowIndex))
                {
                    _commandManager.ProcessCommand("/gatherfish " + searchResult.Item.Base.Name.ExtractText());
                }
                return true;
            }

            return false;
        }

        private bool DrawVendorButton(SearchResult item, int rowIndex, List<MessageBase> messages, bool needsSameLine)
        {
            var shops = item.Item.GetSourcesByCategory<ItemShopSource>(ItemInfoCategory.Shop).Select(c => c.Shop);

            if (shops.Any())
            {
                if (needsSameLine)
                {
                    ImGui.SameLine();
                }
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0.0f);
                if (ImGui.Button("Buy".Loc() + "##Buy" + rowIndex))
                {
                    uint? umapId = item.CraftItem?.MapId ?? null;
                    int mapId = umapId == null ? -1 : (int)umapId;
                    var vendor = GetLocations(item.Item).OrderBy(c => (c.location?.Map.RowId ?? 0) == mapId ? 0 : 1).FirstOrDefault();
                    if (vendor.location != null)
                    {
                        var nearestAetheryte = _teleporterService.GetNearestAetheryte(vendor.location);
                        if (nearestAetheryte != null)
                        {
                            messages.Add(new RequestTeleportMessage(nearestAetheryte.Value.RowId));
                        }

                        var npcName = vendor.npc?.Resident.Value.Singular.ExtractText() ?? null;

                        List<string?> stringParts = new()
                        {
                            vendor.location + " - Buy ".Loc(),
                            item.CraftItem?.QuantityMissingOverall.ToString() ?? null,
                            item.Item.NameString,
                            npcName != null ? $" from {npcName}" : null
                        };

                        var mapLinkText = String.Join("", stringParts.Where(c => c != null).Select(c => c!).ToList());

                        _chatUtilities.PrintFullMapLink(vendor.location, mapLinkText);
                    }
                    else
                    {
                        var shopName = vendor.shop.Name;
                        _chatUtilities.Print("No location available. Shop is called ".Loc() + shopName);
                    }
                }

                ImGui.SameLine(0, 0);
                if (ImGuiP.ArrowButtonEx("select##" + rowIndex, ImGuiDir.Down, new Vector2(24,24)))
                {
                    ImGui.OpenPopup("buyLocations" + rowIndex);
                }

                using (var popup = ImRaii.Popup("buyLocations" + rowIndex))
                {
                    if (popup.Success)
                    {
                        using (var scroller = ImRaii.Child("buyLocationsScroll" + rowIndex, new(400, 200)))
                        {
                            if (scroller.Success)
                            {
                                ImGuiTable.DrawTable("VendorsText", GetLocations(item.Item), tuple =>
                                    {
                                        DrawSupplierRow(item.Item, tuple, messages);
                                    }, ImGuiTableFlags.None,
                                    new[] { "Shop Name".Loc(), "NPC".Loc(), "Location".Loc(), "" });
                            }
                        }
                    }
                }

                ImGui.PopStyleVar();
                return true;
            }

            return false;
        }

        public override string RenderName { get; } = "Gather/Purchase".Loc();
        public override string Name { get; set; } = "Gather/Purchase/Buy".Loc();
        public override float Width { get; set; } = 100;
        public override string HelpText { get; set; } = "Shows a button that links to gatherbuddy's /gather function.".Loc();
        public override bool HasFilter { get; set; } = false;
        public override ColumnFilterType FilterType { get; set; } = ColumnFilterType.Text;
        public override FilterType DefaultIn => Logic.FilterType.CraftFilter;
    }
}