using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace LocoSwap.Properties
{
    /// <summary>
    /// User settings, persisted as JSON in $XDG_CONFIG_HOME/LocoSwap/settings.json
    /// (replaces the .NET Framework user.config based Settings class).
    /// </summary>
    public class Settings
    {
        public string TsPath { get; set; } = "";
        public SwapPreset Preset { get; set; } = new SwapPreset();
        public string Language { get; set; } = "";
        public List<string> FavoriteRoutes { get; set; } = new List<string>();
        public string ScenarioNameSuffix { get; set; } = "[LoSw]";
        public bool CheckScenarioConsists { get; set; } = false;
        public bool DoNotAutoArchiveWorkshopRoutes { get; set; } = true;

        /// <summary>
        /// Command used to launch serz.exe on non-Windows systems, e.g. "wine" or
        /// "/path/to/Proton/proton run". Empty = "wine" (or direct execution on Windows).
        /// </summary>
        public string RunnerCommand { get; set; } = "";

        /// <summary>Extra environment variables for the runner, one KEY=VALUE per line (or separated by ';').</summary>
        public string RunnerEnv { get; set; } = "";

        // ---------------------------------------------------------------

        private static readonly object Sync = new object();
        private static Settings _default;
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };

        public static string ConfigDir
        {
            get
            {
                string baseDir = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                if (string.IsNullOrEmpty(baseDir))
                    baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
                return Path.Combine(baseDir, "LocoSwap");
            }
        }

        public static string CacheDir
        {
            get
            {
                string baseDir = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
                if (string.IsNullOrEmpty(baseDir))
                    baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
                return Path.Combine(baseDir, "LocoSwap");
            }
        }

        public static string SettingsFile => Path.Combine(ConfigDir, "settings.json");

        public static Settings Default
        {
            get
            {
                lock (Sync)
                {
                    if (_default == null) _default = Load();
                    return _default;
                }
            }
        }

        /// <summary>For tests: replace the singleton.</summary>
        public static void Override(Settings settings)
        {
            lock (Sync) { _default = settings; }
        }

        private static Settings Load()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsFile));
                    if (loaded != null)
                    {
                        if (loaded.Preset == null) loaded.Preset = new SwapPreset();
                        if (loaded.Preset.List == null) loaded.Preset.List = new System.Collections.ObjectModel.ObservableCollection<SwapPresetItem>();
                        if (loaded.FavoriteRoutes == null) loaded.FavoriteRoutes = new List<string>();
                        if (loaded.TsPath == null) loaded.TsPath = "";
                        if (loaded.Language == null) loaded.Language = "";
                        if (loaded.ScenarioNameSuffix == null) loaded.ScenarioNameSuffix = "";
                        return loaded;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning("Could not read settings file: {0}", e.Message);
            }
            return new Settings();
        }

        public void Save()
        {
            lock (Sync)
            {
                try
                {
                    Directory.CreateDirectory(ConfigDir);
                    string tmp = SettingsFile + ".tmp";
                    File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
                    File.Move(tmp, SettingsFile, true);
                }
                catch (Exception e)
                {
                    Log.Warning("Could not save settings: {0}", e.Message);
                }
            }
        }
    }
}
