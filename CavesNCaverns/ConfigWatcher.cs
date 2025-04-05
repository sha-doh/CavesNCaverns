using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace CavesAndCaverns.Config
{
    public class ConfigWatcher
    {
        private readonly ICoreAPI api;
        private readonly ConfigManager configManager;
        private readonly Action<CavesConfig> onConfigReloaded;
        private FileSystemWatcher fileSystemWatcher;
        private DateTime lastModifiedTime;
        private const int MaxRetries = 3;
        private const int RetryDelayMs = 100;
        private const string BackupPath = "ModConfig/CavesAndCaverns_backup.json";

        public ConfigWatcher(ICoreAPI api, ConfigManager configManager, Action<CavesConfig> onConfigReloaded)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
            this.onConfigReloaded = onConfigReloaded ?? throw new ArgumentNullException(nameof(onConfigReloaded));
        }

        public void Start()
        {
            string configPath = Path.Combine(api.DataBasePath, "ModConfig", "CavesAndCaverns.json");
            string configDir = Path.GetDirectoryName(configPath);

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    if (!Directory.Exists(configDir))
                    {
                        Directory.CreateDirectory(configDir);
                    }

                    fileSystemWatcher = new FileSystemWatcher(configDir)
                    {
                        Filter = "CavesAndCaverns.json",
                        EnableRaisingEvents = true,
                        NotifyFilter = NotifyFilters.LastWrite
                    };

                    fileSystemWatcher.Changed += OnConfigFileChanged;
                    lastModifiedTime = File.Exists(configPath) ? File.GetLastWriteTime(configPath) : DateTime.MinValue;

                    api.Logger.Notification("[CavesAndCaverns] Config watcher started, watching {0}", configPath);
                    break;
                }
                catch (Exception ex)
                {
                    if (attempt == MaxRetries)
                    {
                        api.Logger.Error("[CavesAndCaverns] Failed to start config watcher after {0} attempts: {1}", MaxRetries, ex.Message);
                    }
                    else
                    {
                        Task.Delay(RetryDelayMs).Wait();
                    }
                }
            }
        }

        private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
        {
            DateTime currentModifiedTime = File.GetLastWriteTime(e.FullPath);
            if ((currentModifiedTime - lastModifiedTime).TotalSeconds < 1) return;

            lastModifiedTime = currentModifiedTime;

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    BackupConfig();
                    configManager.Load();
                    onConfigReloaded?.Invoke(configManager.Config);
                    api.Logger.Notification("[CavesAndCaverns] Config reloaded due to file change: {0}", e.FullPath);

                    // Notify clients of config change (server-side)
                    if (api.Side == EnumAppSide.Server)
                    {
                        NotifyClientsConfigChange();
                    }

                    break;
                }
                catch (Exception ex)
                {
                    if (attempt == MaxRetries)
                    {
                        api.Logger.Error("[CavesAndCaverns] Failed to reload config after {0} attempts: {1}, attempting rollback", MaxRetries, ex.Message);
                        RollbackConfig();
                    }
                    else
                    {
                        Task.Delay(RetryDelayMs).Wait();
                    }
                }
            }
        }

        private void BackupConfig()
        {
            try
            {
                string configPath = Path.Combine(api.DataBasePath, "ModConfig", "CavesAndCaverns.json");
                string backupPath = Path.Combine(api.DataBasePath, BackupPath);
                if (File.Exists(configPath))
                {
                    File.Copy(configPath, backupPath, true);
                    api.Logger.Event("[CavesAndCaverns] Backed up config to {0}", backupPath);
                }
            }
            catch (Exception ex)
            {
                api.Logger.Error("[CavesAndCaverns] Failed to backup config: {0}", ex.Message);
            }
        }

        private void RollbackConfig()
        {
            try
            {
                string configPath = Path.Combine(api.DataBasePath, "ModConfig", "CavesAndCaverns.json");
                string backupPath = Path.Combine(api.DataBasePath, BackupPath);
                if (File.Exists(backupPath))
                {
                    File.Copy(backupPath, configPath, true);
                    configManager.Load();
                    onConfigReloaded?.Invoke(configManager.Config);
                    api.Logger.Event("[CavesAndCaverns] Restored config from backup after failed reload");
                }
                else
                {
                    api.Logger.Error("[CavesAndCaverns] Failed to restore config: Backup file not found");
                }
            }
            catch (Exception ex)
            {
                api.Logger.Error("[CavesAndCaverns] Failed to rollback config: {0}", ex.Message);
            }
        }

        private void NotifyClientsConfigChange()
        {
            if (api.Side != EnumAppSide.Server)
            {
                return;
            }

            var serverApi = api as ICoreServerAPI;
            if (serverApi != null)
            {
                serverApi.SendMessageToGroup(GlobalConstants.GeneralChatGroup, "[CavesAndCaverns] Server config updated. Some settings may require a client restart.", EnumChatType.Notification);
            }

            api.Logger.Event("[CavesAndCaverns] Notified clients of config change");
        }
    }
}