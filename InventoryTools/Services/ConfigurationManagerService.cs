using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using AllaganLib.Shared.Interfaces;
using AllaganLib.Shared.Services;
using CriticalCommonLib.Helpers;
using CriticalCommonLib.Interfaces;
using CriticalCommonLib.Models;
using CriticalCommonLib.Resolvers;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using InventoryTools.Converters;
using LuminaSupplemental.Excel.Model;
using LuminaSupplemental.Excel.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Task = System.Threading.Tasks.Task;

namespace InventoryTools.Services
{
    using Dalamud.Plugin;

    public class ConfigurationManagerService : BackgroundService
    {
        public ILogger<ConfigurationManagerService> Logger { get; }

        public delegate void ConfigurationChangedDelegate();
        private readonly IFramework _framework;
        private bool _configurationLoaded = false;

        public event ConfigurationChangedDelegate? ConfigurationChanged;

        public ConfigurationManagerService(IFramework framework, IDalamudPluginInterface pluginInterfaceService, ILogger<ConfigurationManagerService> logger, BackgroundTaskQueue.Factory taskQueueFactory, MinifyResolver minifyResolver, ContainerAwareCsvLoader containerAwareCsvLoader)
        {
            Logger = logger;
            _pluginInterfaceService = pluginInterfaceService;
            _saveQueue = taskQueueFactory.Invoke("Configuration Save Queue");
            _minifyResolver = minifyResolver;
            _containerAwareCsvLoader = containerAwareCsvLoader;
            _framework = framework;
            _framework.Update += OnUpdate;
        }

        private void OnUpdate(IFramework framework)
        {
            if (_configurationLoaded)
            {
                if (Config.IsDirty)
                {
                    Config.IsDirty = false;
                    ConfigurationChanged?.Invoke();
                    SaveAsync();
                }
            }
        }

        public InventoryToolsConfiguration Config
        {
            get;
            set;
        } = null!;

        public string ConfigurationFile
        {
            get
            {
                return _pluginInterfaceService.ConfigFile.ToString();
            }
        }
        public string InventoryFile
        {
            get
            {
                return Path.Join(_pluginInterfaceService.ConfigDirectory.FullName, "inventories.json");
            }
        }
        public string InventoryCsv
        {
            get
            {
                return Path.Join(_pluginInterfaceService.ConfigDirectory.FullName, "inventories.csv");
            }
        }
        public string MobSpawnFile
        {
            get
            {
                return Path.Join(_pluginInterfaceService.ConfigDirectory.FullName, "mob_spawns.csv");
            }
        }
        public string HistoryCsv
        {
            get
            {
                return Path.Join(_pluginInterfaceService.ConfigDirectory.FullName, "history.csv");
            }
        }

        public InventoryToolsConfiguration GetConfig()
        {
            return Config;
        }


        /// <summary>
        /// Loads the primary configuration file
        /// </summary>
        /// <param name="file">An alternate file to load</param>
        public void Load(string? file = null)
        {
            Logger.LogTrace("Loading configuration");
            Stopwatch loadConfigStopwatch = new Stopwatch();
            loadConfigStopwatch.Start();
            if (!File.Exists(file ?? ConfigurationFile))
            {
                Config = new InventoryToolsConfiguration();
                Config.MarkReloaded();
                _configurationLoaded = true;
                return;
            }
            string jsonText = File.ReadAllText(file ?? ConfigurationFile);
            var inventoryToolsConfiguration = JsonConvert.DeserializeObject<InventoryToolsConfiguration>(jsonText, new JsonSerializerSettings()
            {
                Converters = new List<JsonConverter>()
                {
                  new ColumnConverter()
                },
                TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
                TypeNameHandling = TypeNameHandling.None,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate,
                ContractResolver = _minifyResolver
            });
            if (inventoryToolsConfiguration == null)
            {
                // Newtonsoft 對空白／全 NUL 回 null 不擲例外；之後的存檔會覆寫原檔，先留備份。
                BackupUnreadable(file ?? ConfigurationFile);
                Config = new InventoryToolsConfiguration();
                Config.MarkReloaded();
                _configurationLoaded = true;
                return;
            }
            loadConfigStopwatch.Stop();
            Logger.LogTrace("Took " + loadConfigStopwatch.Elapsed.TotalSeconds + " to load main configuration file.");
            Config = inventoryToolsConfiguration;
            Config.MarkReloaded();
            _configurationLoaded = true;
        }

        public List<InventoryItem> LoadInventory(string? file = null)
        {
            Logger.LogTrace("Loading inventory");
            Stopwatch loadConfigStopwatch = new Stopwatch();
            loadConfigStopwatch.Start();

            var inventories = new List<InventoryItem>();


            if (!Config.InventoriesMigrated)
            {
                if (File.Exists(ConfigurationFile))
                {
                    string jsonText = File.ReadAllText(ConfigurationFile);
                    var inventoryToolsConfiguration = JsonConvert.DeserializeObject<InventoryToolsConfiguration>(
                        jsonText, new JsonSerializerSettings()
                        {
                            Converters = new List<JsonConverter>()
                            {
                                new ColumnConverter()
                            },
                            DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate,
                            ContractResolver = _minifyResolver
                        });
                    if (inventoryToolsConfiguration != null)
                    {
                        Logger.LogTrace("Migrating inventories");
                        var temp = JObject.Parse(jsonText);
                        if (temp.ContainsKey("SavedInventories"))
                        {
                            var savedInventories = temp["SavedInventories"]
                                ?.ToObject<Dictionary<ulong, Dictionary<InventoryCategory, List<InventoryItem>>>>();

                            var parsedInventories = savedInventories ??
                                                    new Dictionary<ulong, Dictionary<
                                                        InventoryCategory,
                                                        List<InventoryItem>>>();
                            foreach (var parsedInventory in parsedInventories)
                            {
                                foreach (var category in parsedInventory.Value)
                                {
                                    foreach (var item in category.Value)
                                    {
                                        inventories.Add(item);
                                    }
                                }
                            }
                        }
                    }
                }

                Config.InventoriesMigrated = true;
            }
            else if (!Config.InventoriesMigratedToCsv)
            {
                Logger.LogTrace("Marked inventories to now load from CSV");
                if (File.Exists(InventoryFile))
                {
                    var parsedInventories = LoadInventoriesJson(InventoryFile) ?? new();
                    foreach (var parsedInventory in parsedInventories)
                    {
                        foreach (var category in parsedInventory.Value)
                        {
                            foreach (var item in category.Value)
                            {
                                inventories.Add(item);
                            }
                        }
                    }
                }

                Config.InventoriesMigratedToCsv = true;
            }
            else
            {
                inventories = LoadInventoriesFromCsv(out bool success);
            }
            loadConfigStopwatch.Stop();
            Logger.LogTrace("Took " + loadConfigStopwatch.Elapsed.TotalSeconds + " to load inventories.");
            loadConfigStopwatch.Restart();
            return inventories;
        }

        /// <summary>
        /// 原子寫入設定檔，永不擲例外：失敗只記 Warning，原檔維持上一次的完整內容。
        /// 背景佇列與卸載都會呼叫，以 <see cref="_saveLock"/> 序列化（.tmp 名稱固定、獨占開啟，並行＝後到者失敗）。
        /// </summary>
        public void Save()
        {
            try
            {
                // 序列化也在鎖內：後拿到鎖的那次讀的是最新的 Config，最後寫入的一定是最新狀態。
                lock (_saveLock)
                {
                    Stopwatch loadConfigStopwatch = new Stopwatch();
                    loadConfigStopwatch.Start();
                    Logger.LogTrace("Saving allagan tools configuration");
                    FilesystemUtil.WriteAllTextSafe(ConfigurationFile, JsonConvert.SerializeObject(Config, Formatting.None, new JsonSerializerSettings()
                    {
                        TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
                        TypeNameHandling = TypeNameHandling.None,
                        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                        DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate,
                        ContractResolver = _minifyResolver
                    }));
                    loadConfigStopwatch.Stop();
                    Logger.LogTrace("Took " + loadConfigStopwatch.Elapsed.TotalSeconds + " to save configuration.");
                }
            }
            catch (Exception e)
            {
                Logger.LogWarning($"Failed to save allagan tools configuration due to {e.GetType().Name}: {e.Message}; the existing file was left unchanged.");
            }
        }

        // 合流：佇列裡已有一筆還沒開始的存檔就不再排；旗標在序列化之前清掉，之後的變動會再排一筆。
        private void SaveAsync()
        {
            if (Interlocked.Exchange(ref _saveQueued, 1) == 1)
            {
                return;
            }

            _saveQueue.QueueBackgroundWorkItemAsync(token => Task.Run(() =>
            {
                Interlocked.Exchange(ref _saveQueued, 0);
                Save();
            }, token));
        }

        // 讀壞時的原檔備份 <檔名>.bad-<時間>：同內容只留一份、既有備份不覆寫（CreateNew）。永不擲例外。
        private void BackupUnreadable(string path)
        {
            var name = Path.GetFileName(path);
            const string consequence = "這次全部設定使用預設值，之後存檔會用目前的值整份覆寫原檔";
            try
            {
                var original = File.ReadAllBytes(path);
                var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
                foreach (var old in new DirectoryInfo(dir).EnumerateFiles(name + ".bad-*"))
                {
                    if (old.Length == original.Length && File.ReadAllBytes(old.FullName).AsSpan().SequenceEqual(original))
                    {
                        Logger.LogInformation($"[設定] {name}：內容是空的或無法辨識；相同內容已備份過（{old.Name}），這次不再另存。{consequence}。");
                        return;
                    }
                }

                var backup = Path.Combine(dir, $"{name}.bad-{DateTime.Now:yyyyMMdd-HHmmss-fff}");
                using (var fs = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    fs.Write(original);
                }

                Logger.LogInformation($"[設定] {name}：內容是空的或無法辨識；原檔已備份為 {Path.GetFileName(backup)}。{consequence}。");
            }
            catch (Exception e)
            {
                Logger.LogInformation($"[設定] {name}：內容是空的或無法辨識；備份原檔失敗（{e.GetType().Name}: {e.Message}）。{consequence}。");
            }
        }


        /// <summary>
        /// Load a inventories json file, this is no longer the preferred format to store inventories in.
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        public Dictionary<ulong, Dictionary<InventoryCategory, List<InventoryItem>>>? LoadInventoriesJson(string? fileName = null)
        {
            try
            {
                fileName ??= InventoryFile;
                Logger.LogTrace("Loading inventories from " + fileName);
                var cacheFile = new FileInfo(fileName);
                string json = File.ReadAllText(cacheFile.FullName, Encoding.UTF8);
                return JsonConvert.DeserializeObject<Dictionary<ulong, Dictionary<InventoryCategory, List<InventoryItem>>>>(json, new JsonSerializerSettings()
                {
                    DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate,
                    ContractResolver = _minifyResolver
                });
            }
            catch (Exception e)
            {
                Logger.LogError("Error while parsing saved saved inventory data, " + e.Message);
                return null;
            }
        }

        private MinifyResolver _minifyResolver;
        private readonly ContainerAwareCsvLoader _containerAwareCsvLoader;
        private readonly IDalamudPluginInterface _pluginInterfaceService;
        private readonly BackgroundTaskQueue _saveQueue;
        private readonly object _saveLock = new();
        private int _saveQueued;

        [Obsolete]
        public void SaveInventoriesToJson(
            Dictionary<ulong, Dictionary<InventoryCategory, List<InventoryItem>>> savedInventories)
        {
            Logger.LogTrace("Saving inventory data");
            Stopwatch loadConfigStopwatch = new Stopwatch();
            loadConfigStopwatch.Start();
            try
            {
                var items = savedInventories.SelectMany(c => c.Value.SelectMany(d => d.Value)).ToList();
                if (!SaveInventories(items))
                {
                    Logger.LogError($"Failed to save inventories due to a parsing error.");
                }
            }
            catch (Exception e)
            {
                Logger.LogError($"Failed to save inventories due to {e.Message}");
            }
            loadConfigStopwatch.Stop();
            Logger.LogTrace("Took " + loadConfigStopwatch.Elapsed.TotalSeconds + " to save inventories.");

        }

        public bool SaveInventories(List<InventoryItem> items)
        {
            return TrySaveInventories(items) == null;
        }

        public List<InventoryItem> LoadInventoriesFromCsv(out bool success, string? csvPath = null)
        {
            var inventoryCsv = csvPath ?? InventoryCsv;
            if (Path.Exists(inventoryCsv))
            {
                try
                {
                    var items = _containerAwareCsvLoader.LoadInventoryItemsCsv(inventoryCsv,  out _, out _);
                    success = true;
                    return items;
                }
                catch (Exception e)
                {
                    success = false;
                    Logger.LogError("Failed to load inventories from CSV");
                    Logger.LogError(e.Message);
                }
            }
            else
            {
                success = true;
                Logger.LogTrace("Not loading inventories, file does not exist.");
            }


            return new List<InventoryItem>();
        }

        public List<InventoryChange> LoadHistoryFromCsv(out bool success, string? csvPath = null)
        {
            var historyCsv = csvPath ?? HistoryCsv;
            if (Path.Exists(historyCsv))
            {
                try
                {
                    var items = _containerAwareCsvLoader.LoadCsv<InventoryChange>(historyCsv, out _, out _);
                    success = true;
                    return items;
                }
                catch (Exception e)
                {
                    success = false;
                    Logger.LogError(e, "Failed to load history from CSV");
                }
            }
            else
            {
                success = true;
                Logger.LogTrace("Not loading history, file does not exist.");
            }


            return new List<InventoryChange>();
        }

        /// <summary>整份重寫 history.csv：直接覆寫、任何例外回 false（與 <see cref="ContainerAwareCsvLoader.ToCsvRaw{T}"/> 相同）。</summary>
        public bool SaveHistory(List<InventoryChange> changes)
        {
            return CsvRowWriter.ToCsvRaw(changes, Path.Join(_pluginInterfaceService.ConfigDirectory.FullName, "history.csv"));
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // 停用外掛或關閉遊戲時 stoppingToken 被取消是正常收工，刻意吞掉不記成錯誤。
            try
            {
                await BackgroundProcessing(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }

        private async Task BackgroundProcessing(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var workItem =
                    await _saveQueue.DequeueAsync(stoppingToken);

                try
                {
                    await workItem(stoppingToken);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex,
                        "Error occurred executing {WorkItem}.", nameof(workItem));
                }
            }
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            Logger.LogTrace("Stopping service {Type} ({This})", GetType().Name, this);
            Save();
            await base.StopAsync(stoppingToken);
            Logger.LogTrace("Stopped service {Type} ({This})", GetType().Name, this);
        }

        private bool disposed = false;
        public override void Dispose()
        {
            _framework.Update -= OnUpdate;
            disposed = true;
        }

        /// <summary>
        /// 整份重寫 inventories.csv（暫存檔＋取代）。成功回 null；失敗回例外、正式檔保持原內容。
        /// 可在任何執行緒呼叫；兩個呼叫重疊時後到的回失敗（暫存檔是獨占開啟）。
        /// </summary>
        public Exception? TrySaveInventories(List<InventoryItem> items)
        {
            return ContainerAwareCsvLoader.ToCsvRawAtomic(items, Path.Join(_pluginInterfaceService.ConfigDirectory.FullName, "inventories.csv"));
        }
    }
}