using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AllaganLib.Interface.Widgets;
using AllaganLib.Shared.Extensions;
using Autofac;
using CriticalCommonLib.Services;
using CriticalCommonLib.Services.Mediator;
using DalaMock.Host.Mediator;
using Dalamud.Bindings.ImGui;
using InventoryTools.Logic;
using InventoryTools.Logic.Settings.Abstract;
using InventoryTools.Ui.MenuItems;
using InventoryTools.Ui.Widgets;
using OtterGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using InventoryTools.Extensions;
using InventoryTools.Logic.Features;
using InventoryTools.Mediator;
using InventoryTools.Services;
using InventoryTools.Services.Interfaces;
using InventoryTools.Ui.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Events;
using ImGuiUtil = OtterGui.ImGuiUtil;

namespace InventoryTools.Ui
{
    public class ConfigurationWindow : GenericWindow, IMenuWindow
    {
        private readonly IPluginLog _pluginLog;
        private readonly ConfigurationWizardService _configurationWizardService;
        private readonly IChatUtilities _chatUtilities;
        private readonly PluginLogic _pluginLogic;
        private readonly IListService _listService;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly SettingPage.Factory _settingPageFactory;
        private readonly FilterConfiguration.Factory _filterConfigurationFactory;
        private readonly IEnumerable<ISampleFilter> _sampleFilters;
        private readonly Func<Type, IConfigPage> _configPageFactory;
        private readonly Func<FilterConfiguration, FilterPage> _filterPageFactory;
        private readonly IComponentContext _context;
        private readonly InventoryToolsConfiguration _configuration;
        private readonly VerticalSplitter _verticalSplitter;
        private IEnumerable<IMenuWindow>? _menuWindows;
        private FilterConfiguration? _nextFilter;

        public ConfigurationWindow(ILogger<ConfigurationWindow> logger,
            IPluginLog pluginLog,
            MediatorService mediator,
            ImGuiService imGuiService,
            InventoryToolsConfiguration configuration,
            ConfigurationWizardService configurationWizardService,
            IChatUtilities chatUtilities,
            PluginLogic pluginLogic,
            IListService listService,
            IServiceScopeFactory serviceScopeFactory,
            Func<Type, IConfigPage> configPageFactory,
            Func<FilterConfiguration, FilterPage> filterPageFactory,
            SettingPage.Factory settingPageFactory,
            FilterConfiguration.Factory filterConfigurationFactory,
            IEnumerable<ISampleFilter> sampleFilters,
            IComponentContext context) : base(logger,
            mediator,
            imGuiService,
            configuration,
            "Configuration Window")
        {
            _pluginLog = pluginLog;
            _configurationWizardService = configurationWizardService;
            _chatUtilities = chatUtilities;
            _pluginLogic = pluginLogic;
            _listService = listService;
            _serviceScopeFactory = serviceScopeFactory;
            _settingPageFactory = settingPageFactory;
            _filterConfigurationFactory = filterConfigurationFactory;
            _sampleFilters = sampleFilters;
            _configPageFactory = configPageFactory;
            _filterPageFactory = filterPageFactory;
            _context = context;
            _configuration = configuration;
            _verticalSplitter = new VerticalSplitter(250, new Vector2(200, 400));
            this.Flags = ImGuiWindowFlags.MenuBar;
        }

        public override void Initialize()
        {
            WindowName = "Configuration".Loc();
            Key = "configuration";
            _configPages = new List<IConfigPage>();
            _configPages.Add(new SeparatorPageItem("Settings".Loc()));
            _configPages.Add(_settingPageFactory.Invoke(SettingCategory.General));
            _configPages.Add(_settingPageFactory.Invoke(SettingCategory.Lists));
            _configPages.Add(_settingPageFactory.Invoke(SettingCategory.Highlighting));
            _configPages.Add(_settingPageFactory.Invoke(SettingCategory.Items));
            _configPages.Add(_settingPageFactory.Invoke(SettingCategory.Windows));
            _configPages.Add(_settingPageFactory.Invoke(SettingCategory.AutoSave));
            _configPages.Add(new SeparatorPageItem("Modules".Loc(), true));
            _configPages.Add(_settingPageFactory.Invoke(SettingCategory.MarketBoard));
            _configPages.Add(_settingPageFactory.Invoke(SettingCategory.ToolTips));
            _configPages.Add(_settingPageFactory.Invoke(SettingCategory.ContextMenu));
            _configPages.Add(_settingPageFactory.Invoke(SettingCategory.Hotkeys));
            _configPages.Add(_settingPageFactory.Invoke(SettingCategory.MobSpawnTracker));
            _configPages.Add(_settingPageFactory.Invoke(SettingCategory.TitleMenuButtons));
            _configPages.Add(_settingPageFactory.Invoke(SettingCategory.CraftOverlay));
            _configPages.Add(_settingPageFactory.Invoke(SettingCategory.CraftTracker));
            _configPages.Add(_settingPageFactory.Invoke(SettingCategory.History));
            _configPages.Add(_settingPageFactory.Invoke(SettingCategory.Misc));
            _configPages.Add(_settingPageFactory.Invoke(SettingCategory.Troubleshooting, null, true));
            _configPages.Add(new SeparatorPageItem("Data".Loc(), true));
            _configPages.Add(_configPageFactory.Invoke(typeof(FiltersPage)));
            _configPages.Add(_configPageFactory.Invoke(typeof(CraftFiltersPage)));
            _configPages.Add(_configPageFactory.Invoke(typeof(ImportExportPage)));
            _configPages.Add(_configPageFactory.Invoke(typeof(CharacterRetainerPage)));

            _addFilterMenu = new PopupMenu("addFilter", PopupMenu.PopupMenuButtons.LeftRight,
                new List<PopupMenu.IPopupMenuItem>()
                {
                    new PopupMenu.PopupMenuItemSelectableAskName("Search List".Loc(), "adf1", "New Search List".Loc(), AddSearchFilter, "This will create a new list that let's you search for specific items within your characters and retainers inventories.".Loc()),
                    new PopupMenu.PopupMenuItemSelectableAskName("Sort List".Loc(), "af2", "New Sort Filter".Loc(), AddSortFilter, "This will create a new list that let's you search for specific items within your characters and retainers inventories then determine where they should be moved to.".Loc()),
                    new PopupMenu.PopupMenuItemSelectableAskName("Game Item List".Loc(), "af3", "New Game Item List".Loc(), AddGameItemFilter, "This will create a list that lets you search for all items in the game.".Loc()),
                    new PopupMenu.PopupMenuItemSelectableAskName("History List".Loc(), "af4", "New History Item List".Loc(), AddHistoryFilter, "This will create a list that lets you view historical data of how your inventory has changed.".Loc()),
                });

            _addSampleMenu = new PopupMenu("addSampleFilter", PopupMenu.PopupMenuButtons.LeftRight, []);

            var sampleId = 0;
            foreach (var sampleFilter in _sampleFilters)
            {
                if (sampleFilter.SampleFilterType == SampleFilterType.Default)
                {
                    _addSampleMenu.Items.Add(new PopupMenu.PopupMenuItemSelectableAskName(sampleFilter.Name.Loc(),
                        $"sf{sampleId}", sampleFilter.SampleDefaultName, (newName, id) =>
                        {
                            var createdFilter = sampleFilter.AddFilter();
                            createdFilter.Name = newName;
                        }, sampleFilter.SampleDescription.Loc()));
                    sampleId++;
                }
            }

            _addSampleMenu.Items.Add(new PopupMenu.PopupMenuItemSeparator());

            foreach (var sampleFilter in _sampleFilters)
            {
                if (sampleFilter.SampleFilterType == SampleFilterType.Sample)
                {
                    _addSampleMenu.Items.Add(new PopupMenu.PopupMenuItemSelectableAskName(sampleFilter.Name.Loc(),
                        $"sf{sampleId}", sampleFilter.SampleDefaultName, (newName, id) =>
                        {
                            var createdFilter = sampleFilter.AddFilter();
                            createdFilter.Name = newName;
                        }, sampleFilter.SampleDescription.Loc()));
                    sampleId++;
                }
            }

            _settingsMenu = new PopupMenu("configMenu", PopupMenu.PopupMenuButtons.All,
                new List<PopupMenu.IPopupMenuItem>()
                {
                    new PopupMenu.PopupMenuItemSelectable("Items Window".Loc(), "filters", OpenFiltersWindow,"Open the items window.".Loc()),
                    new PopupMenu.PopupMenuItemSelectable("Craft Window".Loc(), "crafts", OpenCraftsWindow,"Open the crafts window.".Loc()),
                    new PopupMenu.PopupMenuItemSeparator(),
                    new PopupMenu.PopupMenuItemSelectable("Mob Window".Loc(), "mobs", OpenMobsWindow,"Open the mobs window.".Loc()),
                    new PopupMenu.PopupMenuItemSelectable("Npcs Window".Loc(), "npcs", OpenNpcsWindow,"Open the npcs window.".Loc()),
                    new PopupMenu.PopupMenuItemSelectable("Duties Window".Loc(), "duties", OpenDutiesWindow,"Open the duties window.".Loc()),
                    new PopupMenu.PopupMenuItemSelectable("Airships Window".Loc(), "airships", OpenAirshipsWindow,"Open the airships window.".Loc()),
                    new PopupMenu.PopupMenuItemSelectable("Submarines Window".Loc(), "submarines", OpenSubmarinesWindow,"Open the submarines window.".Loc()),
                    new PopupMenu.PopupMenuItemSelectable("Retainer Ventures Window".Loc(), "ventures", OpenRetainerVenturesWindow,"Open the retainer ventures window.".Loc()),
                    new PopupMenu.PopupMenuItemSeparator(),
                    new PopupMenu.PopupMenuItemSelectable("Help".Loc(), "help", OpenHelpWindow,"Open the help window.".Loc()),
                });

            _wizardMenu = new PopupMenu("wizardMenu", PopupMenu.PopupMenuButtons.All,
                new List<PopupMenu.IPopupMenuItem>()
                {
                    new PopupMenu.PopupMenuItemSelectable("Configure new settings".Loc(), "configureNew", ConfigureNewSettings,"Configure new settings.".Loc()),
                    new PopupMenu.PopupMenuItemSelectable("Configure all settings".Loc(), "configureAll", ConfigureAllSettings,"Configure all settings.".Loc()),
                });
            _menuWindows = _context.Resolve<IEnumerable<IMenuWindow>>().OrderBy(c => c.GenericName).Where(c => c.GetType() != this.GetType());

            GenerateFilterPages();
            MediatorService.Subscribe<ListInvalidatedMessage>(this, _ => Invalidate());
            MediatorService.Subscribe<ListRepositionedMessage>(this, _ => Invalidate());
            MediatorService.Subscribe<ListAddedMessage>(this, _ => Invalidate());
            MediatorService.Subscribe<ListRemovedMessage>(this, _ => Invalidate());
            MediatorService.Subscribe<ConfigurationWindowEditFilter>(this,  message =>
            {
                Invalidate();
                SetActiveFilter(message.filter);
            });
            MediatorService.Subscribe<ListInvalidatedMessage>(this, _ => Invalidate());
            MediatorService.Subscribe<ListRepositionedMessage>(this, _ => Invalidate());
            MediatorService.Subscribe<ListAddedMessage>(this, _ => Invalidate());
            MediatorService.Subscribe<ListRemovedMessage>(this, _ => Invalidate());
        }

        private void ListInvalidated(ListInvalidatedMessage obj)
        {
            Invalidate();
        }

        private HoverButton _addIcon = new();
        private HoverButton _lightBulbIcon= new();
        private HoverButton _menuIcon = new ();
        private HoverButton _wizardStart = new();

        private PopupMenu _wizardMenu = null!;

        private void ConfigureAllSettings(string obj)
        {
            _configurationWizardService.ClearFeaturesSeen();
            MediatorService.Publish(new OpenGenericWindowMessage(typeof(ConfigurationWizard)));
        }

        private void ConfigureNewSettings(string obj)
        {
            if (_configurationWizardService.HasNewFeatures)
            {
                MediatorService.Publish(new OpenGenericWindowMessage(typeof(ConfigurationWizard)));
            }
            else
            {
                _chatUtilities.Print("There are no new settings available to configure.".Loc());
            }
        }

        private PopupMenu _addFilterMenu = null!;
        private PopupMenu _addSampleMenu = null!;
        private PopupMenu _settingsMenu = null!;

        private void OpenCraftsWindow(string obj)
        {
            MediatorService.Publish(new OpenGenericWindowMessage(typeof(CraftsWindow)));
        }

        private void OpenFiltersWindow(string obj)
        {
            MediatorService.Publish(new OpenGenericWindowMessage(typeof(FiltersWindow)));
        }

        private void OpenHelpWindow(string obj)
        {
            MediatorService.Publish(new OpenGenericWindowMessage(typeof(HelpWindow)));
        }

        private void OpenDutiesWindow(string obj)
        {
            MediatorService.Publish(new OpenGenericWindowMessage(typeof(DutiesWindow)));
        }

        private void OpenAirshipsWindow(string obj)
        {
            MediatorService.Publish(new OpenGenericWindowMessage(typeof(AirshipsWindow)));
        }

        private void OpenSubmarinesWindow(string obj)
        {
            MediatorService.Publish(new OpenGenericWindowMessage(typeof(SubmarinesWindow)));
        }

        private void OpenRetainerVenturesWindow(string obj)
        {
            MediatorService.Publish(new OpenGenericWindowMessage(typeof(RetainerTasksWindow)));
        }

        private void OpenMobsWindow(string obj)
        {
            MediatorService.Publish(new OpenGenericWindowMessage(typeof(BNpcsWindow)));
        }

        private void OpenNpcsWindow(string obj)
        {
            MediatorService.Publish(new OpenGenericWindowMessage(typeof(ENpcsWindow)));
        }

        private Dictionary<FilterConfiguration, PopupMenu> _popupMenus = new();
        public PopupMenu GetFilterMenu(FilterConfiguration configuration)
        {
            if (!_popupMenus.ContainsKey(configuration))
            {
                _popupMenus[configuration] = new PopupMenu("fm" + configuration.Key, PopupMenu.PopupMenuButtons.Right,
                    new List<PopupMenu.IPopupMenuItem>()
                    {
                        new PopupMenu.PopupMenuItemSelectableAskName("Duplicate".Loc(), "df_" + configuration.Key, configuration.Name, DuplicateFilter, "Duplicate the filter.".Loc()),
                        new PopupMenu.PopupMenuItemSelectable("Move Up".Loc(), "mu_" + configuration.Key, MoveFilterUp, "Move the filter up.".Loc()),
                        new PopupMenu.PopupMenuItemSelectable("Move Down".Loc(), "md_" + configuration.Key, MoveFilterDown, "Move the filter down.".Loc()),
                        new PopupMenu.PopupMenuItemSelectableConfirm("Remove".Loc(), "rf_" + configuration.Key, "Are you sure you want to remove this filter?".Loc(), RemoveFilter, "Remove the filter.".Loc()),
                    }
                );
            }

            return _popupMenus[configuration];
        }

        private void RemoveFilter(string id, bool confirmed)
        {
            if (confirmed)
            {
                id = id.Replace("rf_", "");
                var existingFilter = _listService.GetListByKey(id);
                if (existingFilter != null)
                {
                    _listService.RemoveList(existingFilter);
                    ConfigSelectedConfigurationPage--;
                }
            }
        }

        private void MoveFilterDown(string id)
        {
            id = id.Replace("md_", "");
            var existingFilter = _listService.GetListByKey(id);
            if (existingFilter != null)
            {
                _listService.MoveListDown(existingFilter);
            }
        }

        private void MoveFilterUp(string id)
        {
            id = id.Replace("mu_", "");
            var existingFilter = _listService.GetListByKey(id);
            if (existingFilter != null)
            {
                _listService.MoveListUp(existingFilter);
            }
        }

        private void DuplicateFilter(string filterName, string id)
        {
            id = id.Replace("df_", "");
            var existingFilter = _listService.GetListByKey(id);
            if (existingFilter != null)
            {
                _listService.DuplicateList(existingFilter, filterName);
                SetNewFilterActive();
            }
        }

        private void AddSearchFilter(string newName, string id)
        {
            var filterConfiguration = _filterConfigurationFactory.Invoke();
            filterConfiguration.Name = newName;
            filterConfiguration.FilterType = FilterType.SearchFilter;
            _listService.AddDefaultColumns(filterConfiguration);
            _listService.AddList(filterConfiguration);
            SetNewFilterActive();
        }

        private void AddHistoryFilter(string newName, string id)
        {
            var filterConfiguration = _filterConfigurationFactory.Invoke();
            filterConfiguration.Name = newName;
            filterConfiguration.FilterType = FilterType.HistoryFilter;
            _listService.AddDefaultColumns(filterConfiguration);
            _listService.AddList(filterConfiguration);
            SetNewFilterActive();
        }

        private void AddGameItemFilter(string newName, string id)
        {
            var filterConfiguration = _filterConfigurationFactory.Invoke();
            filterConfiguration.Name = newName;
            filterConfiguration.FilterType = FilterType.GameItemFilter;
            _listService.AddDefaultColumns(filterConfiguration);
            _listService.AddList(filterConfiguration);
            SetNewFilterActive();
        }

        private void AddSortFilter(string newName, string id)
        {
            var filterConfiguration = _filterConfigurationFactory.Invoke();
            filterConfiguration.Name = newName;
            filterConfiguration.FilterType = FilterType.SortingFilter;
            _listService.AddDefaultColumns(filterConfiguration);
            _listService.AddList(filterConfiguration);
            SetNewFilterActive();
        }


        private int ConfigSelectedConfigurationPage
        {
            get => _configuration.SelectedConfigurationPage;
            set => _configuration.SelectedConfigurationPage = value;
        }

        public void SetActiveFilter(FilterConfiguration configuration)
        {
            if (_filterPages.ContainsKey(configuration.Key))
            {
                _nextFilter = configuration;
            }
        }

        public void GenerateFilterPages()
        {
            var filterConfigurations = _listService.Lists.Where(c => c.FilterType != FilterType.CraftFilter);
            var filterPages = new Dictionary<string, IConfigPage>();
            foreach (var filter in filterConfigurations)
            {
                if (!filterPages.ContainsKey(filter.Key))
                {
                    filterPages.Add(filter.Key, _filterPageFactory.Invoke(filter));
                }
            }

            _filterPages = filterPages;
        }

        public override bool SaveState => true;
        public override Vector2? DefaultSize { get; } = new(700, 700);
        public override Vector2? MaxSize { get; } = new(2000, 2000);
        public override Vector2? MinSize { get; } = new(200, 200);
        public override string GenericKey => "configuration";
        public override string GenericName => "Configuration".Loc();
        public override bool DestroyOnClose => true;
        private List<IConfigPage> _configPages = null!;
        public Dictionary<string, IConfigPage> _filterPages = new Dictionary<string,IConfigPage>();


        private void SetNewFilterActive()
        {
            ConfigSelectedConfigurationPage = _configPages.Count + _filterPages.Count - 2;
        }

        private void DrawMenuBar()
        {
            using (var menuBar = ImRaii.MenuBar())
            {
                if (menuBar)
                {
                    using (var menu = ImRaii.Menu("File".Loc()))
                    {
                        if (menu)
                        {
                            if (ImGui.MenuItem("Report a Issue".Loc()))
                            {
                                "https://github.com/Critical-Impact/AllaganMarket".OpenBrowser();
                            }

                            if (ImGui.MenuItem("Changelog".Loc()))
                            {
                                MediatorService.Publish(new OpenGenericWindowMessage(typeof(ChangelogWindow)));
                            }

                            if (ImGui.MenuItem("Help".Loc()))
                            {
                                MediatorService.Publish(new OpenGenericWindowMessage(typeof(HelpWindow)));
                            }

                            if (ImGui.MenuItem("Enable Verbose Logging".Loc(), "",
                                    this._pluginLog.MinimumLogLevel == LogEventLevel.Verbose))
                            {
                                if (this._pluginLog.MinimumLogLevel == LogEventLevel.Verbose)
                                {
                                    this._pluginLog.MinimumLogLevel = LogEventLevel.Debug;
                                }
                                else
                                {
                                    this._pluginLog.MinimumLogLevel = LogEventLevel.Verbose;
                                }
                            }

                            if (ImGui.MenuItem("Ko-Fi".Loc()))
                            {
                                "https://ko-fi.com/critical_impact".OpenBrowser();
                            }

                            if (ImGui.MenuItem("Close".Loc()))
                            {
                                this.IsOpen = false;
                            }
                        }
                    }

                    using (var menu = ImRaii.Menu("Wizard".Loc()))
                    {
                        if (menu)
                        {
                            var hasNewFeatures = this._configurationWizardService.HasNewFeatures;
                            using var disabled = ImRaii.Disabled(!hasNewFeatures);
                            if (ImGui.MenuItem("Configure New Features".Loc()))
                            {
                                MediatorService.Publish(new OpenGenericWindowMessage(typeof(ConfigurationWizard)));
                            }

                            disabled.Dispose();

                            if (ImGui.MenuItem("Reconfigure All Features".Loc()))
                            {
                                this._configurationWizardService.ClearFeaturesSeen();
                                MediatorService.Publish(new OpenGenericWindowMessage(typeof(ConfigurationWizard)));
                            }
                        }
                    }

                    using (var menu = ImRaii.Menu("Windows".Loc()))
                    {
                        if (menu)
                        {
                            if (_menuWindows != null)
                            {
                                foreach (var window in _menuWindows)
                                {
                                    if (ImGui.MenuItem(window.GenericName))
                                    {
                                        MediatorService.Publish(new OpenGenericWindowMessage(window.GetType()));
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        public override void Draw()
        {
            DrawMenuBar();
            _verticalSplitter.Draw(DrawSideBar, DrawMainWindow);
        }

        private void DrawMainWindow()
        {
            IConfigPage? currentConfigPage = null;

            {
                var count = 0;
                for (var index = 0; index < _configPages.Count; index++)
                {
                    var configPage = _configPages[index];
                    if (configPage.IsMenuItem)
                    {
                        continue;
                    }

                    if (ConfigSelectedConfigurationPage == count)
                    {
                        currentConfigPage = configPage;
                    }


                    if (configPage.ChildPages != null)
                    {
                        foreach (var childPage in configPage.ChildPages)
                        {
                            if (ConfigSelectedConfigurationPage == count)
                            {
                                currentConfigPage = childPage;
                            }
                            count++;
                        }
                    }
                    else
                    {
                        count++;
                    }
                }

                foreach (var filter in _filterPages)
                {
                    count++;
                    if (_nextFilter != null)
                    {
                        if (filter.Value is FilterPage filterPage)
                        {
                            if (filterPage.FilterConfiguration == _nextFilter)
                            {
                                currentConfigPage = filterPage;
                                ConfigSelectedConfigurationPage = count;
                                _nextFilter = null;
                            }
                        }
                    }
                    if (ConfigSelectedConfigurationPage == count)
                    {
                        currentConfigPage = filter.Value;
                    }
                }
            }
            if (currentConfigPage != null)
            {
                MediatorService.Publish(currentConfigPage.Draw());
            }
        }

        private void DrawSideBar()
        {
            using (var menuChild = ImRaii.Child("Menu", new Vector2(0, -28) * ImGui.GetIO().FontGlobalScale,
                       false, ImGuiWindowFlags.NoSavedSettings))
            {
                if (menuChild.Success)
                {

                    var count = 0;
                    for (var index = 0; index < _configPages.Count; index++)
                    {
                        var configPage = _configPages[index];
                        if (configPage.IsMenuItem)
                        {
                            MediatorService.Publish(configPage.Draw());
                        }
                        else
                        {
                            var hasChildren = configPage.ChildPages != null;
                            var isSelected = ConfigSelectedConfigurationPage == count;
                            using (var node = ImRaii.TreeNode(configPage.Name, hasChildren ?  ImGuiTreeNodeFlags.None : isSelected ? ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.Selected : ImGuiTreeNodeFlags.Leaf))
                            {
                                if (node)
                                {
                                    if (configPage.ChildPages != null)
                                    {
                                        foreach (var childPage in configPage.ChildPages)
                                        {
                                            isSelected = ConfigSelectedConfigurationPage == count;

                                            using (var subNode = ImRaii.TreeNode(childPage.Name,
                                                       isSelected
                                                           ? ImGuiTreeNodeFlags.Selected |
                                                             ImGuiTreeNodeFlags.Bullet
                                                           : ImGuiTreeNodeFlags.Bullet))
                                            {
                                                if (subNode)
                                                {
                                                }
                                            }

                                            if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen())
                                            {
                                                ConfigSelectedConfigurationPage = count;
                                            }

                                            count++;
                                        }
                                    }
                                }
                                else
                                {
                                    if (configPage.ChildPages != null)
                                    {
                                        foreach (var childPage in configPage.ChildPages)
                                        {
                                            count++;
                                        }
                                    }
                                }
                            }

                            if (!hasChildren)
                            {
                                if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen())
                                {
                                    ConfigSelectedConfigurationPage = count;
                                }
                                count++;

                            }
                        }
                    }

                    ImGui.NewLine();
                    ImGui.TextUnformatted("Item Lists".Loc());
                    ImGui.Separator();

                    var filterIndex = count;
                    foreach (var item in _filterPages)
                    {
                        filterIndex++;
                        using (var subNode = ImRaii.TreeNode(item.Value.Name,
                                   ConfigSelectedConfigurationPage == filterIndex
                                       ? ImGuiTreeNodeFlags.Selected |
                                         ImGuiTreeNodeFlags.Leaf
                                       : ImGuiTreeNodeFlags.Leaf))
                        {
                            if (subNode)
                            {
                            }
                        }

                        if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen())
                        {
                            ConfigSelectedConfigurationPage = filterIndex;
                        }

                        var filter = _listService.GetListByKey(item.Key);
                        if (filter != null)
                        {
                            GetFilterMenu(filter).Draw();
                        }

                    }
                }
            }

            using (var commandBarChild = ImRaii.Child("CommandBar",
                       new Vector2(0, 0) * ImGui.GetIO().FontGlobalScale, false))
            {
                if (commandBarChild.Success)
                {

                    float height = ImGui.GetWindowSize().Y;
                    ImGui.SetCursorPosY(height - 24 * ImGui.GetIO().FontGlobalScale);

                    if(_addIcon.Draw(ImGuiService.GetIconTexture(66315).Handle, "addFilter"))
                    {

                    }

                    _addFilterMenu.Draw();
                    ImGuiUtil.HoverTooltip("Add a new filter".Loc());

                    ImGui.SetCursorPosY(height - 24 * ImGui.GetIO().FontGlobalScale);
                    ImGui.SetCursorPosX(26 * ImGui.GetIO().FontGlobalScale);

                    if (_lightBulbIcon.Draw(ImGuiService.GetIconTexture(66318).Handle,"addSample"))
                    {

                    }

                    _addSampleMenu.Draw();
                    ImGuiUtil.HoverTooltip("Add a sample filter".Loc());

                    var width = ImGui.GetWindowSize().X;
                    width -= 24 * ImGui.GetIO().FontGlobalScale;

                    ImGui.SetCursorPosY(height - 24 * ImGui.GetIO().FontGlobalScale);
                    ImGui.SetCursorPosX(width);

                    if (_menuIcon.Draw(ImGuiService.GetImageTexture("menu").Handle, "openMenu"))
                    {

                    }

                    _settingsMenu.Draw();


                    width -= 26 * ImGui.GetIO().FontGlobalScale;

                    ImGui.SetCursorPosY(height - 24 * ImGui.GetIO().FontGlobalScale);
                    ImGui.SetCursorPosX(width);

                    if (_wizardStart.Draw(ImGuiService.GetImageTexture("wizard").Handle, "openMenu"))
                    {
                        _wizardMenu.Open();
                    }
                    _wizardMenu.Draw();


                    ImGuiUtil.HoverTooltip("Start configuration wizard.".Loc());
                }
            }
        }

        public override void Invalidate()
        {
            GenerateFilterPages();
        }

        public override FilterConfiguration? SelectedConfiguration => null;
    }
}