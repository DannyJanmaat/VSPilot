using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Settings;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using VSPilot.Common.Models;
using VSPilot.Common.Exceptions;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio;

namespace VSPilot.Core.Services
{
    public class ConfigurationService : IDisposable
    {
        private const string CollectionPath = "VSPilot";
        private const string SettingsKey = "Settings";

        private readonly AsyncPackage _package;
        private readonly ILogger<ConfigurationService> _logger;
        private bool _disposed;

        public ConfigurationService(AsyncPackage package, ILogger<ConfigurationService> logger)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<VSPilotSettings> LoadSettingsAsync()
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var settingsStore = await GetSettingsStoreAsync();

                if (!settingsStore.CollectionExists(CollectionPath))
                {
                    _logger.LogInformation("No existing settings found. Using defaults.");
                    return new VSPilotSettings();
                }

                return new VSPilotSettings
                {
                    AutoBuildAfterChanges = SafeGetBoolean(settingsStore, nameof(VSPilotSettings.AutoBuildAfterChanges), true),
                    AutoRunTests = SafeGetBoolean(settingsStore, nameof(VSPilotSettings.AutoRunTests), true),
                    AutoFixErrors = SafeGetBoolean(settingsStore, nameof(VSPilotSettings.AutoFixErrors), true),
                    MaxAutoFixAttempts = SafeGetInt32(settingsStore, nameof(VSPilotSettings.MaxAutoFixAttempts), 3),
                    ShowDetailedLogs = SafeGetBoolean(settingsStore, nameof(VSPilotSettings.ShowDetailedLogs), false),
                    PreferredFolderStructure = SafeGetStringArray(settingsStore, nameof(VSPilotSettings.PreferredFolderStructure),
                        new[] { "Models", "ViewModels", "Views", "Services", "Interfaces", "Helpers" })
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load settings");
                throw new AutomationException("Failed to load settings", ex);
            }
        }

        public async Task SaveSettingsAsync(VSPilotSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var settingsStore = await GetSettingsStoreAsync();

                EnsureCollectionExists(settingsStore, CollectionPath);

                // Save each setting
                settingsStore.SetBoolean(CollectionPath, nameof(VSPilotSettings.AutoBuildAfterChanges), settings.AutoBuildAfterChanges);
                settingsStore.SetBoolean(CollectionPath, nameof(VSPilotSettings.AutoRunTests), settings.AutoRunTests);
                settingsStore.SetBoolean(CollectionPath, nameof(VSPilotSettings.AutoFixErrors), settings.AutoFixErrors);
                settingsStore.SetInt32(CollectionPath, nameof(VSPilotSettings.MaxAutoFixAttempts), settings.MaxAutoFixAttempts);
                settingsStore.SetBoolean(CollectionPath, nameof(VSPilotSettings.ShowDetailedLogs), settings.ShowDetailedLogs);

                // Save array as concatenated string
                if (settings.PreferredFolderStructure != null)
                {
                    settingsStore.SetString(CollectionPath, nameof(VSPilotSettings.PreferredFolderStructure),
                        string.Join("|", settings.PreferredFolderStructure));
                }

                _logger.LogInformation("Settings saved successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save settings");
                throw new AutomationException("Failed to save settings", ex);
            }
        }

        private async Task<WritableSettingsStore> GetSettingsStoreAsync()
        {
            var settingsManager = await _package.GetServiceAsync(typeof(SVsSettingsManager)) as SettingsManager;
            if (settingsManager == null)
            {
                throw new AutomationException("Could not get settings manager");
            }

            return settingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);
        }

        private void EnsureCollectionExists(WritableSettingsStore store, string collectionPath)
        {
            if (!store.CollectionExists(collectionPath))
            {
                store.CreateCollection(collectionPath);
            }
        }

        private bool SafeGetBoolean(WritableSettingsStore store, string propertyName, bool defaultValue)
        {
            try
            {
                return store.GetBoolean(CollectionPath, propertyName);
            }
            catch
            {
                return defaultValue;
            }
        }

        private int SafeGetInt32(WritableSettingsStore store, string propertyName, int defaultValue)
        {
            try
            {
                return store.GetInt32(CollectionPath, propertyName);
            }
            catch
            {
                return defaultValue;
            }
        }

        private string[] SafeGetStringArray(WritableSettingsStore store, string propertyName, string[] defaultValue)
        {
            try
            {
                var value = store.GetString(CollectionPath, propertyName);
                return string.IsNullOrEmpty(value) ? defaultValue : value.Split('|');
            }
            catch
            {
                return defaultValue;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Cleanup any managed resources if needed
                }
                _disposed = true;
            }
        }
    }
}