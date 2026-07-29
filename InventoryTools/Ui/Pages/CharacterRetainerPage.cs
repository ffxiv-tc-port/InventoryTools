using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AllaganLib.Shared.Extensions;
using CriticalCommonLib.Extensions;
using CriticalCommonLib.Models;
using CriticalCommonLib.Services;
using CriticalCommonLib.Services.Mediator;
using DalaMock.Host.Mediator;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;
using InventoryTools.Mediator;
using InventoryTools.Services;
using InventoryTools.Ui.Widgets;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.Logging;
using ImGuiUtil = OtterGui.ImGuiUtil;

namespace InventoryTools.Ui.Pages
{
    public class CharacterRetainerPage : Page
    {
        private readonly ICharacterMonitor _characterMonitor;
        private readonly IInventoryMonitor _inventoryMonitor;
        private readonly ExcelSheet<World> _worldSheet;

        public CharacterRetainerPage(ILogger<CharacterRetainerPage> logger, ImGuiService imGuiService, ICharacterMonitor characterMonitor, IInventoryMonitor inventoryMonitor, ExcelSheet<World> worldSheet) : base(logger, imGuiService)
        {
            _characterMonitor = characterMonitor;
            _inventoryMonitor = inventoryMonitor;
            _worldSheet = worldSheet;

            // The character/retainer/FC/house lists below are filtered+sorted with LINQ, which used to
            // happen every single Draw() call (every frame the window is open). Cache the results instead
            // and only recompute when something ICharacterMonitor tracks actually changes, or when the
            // world filter combo changes.
            _characterMonitor.OnCharacterUpdated += CharacterMonitorOnCharacterUpdated;
            _characterMonitor.OnCharacterRemoved += CharacterMonitorOnCharacterRemoved;
            _characterMonitor.OnCharacterJobChanged += CharacterMonitorOnCharacterJobChanged;
            _characterMonitor.OnCharacterLoggedIn += CharacterMonitorOnCharacterLoggedIn;
            _characterMonitor.OnCharacterLoggedOut += CharacterMonitorOnCharacterLoggedOut;
            _characterMonitor.OnActiveRetainerChanged += CharacterMonitorOnActiveRetainerChanged;
            _characterMonitor.OnActiveFreeCompanyChanged += CharacterMonitorOnActiveFreeCompanyChanged;
            _characterMonitor.OnActiveHouseChanged += CharacterMonitorOnActiveHouseChanged;
        }
        private bool _isSeparator = false;
        public override void Initialize()
        {
        }

        public override string Name { get; } = "Characters/Retainers";

        private ulong _selectedCharacter = 0;
        private uint _currentWorld = 0;

        private bool _editMode = false;
        private string _newName = "";

        private HoverButton _editIcon = new(new Vector2(16,16));

        private bool _characterListsCacheDirty = true;
        private uint _cachedWorldFilter = 0;
        private List<KeyValuePair<ulong, Character>> _cachedCharacters = new();
        private List<KeyValuePair<ulong, Character>> _cachedFreeCompanies = new();
        private List<KeyValuePair<ulong, Character>> _cachedHouses = new();
        private List<KeyValuePair<ulong, Character>> _cachedRetainers = new();
        private Dictionary<ulong, List<KeyValuePair<ulong, Character>>> _cachedRetainersByOwner = new();

        private void CharacterMonitorOnCharacterUpdated(Character? character) => _characterListsCacheDirty = true;
        private void CharacterMonitorOnCharacterRemoved(ulong characterId) => _characterListsCacheDirty = true;
        private void CharacterMonitorOnCharacterJobChanged() => _characterListsCacheDirty = true;
        private void CharacterMonitorOnCharacterLoggedIn(ulong characterId) => _characterListsCacheDirty = true;
        private void CharacterMonitorOnCharacterLoggedOut(ulong characterId) => _characterListsCacheDirty = true;
        private void CharacterMonitorOnActiveRetainerChanged(ulong retainerId) => _characterListsCacheDirty = true;
        private void CharacterMonitorOnActiveFreeCompanyChanged(ulong freeCompanyId) => _characterListsCacheDirty = true;
        private void CharacterMonitorOnActiveHouseChanged(ulong houseId, sbyte wardId, sbyte plotId, byte divisionId, short roomId, bool hasHousePermission) => _characterListsCacheDirty = true;

        /// <summary>
        /// Recomputes the filtered/sorted character, free company, house and retainer lists used by Draw().
        /// Only called when the underlying data or the world filter has actually changed, instead of on
        /// every single frame.
        /// </summary>
        private void RefreshCharacterListsCache()
        {
            _cachedCharacters = _characterMonitor.GetPlayerCharacters().Where(c => _currentWorld == 0 || _currentWorld == c.Value.WorldId).OrderBy(c => c.Value.FormattedName).ToList();
            _cachedFreeCompanies = _characterMonitor.GetFreeCompanies().Where(c => _currentWorld == 0 || _currentWorld == c.Value.WorldId).OrderBy(c => c.Value.FormattedName).ToList();
            _cachedHouses = _characterMonitor.GetHouses().Where(c => _currentWorld == 0 || _currentWorld == c.Value.WorldId).OrderBy(c => c.Value.FormattedName).ToList();
            _cachedRetainers = _characterMonitor.GetRetainerCharacters().Where(c => _currentWorld == 0 || _currentWorld == c.Value.WorldId).OrderBy(c => c.Value.FormattedName).ToList();

            var retainersByOwner = new Dictionary<ulong, List<KeyValuePair<ulong, Character>>>();
            foreach (var character in _cachedCharacters)
            {
                retainersByOwner[character.Key] = _characterMonitor.GetRetainerCharacters(character.Key).Where(c => _currentWorld == 0 || _currentWorld == c.Value.WorldId).OrderBy(c => c.Value.FormattedName).ToList();
            }
            _cachedRetainersByOwner = retainersByOwner;

            _cachedWorldFilter = _currentWorld;
            _characterListsCacheDirty = false;
        }

        private Dictionary<Character, PopupMenu> _popupMenus = new();
        public PopupMenu GetCharacterMenu(Character character)
        {
            if (!_popupMenus.ContainsKey(character))
            {
                _popupMenus[character] = new PopupMenu("cm_" + character.CharacterId, PopupMenu.PopupMenuButtons.Right,
                    new List<PopupMenu.IPopupMenuItem>()
                    {
                        new PopupMenu.PopupMenuItemSelectableConfirm("Clear Inventories".Loc(), "ci_" + character.CharacterId, "Are you sure you want to clear the inventories of this ".Loc() + character.CharacterType.FormattedName().Loc() + "?", ClearInventories, "Clear the inventories of this ".Loc() + character.CharacterType.FormattedName().Loc() + "?"),
                        new PopupMenu.PopupMenuItemSelectableConfirm("Delete ".Loc() + character.CharacterType.FormattedName().Loc(), "dc_" + character.CharacterId, "Are you sure you want to delete this ".Loc() + character.CharacterType.FormattedName().Loc() + "?", DeleteCharacter, "Delete the ".Loc() + character.CharacterType.FormattedName().Loc() + "?"),
                    }
                );
            }

            return _popupMenus[character];
        }

        private void DeleteCharacter(string arg1, bool arg2)
        {
            if (arg2)
            {
                var characterId = ulong.Parse(arg1.Split("dc_", StringSplitOptions.RemoveEmptyEntries)[0]);
                _characterMonitor.RemoveCharacter(characterId);
                _inventoryMonitor.ClearCharacterInventories(characterId);
            }
        }

        private void ClearInventories(string arg1, bool arg2)
        {
            if (arg2)
            {
                var characterId = ulong.Parse(arg1.Split("ci_", StringSplitOptions.RemoveEmptyEntries)[0]);
                _inventoryMonitor.ClearCharacterInventories(characterId);
            }
        }

        public override List<MessageBase>? Draw()
        {
            if (_characterListsCacheDirty || _cachedWorldFilter != _currentWorld)
            {
                RefreshCharacterListsCache();
            }

            var messages = new List<MessageBase>();
            using (var sidebar = ImRaii.Child("charactersBar", new Vector2(160, 0) * ImGui.GetIO().FontGlobalScale, true))
            {
                if (sidebar.Success)
                {
                    var worldIds = _characterMonitor.GetWorldIds();
                    var characters = _cachedCharacters;
                    ImGui.TextUnformatted("Characters (".Loc() + characters.Count + ")");
                    ImGui.Separator();
                    for (var index = 0; index < characters.Count; index++)
                    {
                        var character = characters[index];
                        using (ImRaii.PushId(index))
                        {
                            if (ImGui.Selectable(character.Value.FormattedName))
                            {
                                _selectedCharacter = character.Key;
                            }

                            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                            {
                                ImGui.OpenPopup("cm_" + character.Key);
                            }

                            GetCharacterMenu(character.Value).Draw();

                            var tooltip = character.Value.FormattedName;
                            if (character.Value.ActualClassJob != null)
                            {
                                tooltip += "\n" + character.Value.ActualClassJob?.Base.Name.ExtractText().ToTitleCase();
                            }

                            tooltip += "\n\nRight Click: Options".Loc();
                            ImGuiUtil.HoverTooltip(tooltip);
                            ImGui.SameLine();
                            if (character.Value.ActualClassJob != null)
                            {
                                var icon = ImGuiService.GetIconTexture(character.Value.Icon);
                                ImGui.Image(icon.Handle, new Vector2(16, 16) * ImGui.GetIO().FontGlobalScale);
                            }
                        }
                    }
                    ImGui.NewLine();

                    var freeCompanies = _cachedFreeCompanies;
                    ImGui.TextUnformatted("Free Companies (".Loc() + freeCompanies.Count + ")");
                    ImGui.Separator();
                    for (var index = 0; index < freeCompanies.Count; index++)
                    {
                        var freeCompany = freeCompanies[index];
                        if (ImGui.Selectable(freeCompany.Value.FormattedName))
                        {
                            _selectedCharacter = freeCompany.Key;
                        }

                        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                        {
                            ImGui.OpenPopup("cm_" + freeCompany.Key);
                        }

                        GetCharacterMenu(freeCompany.Value).Draw();
                        var tooltip = freeCompany.Value.FormattedName;

                        tooltip += "\n\nRight Click: Options".Loc();
                        ImGuiUtil.HoverTooltip(tooltip);
                        if (freeCompany.Value.ActualClassJob != null)
                        {
                            ImGui.SameLine();
                            var icon = ImGuiService.GetIconTexture(freeCompany.Value.Icon);
                            ImGui.Image(icon.Handle, new Vector2(16,16) * ImGui.GetIO().FontGlobalScale);
                        }
                    }
                    ImGui.NewLine();

                    var houses = _cachedHouses;
                    ImGui.TextUnformatted("Residences (".Loc() + houses.Count + ")");
                    ImGui.Separator();
                    for (var index = 0; index < houses.Count; index++)
                    {
                        var house = houses[index];
                        if (ImGui.Selectable(house.Value.FormattedName))
                        {
                            _selectedCharacter = house.Key;
                        }
                        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                        {
                            ImGui.OpenPopup("cm_" + house.Key);
                        }

                        GetCharacterMenu(house.Value).Draw();
                        var tooltip = house.Value.FormattedName;
                        tooltip += "\n" + house.Value.GetPlotSize().ToString();

                        tooltip += "\n\nRight Click: Options".Loc();
                        ImGuiUtil.HoverTooltip(tooltip);

                        if (house.Value.ActualClassJob != null)
                        {
                            ImGui.SameLine();
                            var icon = ImGuiService.GetIconTexture(house.Value.Icon);
                            ImGui.Image(icon.Handle, new Vector2(16,16) * ImGui.GetIO().FontGlobalScale);
                        }
                    }
                    ImGui.NewLine();

                    // Copied because orphaned-retainer detection below removes entries as it matches them
                    // against their owning character; the cached list itself must stay untouched.
                    var retainers = new List<KeyValuePair<ulong, Character>>(_cachedRetainers);
                    ImGui.TextUnformatted("Retainers (".Loc() + retainers.Count + ")");
                    ImGui.Separator();

                    for (var index = 0; index < characters.Count; index++)
                    {
                        var character = characters[index];
                        var characterRetainers = _cachedRetainersByOwner.TryGetValue(character.Key, out var ownedRetainers) ? ownedRetainers : new List<KeyValuePair<ulong, Character>>();
                        ImGui.TextUnformatted(character.Value.FormattedName + " (" + characterRetainers.Count + ")");
                        ImGui.Separator();
                        for (var index2 = 0; index2 < characterRetainers.Count; index2++)
                        {
                            var characterRetainer = characterRetainers[index2];
                            retainers.Remove(characterRetainer);
                            if (ImGui.Selectable(characterRetainer.Value.FormattedName))
                            {
                                _selectedCharacter = characterRetainer.Key;
                            }
                            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                            {
                                ImGui.OpenPopup("cm_" + characterRetainer.Key);
                            }

                            GetCharacterMenu(characterRetainer.Value).Draw();
                            var tooltip = characterRetainer.Value.FormattedName;
                            if (characterRetainer.Value.ActualClassJob != null)
                            {
                                tooltip += "\n" + characterRetainer.Value.ActualClassJob?.Base.Name.ExtractText().ToTitleCase();
                            }

                            tooltip += "\n\nRight Click: Options".Loc();
                            ImGuiUtil.HoverTooltip(tooltip);
                            if (characterRetainer.Value.ActualClassJob != null)
                            {
                                ImGui.SameLine();
                                var icon = ImGuiService.GetIconTexture(characterRetainer.Value.Icon);
                                ImGui.Image(icon.Handle, new Vector2(16,16) * ImGui.GetIO().FontGlobalScale);
                            }
                        }
                        ImGui.NewLine();
                    }

                    if (retainers.Count != 0)
                    {
                        ImGui.TextUnformatted("Orphaned Retainers:".Loc());
                        ImGui.Separator();
                        for (var index2 = 0; index2 < retainers.Count; index2++)
                        {
                            var characterRetainer = retainers[index2];
                            if (ImGui.Selectable(characterRetainer.Value.FormattedName))
                            {
                                _selectedCharacter = characterRetainer.Key;
                            }

                            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                            {
                                ImGui.OpenPopup("cm_" + characterRetainer.Key);
                            }

                            GetCharacterMenu(characterRetainer.Value).Draw();
                            var tooltip = characterRetainer.Value.FormattedName;
                            if (characterRetainer.Value.ActualClassJob != null)
                            {
                                tooltip += "\n" + characterRetainer.Value.ActualClassJob?.Base.Name.ExtractText().ToTitleCase();
                            }

                            tooltip += "\n\nRight Click: Options".Loc();
                            ImGuiUtil.HoverTooltip(tooltip);
                            if (characterRetainer.Value.ActualClassJob != null)
                            {
                                ImGui.SameLine();
                                var icon = ImGuiService.GetIconTexture(characterRetainer.Value.Icon);
                                ImGui.Image(icon.Handle, new Vector2(16, 16) * ImGui.GetIO().FontGlobalScale);
                            }
                        }

                        ImGui.NewLine();
                    }

                    World? selectedWorld = null;
                    if (_currentWorld != 0)
                    {
                        selectedWorld = _worldSheet.GetRowOrDefault(_currentWorld);
                    }

                    ImGui.Text("World: ".Loc());
                    using var combo = ImRaii.Combo("##activeWorld", selectedWorld?.Name.ExtractText() ?? "All".Loc());
                    if (combo.Success)
                    {
                        if (ImGui.Selectable("All".Loc()))
                        {
                            _currentWorld = 0;
                        }

                        var worlds = _worldSheet.Where(c => worldIds.Contains(c.RowId)).ToList();
                        foreach (var world in worlds)
                        {
                            if (ImGui.Selectable(world.Name.ExtractText()))
                            {
                                _currentWorld = world.RowId;
                                _selectedCharacter = 0;
                            }
                        }
                    }
                }
            }
            ImGui.SameLine();
            using (var main = ImRaii.Child("characterMain", new Vector2(0, 0), true))
            {
                if (main.Success)
                {
                    if (_selectedCharacter != 0)
                    {
                        var character = _characterMonitor.GetCharacterById(_selectedCharacter);
                        if (character != null)
                        {
                            ImGui.Text(character.FormattedName.ToString());

                            if (character.ActualClassJob != null)
                            {
                                ImGui.SameLine();
                                var icon = ImGuiService.GetIconTexture((uint)character.Icon);
                                ImGui.Image(icon.Handle, new Vector2(16,16) * ImGui.GetIO().FontGlobalScale);
                            }

                            ImGui.SameLine();
                            if(_editIcon.Draw(ImGuiService.LoadImage("edit"), "editName"))
                            {
                                _editMode = true;
                                _newName = character.AlternativeName ?? "";
                            }

                            ImGuiUtil.HoverTooltip("Edit name, set the name to blank to return it to the original name.".Loc());

                            if (_editMode)
                            {
                                var newName = _newName;
                                ImGui.Text("Custom Name: ".Loc());
                                ImGui.SameLine();
                                if (ImGui.InputText("##customName", ref newName, 100))
                                {
                                    _newName = newName;
                                }

                                if (character.AlternativeName != null && character.AlternativeName != character.Name)
                                {
                                    ImGui.Text("Original Name: ".Loc() + character.Name);
                                }

                                if (ImGui.Button("Save".Loc()))
                                {
                                    if (_newName == "" || _newName == character.Name)
                                    {
                                        character.AlternativeName = null;
                                        _characterMonitor.UpdateCharacter(character);
                                        _editMode = false;
                                    }
                                    else
                                    {
                                        character.AlternativeName = _newName;
                                        _characterMonitor.UpdateCharacter(character);
                                        _editMode = false;
                                    }
                                }
                            }


                            ImGui.Separator();
                            if (character.CharacterType is CharacterType.Character or CharacterType.Retainer )
                            {
                                ImGui.Text("Level: ".Loc() + character.Level);
                                ImGui.Text("Gil: ".Loc() + character.Gil);
                                ImGui.Text("Gender: ".Loc() + character.Gender);
                                ImGui.Text("Free Company: ".Loc() + character.FreeCompanyName);
                                ImGui.Text("World: ".Loc() + (character.World?.Name.ExtractText() ?? "Unknown".Loc()));
                                ImGui.Text("Class/Job: ".Loc() +
                                           (character.ActualClassJob?.Base.Name.ExtractText().ToTitleCase() ?? "Unknown".Loc()));
                            }
                            else if (character.CharacterType is CharacterType.Housing)
                            {
                                ImGui.Text("World: ".Loc() + (character.World?.Name.ExtractText() ?? "Unknown".Loc()));
                                ImGui.Text("Plot Size: ".Loc() + character.GetPlotSize());
                                ImGui.Text("Location: ".Loc() + character.HousingName);
                                ImGui.Text("Owners: ".Loc());
                                foreach (var ownerId in character.Owners)
                                {
                                    var owner = _characterMonitor.GetCharacterById(ownerId);
                                    var ownerName = owner?.FormattedName ?? "Missing Character".Loc();
                                    ImGui.Text(ownerName);
                                }
                            }
                            else if (character.CharacterType is CharacterType.FreeCompanyChest)
                            {
                                ImGui.Text("World: ".Loc() + (character.World?.Name.ExtractText() ?? "Unknown".Loc()));
                                ImGui.Text("Related Characters: ".Loc());
                                foreach (var relatedCharacter in _characterMonitor.GetFreeCompanyCharacters(character.CharacterId))
                                {
                                    var relatedCharacterName = relatedCharacter.Value.FormattedName;
                                    ImGui.Text(relatedCharacterName);
                                }
                            }

                            ImGui.NewLine();
                            ImGui.Text("Inventories: ".Loc());
                            ImGui.Separator();
                            var inventories =
                                _inventoryMonitor.Inventories.ContainsKey(character.CharacterId)
                                    ? _inventoryMonitor.Inventories[character.CharacterId]
                                    : null;
                            if (inventories != null)
                            {
                                var categories = inventories.GetAllInventoriesByCategory();

                                using (var tabBar = ImRaii.TabBar("categories", ImGuiTabBarFlags.FittingPolicyScroll | ImGuiTabBarFlags.ListPopupButton))
                                {
                                    if (tabBar.Success)
                                    {
                                        foreach (var category in categories)
                                        {
                                            var inventoryWidth = 5;
                                            using (var tabItem = ImRaii.TabItem(category.Key.FormattedName()))
                                            {
                                                if (tabItem.Success)
                                                {
                                                    using (var tabBar2 = ImRaii.TabBar("types", ImGuiTabBarFlags.FittingPolicyScroll | ImGuiTabBarFlags.ListPopupButton))
                                                    {
                                                        if (tabBar2.Success)
                                                        {
                                                            var itemsByType = category.Value.GroupBy(c => c.SortedContainer);
                                                            foreach (var type in itemsByType)
                                                            {
                                                                using (var typeChild = ImRaii.TabItem(type.Key.FormattedName()))
                                                                {
                                                                    if (typeChild.Success)
                                                                    {
                                                                        var chunkedItems = type.OrderBy(c => c.Slot).Chunk(inventoryWidth);
                                                                        var realSlot = 1;
                                                                        foreach (var itemChunk in chunkedItems)
                                                                        {
                                                                            for (var index = 0;
                                                                                 index < itemChunk.Length;
                                                                                 index++)
                                                                            {
                                                                                var item = itemChunk[index];
                                                                                using (ImRaii.PushId(item.Slot))
                                                                                {
                                                                                    if (ImGui.ImageButton(item.ItemId == 0
                                                                                                ? ImGuiService
                                                                                                    .GetIconTexture(62574)
                                                                                                    .Handle
                                                                                                : ImGuiService
                                                                                                    .GetIconTexture(item.Icon)
                                                                                                    .Handle,
                                                                                            new Vector2(32, 32)))
                                                                                    {

                                                                                    }

                                                                                    ImGuiUtil.HoverTooltip(item.FormattedName +
                                                                                        " - " + item.Quantity + " in slot ".Loc() +
                                                                                        realSlot);
                                                                                    ImGui.SameLine();

                                                                                    var hoveredRow = -1;

                                                                                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled & ImGuiHoveredFlags.AllowWhenOverlapped & ImGuiHoveredFlags.AllowWhenBlockedByPopup & ImGuiHoveredFlags.AllowWhenBlockedByActiveItem & ImGuiHoveredFlags.AnyWindow)) {
                                                                                        hoveredRow = realSlot;
                                                                                    }

                                                                                    if (hoveredRow == realSlot && item.ItemId != 0 && ImGui.IsMouseReleased(ImGuiMouseButton.Right))
                                                                                    {
                                                                                        ImGui.OpenPopup("RightClick" + realSlot);
                                                                                    }

                                                                                    if (hoveredRow == realSlot && item.ItemId != 0 && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                                                                                    {
                                                                                        messages.Add(new OpenUintWindowMessage(typeof(ItemWindow), item.ItemId));
                                                                                    }

                                                                                    using (var popup = ImRaii.Popup("RightClick" + realSlot))
                                                                                    {
                                                                                        using var _ = ImRaii.PushId("RightClick" + realSlot);
                                                                                        if (popup.Success)
                                                                                        {
                                                                                            ImGuiService.ImGuiMenuService.DrawRightClickPopup(item, messages);
                                                                                        }
                                                                                    }
                                                                                }

                                                                                realSlot++;
                                                                            }

                                                                            ImGui.NewLine();
                                                                        }
                                                                    }
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }


                            }
                            else
                            {
                                ImGui.Text("No inventories found.".Loc());
                            }

                        }
                        else
                        {
                            ImGui.Text("Invalid character selected.".Loc());
                        }
                    }
                }
            }
            return messages;
        }

        public override bool IsMenuItem => _isSeparator;
        public override bool DrawBorder => false;
    }
}