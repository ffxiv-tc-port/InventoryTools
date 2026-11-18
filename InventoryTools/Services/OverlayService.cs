using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CriticalCommonLib.Services.Mediator;
using CriticalCommonLib.Services.Ui;
using DalaMock.Host.Mediator;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using InventoryTools.Lists;
using InventoryTools.Logic;
using InventoryTools.Mediator;
using InventoryTools.Overlays;
using InventoryTools.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace InventoryTools.Services
{
    public class OverlayService : MediatorSubscriberBase, IOverlayService
    {
        private readonly IListService _listService;
        private readonly TableService _tableService;
        private readonly IAddonLifecycle _addonLifecycle;
        private readonly IFramework _frameworkService;
        private readonly Func<FilterConfiguration, FilterState> _filterStateFactory;
        private readonly IGameUiManager _gameUiManager;
        private readonly ILogger<OverlayService> _logger;
        private readonly Dictionary<string, bool> _windowVisible = new();
        private readonly HashSet<string> _windowsToTrack = new();
        private readonly List<IGameOverlay> _overlays = new();
        private FilterState? _lastState;

        public OverlayService(ILogger<OverlayService> logger, MediatorService mediatorService, IAddonLifecycle addonLifecycle, IListService listService,TableService tableService, IFramework frameworkService, Func<FilterConfiguration, FilterState> filterStateFactory, IEnumerable<IGameOverlay> gameOverlays, IGameUiManager gameUiManager) : base(logger, mediatorService)
        {
            _addonLifecycle = addonLifecycle;
            _listService = listService;
            _tableService = tableService;
            _frameworkService = frameworkService;
            _filterStateFactory = filterStateFactory;
            _gameUiManager = gameUiManager;
            _logger = logger;

            foreach (var overlay in gameOverlays)
            {
                AddOverlay(overlay);
            }
        }

        public FilterState? LastState => _lastState;

        public List<IGameOverlay> Overlays
        {
            get => _overlays;
        }

        private void PostRequestedUpdate(AddonEvent type, AddonArgs args)
        {
            if (TryGetTrackedWindow(args, out var addonName))
            {
                DrawOverlays(addonName);
            }
        }

        private void PreFinalize(AddonEvent type, AddonArgs args)
        {
            //Window destroyed
            if (TryGetTrackedWindow(args, out var addonName))
            {
                _windowVisible[addonName] = false;
                DrawOverlays(addonName);
            }
        }

        private void PostSetup(AddonEvent type, AddonArgs args)
        {
            //Window loaded
            if (TryGetTrackedWindow(args, out var addonName))
            {
                _windowVisible[addonName] = true;
                UpdateState(_lastState);
                DrawOverlays(addonName);
            }
        }

        private unsafe void PostRefresh(AddonEvent type, AddonArgs args)
        {
            //Window shown/hidden/needs redraw
            var addon = (AtkUnitBase*)args.Addon.Address;
            if (TryGetTrackedWindow(args, out var addonName))
            {
                var newState = addon->IsVisible;
                var oldState = _windowVisible.ContainsKey(addonName) && _windowVisible[addonName];
                if (oldState != newState)
                {
                    _windowVisible[addonName] = newState;
                    UpdateState(_lastState);
                }

                DrawOverlays(addonName);
            }
        }

        // 契約：等同 _windowsToTrack.Contains(args.AddonName)，命中時回傳集合裡那個相等的字串。
        // 不讀 AddonName：這四個是全域監聽、每個 addon 每次事件都進來，AddonName 第一次讀就建立字串
        // （同一事件所有監聽者共用，誰先讀誰付）。名稱規則照 Dalamud：空指標或首位元組 0＝InvalidAddon、讀到 NUL、上限 32 位元組。
        private unsafe bool TryGetTrackedWindow(AddonArgs args, [NotNullWhen(true)] out string? addonName)
        {
            var addon = (AtkUnitBase*)args.Addon.Address;
            if (addon == null || addon->Name[0] == 0)
            {
                return _windowsToTrack.TryGetValue(AddonArgs.InvalidAddon, out addonName);
            }

            ReadOnlySpan<byte> buffer = addon->Name;
            var terminator = buffer.IndexOf((byte)0);
            var bytes = terminator < 0 ? buffer : buffer[..terminator];
            if (!Ascii.IsValid(bytes))
            {
                return _windowsToTrack.TryGetValue(Encoding.UTF8.GetString(bytes), out addonName);
            }

            foreach (var tracked in _windowsToTrack)
            {
                if (!Ascii.Equals(bytes, tracked)) continue;
                addonName = tracked;
                return true;
            }

            addonName = null;
            return false;
        }

        private void DrawOverlays(string addonName)
        {
            foreach (var overlay in Overlays)
            {
                if (!overlay.Enabled) continue;
                if (addonName == overlay.WindowName.ToString())
                {
                    if (overlay.NeedsStateRefresh)
                    {
                        overlay.UpdateState(_lastState);
                        overlay.NeedsStateRefresh = false;
                    }
                    if (_windowVisible.ContainsKey(overlay.WindowName.ToString()) && _windowVisible[overlay.WindowName.ToString()] && overlay.Draw())
                    {

                    }
                }
            }
        }

        private void TableRefreshed(RenderTableBase tableBase)
        {
            RefreshOverlayStates();
        }

        private void ListServiceOnListToggled(FilterConfiguration configuration, bool newstate)
        {
            RefreshOverlayStates();
        }

        private void ListServiceOnListModified(FilterConfiguration configuration)
        {
            RefreshOverlayStates();
        }

        public void RefreshOverlayStates()
        {
            if (!_frameworkService.IsInFrameworkUpdateThread)
            {
                _frameworkService.RunOnFrameworkThread(RefreshOverlayStates);
                return;
            }
            var activeFilter = _listService.GetActiveUiList(false);
            var activeBackgroundFilter = _listService.GetActiveBackgroundList();
            // 兩個清單都沒有、且上一次已經是「沒有清單」⇒ 疊加層早就是空的，
            // 這一輪不會改變任何狀態。從有清單變成沒有那一次 _lastState 還不是 null，
            // 所以那一次仍然會走下去把疊加層清掉。
            if (activeFilter == null && activeBackgroundFilter == null && _lastState == null)
            {
                return;
            }

            _logger.LogDebug($"Overlays refreshing, active list: {activeFilter?.Name ?? "no list"}, background list: {activeBackgroundFilter?.Name ?? "no list"}");
            if (activeFilter != null && _tableService.HasListTable(activeFilter))
            {
                var newState = _filterStateFactory.Invoke(activeFilter);
                newState.FilterTable = _tableService.GetListTable(activeFilter);
                UpdateState(newState);
            }
            else if (activeBackgroundFilter != null)
            {
                var newState = _filterStateFactory.Invoke(activeBackgroundFilter);
                UpdateState(newState);
            }
            else
            {
                UpdateState(null);
            }
        }

        private void FrameworkOnUpdate(IFramework framework)
        {
            foreach (var overlay in _overlays)
            {
                if (!overlay.Enabled) continue;
                if (overlay.NeedsStateRefresh)
                {
                    overlay.UpdateState(_lastState);
                    overlay.NeedsStateRefresh = false;
                }

                if (overlay.HasAddon)
                {
                    overlay.Update();
                }
            }
        }

        public void UpdateState(FilterState? filterState)
        {
            _lastState = filterState;
            foreach (var overlay in _overlays)
            {
                if (!overlay.Enabled) continue;
                overlay.UpdateState(filterState);
            }
        }

        public void EnableOverlay(IGameOverlay overlayState)
        {
            overlayState.Enabled = true;
            overlayState.NeedsStateRefresh = true;
        }

        public void DisableOverlay(IGameOverlay overlayState)
        {
            overlayState.Enabled = false;
            overlayState.Clear();
        }


        public void AddOverlay(IGameOverlay overlay)
        {
            _windowsToTrack.Add(overlay.WindowName.ToString());
            if (overlay.ExtraWindows != null)
            {
                foreach (var extraWindow in overlay.ExtraWindows)
                {
                    _windowsToTrack.Add(extraWindow.ToString());
                }
            }

            if (!Overlays.Contains(overlay))
            {
                Overlays.Add(overlay);
                overlay.Setup();
                overlay.Draw();
            }
            else
            {
                _logger.LogError("Attempted to add an overlay that is already registered.");
            }
        }

        public void ClearOverlays()
        {
            foreach (var overlay in _overlays)
            {
                overlay.Clear();
            }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            MediatorService.Subscribe<OverlaysRequestRefreshMessage>(this, RefreshRequested);
            _listService.ListConfigurationChanged += ListServiceOnListModified;
            _listService.UiListToggled += ListServiceOnListToggled;
            _listService.BackgroundListToggled += ListServiceOnListToggled;
            _tableService.TableRefreshed += TableRefreshed;
            _addonLifecycle.RegisterListener(AddonEvent.PostRefresh, PostRefresh);
            _addonLifecycle.RegisterListener(AddonEvent.PostSetup, PostSetup);
            _addonLifecycle.RegisterListener(AddonEvent.PreFinalize, PreFinalize);
            _addonLifecycle.RegisterListener(AddonEvent.PostRequestedUpdate,PostRequestedUpdate );
            _frameworkService.Update += FrameworkOnUpdate;
            _gameUiManager.UiVisibilityChanged += GameUiManagerOnUiVisibilityChanged;
            return Task.CompletedTask;
        }

        private void GameUiManagerOnUiVisibilityChanged(WindowName windowName, bool? windowState)
        {
            var windowNameString = windowName.ToString();
            if (_windowsToTrack.Contains(windowNameString) && windowState != null)
            {
                _windowVisible[windowNameString] = windowState.Value;
                UpdateState(_lastState);
                DrawOverlays(windowNameString);
            }
        }

        private void RefreshRequested(OverlaysRequestRefreshMessage obj)
        {
            RefreshOverlayStates();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogTrace("Stopping service {Type} ({This})", GetType().Name, this);
            _addonLifecycle.UnregisterListener(AddonEvent.PostRefresh, PostRefresh);
            _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, PostSetup);
            _addonLifecycle.UnregisterListener(AddonEvent.PreFinalize, PreFinalize);
            _addonLifecycle.UnregisterListener(AddonEvent.PostRequestedUpdate, PostRequestedUpdate);
            _frameworkService.Update -= FrameworkOnUpdate;
            _tableService.TableRefreshed -= TableRefreshed;
            ClearOverlays();
            _listService.ListConfigurationChanged -= ListServiceOnListModified;
            _listService.UiListToggled -= ListServiceOnListToggled;
            _listService.BackgroundListToggled -= ListServiceOnListToggled;
            _gameUiManager.UiVisibilityChanged -= GameUiManagerOnUiVisibilityChanged;
            UnsubscribeAll();
            _logger.LogTrace("Stopped service {Type} ({This})", GetType().Name, this);
            return Task.CompletedTask;
        }
    }
}