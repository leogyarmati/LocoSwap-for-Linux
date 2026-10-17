using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LocoSwap.Properties;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LocoSwap
{
    public partial class MainWindow : Window
    {
        private readonly List<Route> allRoutes = new List<Route>();
        private List<Scenario> allScenarios = new List<Scenario>();
        private bool suppressRouteSelection = false;
        private int scenarioRefreshVersion = 0;
        private FileSystemWatcher sdbWatcher;

        public MainWindow()
        {
            InitializeComponent();
            Title = "LocoSwap " + typeof(MainWindow).Assembly.GetName().Version;

            RouteFilterTextbox.PropertyChanged += (s, e) => { if (e.Property == TextBox.TextProperty) RefreshRouteView(); };
            ScenarioFilterTextbox.PropertyChanged += (s, e) => { if (e.Property == TextBox.TextProperty) RebuildScenarioView(); };
            HidePlayedScenariosCheckbox.PropertyChanged += (s, e) => { if (e.Property == CheckBox.IsCheckedProperty) RebuildScenarioView(); };

            Opened += OnOpened;
            Closed += (s, e) => sdbWatcher?.Dispose();
        }

        private async void OnOpened(object sender, EventArgs e)
        {
            try
            {
                while (!Utilities.IsValidTsPath(Settings.Default.TsPath))
                {
                    if (string.IsNullOrEmpty(Settings.Default.TsPath))
                    {
                        await MessageBox.Show(this, LocoSwap.Language.Resources.msg_first_time, LocoSwap.Language.Resources.msg_message);
                    }
                    if (!await Dialogs.ChooseTsPath(this))
                    {
                        await MessageBox.Show(this, LocoSwap.Language.Resources.msg_ts_path_required, LocoSwap.Language.Resources.msg_message);
                        Close();
                        return;
                    }
                }

                await LoadRoutes();
                ReadScenarioDb();
                StartSdbWatcher();
            }
            catch (Exception ex)
            {
                Log.Error("Startup failed: {0}", ex);
                await MessageBox.Show(this, ex.Message, LocoSwap.Language.Resources.msg_error);
            }
        }

        // ---------------------------------------------------------------- routes

        private async Task LoadRoutes()
        {
            foreach (var old in allRoutes) old.PropertyChanged -= Route_PropertyChanged;
            allRoutes.Clear();

            var routes = await Task.Run(() => Route.ListAllRoutes());
            foreach (Route route in routes)
            {
                route.PropertyChanged += Route_PropertyChanged;
                allRoutes.Add(route);
            }
            RefreshRouteView();
        }

        private void Route_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != "IsFavorite") return;

            Route route = (Route)sender;
            var favorites = Settings.Default.FavoriteRoutes;
            if (route.IsFavorite)
            {
                Log.Debug("Adding {0} to favorite..", route.Name);
                if (!favorites.Contains(route.Id)) favorites.Add(route.Id);
            }
            else
            {
                Log.Debug("Removing {0} from favorite..", route.Name);
                favorites.Remove(route.Id);
            }
            Settings.Default.Save();
            RefreshRouteView();
        }

        private void Favorite_Click(object sender, RoutedEventArgs e)
        {
            if (((Control)sender).DataContext is Route route) route.IsFavorite = !route.IsFavorite;
        }

        private static bool MatchesAllTokens(string filterText, params string[] properties)
        {
            if (string.IsNullOrEmpty(filterText)) return true;
            return filterText.Split(' ').All(token =>
                properties.Any(prop => prop != null && prop.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private void RefreshRouteView()
        {
            var previous = RouteList.SelectedItems.Cast<Route>().ToList();
            string filter = RouteFilterTextbox.Text;

            var view = allRoutes
                .Where(r => MatchesAllTokens(filter, r.Id, r.Name))
                .OrderByDescending(r => r.IsFavorite)
                .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            suppressRouteSelection = true;
            RouteList.ItemsSource = view;
            foreach (var route in previous.Where(view.Contains)) RouteList.SelectedItems.Add(route);
            suppressRouteSelection = false;

            var now = RouteList.SelectedItems.Cast<Route>().ToList();
            if (!now.SequenceEqual(previous)) _ = RefreshScenarioList();
            UpdateButtons();
        }

        private void RouteList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (suppressRouteSelection) return;
            _ = RefreshScenarioList();
            UpdateButtons();
        }

        private void EmptyRouteFilter_Click(object sender, RoutedEventArgs e) => RouteFilterTextbox.Text = "";

        // ------------------------------------------------------------- scenarios

        private async Task RefreshScenarioList()
        {
            int version = ++scenarioRefreshVersion;
            var route = RouteList.SelectedItem as Route;
            if (route == null)
            {
                allScenarios = new List<Scenario>();
                RebuildScenarioView();
                return;
            }

            try
            {
                var list = await Task.Run(() => BuildScenarioList(route));
                if (version != scenarioRefreshVersion) return; // a newer refresh superseded this one
                allScenarios = list;
                RebuildScenarioView();
            }
            catch (Exception ex)
            {
                Log.Error("Could not list scenarios: {0}", ex);
            }
        }

        private static List<Scenario> BuildScenarioList(Route route)
        {
            var result = new List<Scenario>();
            string routeDirectory = Route.GetRouteDirectory(route.Id);
            string scenariosDirectory = Scenario.GetScenariosDirectory(route.Id);

            if (Directory.Exists(scenariosDirectory))
            {
                foreach (string directory in Directory.GetDirectories(scenariosDirectory))
                {
                    string id = new DirectoryInfo(directory).Name;
                    string xmlPath = Path.Combine(directory, "ScenarioProperties.xml");
                    string xmlPathIfArchived = Path.Combine(directory, "ScenarioPropertiesLocoSwapOff.xml");
                    string binPath = Path.Combine(directory, "Scenario.bin");
                    if (!File.Exists(binPath) || (!File.Exists(xmlPath) && !File.Exists(xmlPathIfArchived))) continue;
                    try
                    {
                        result.Add(new Scenario(route, id, ""));
                    }
                    catch (Exception e)
                    {
                        Log.Error("Could not load scenario {0}: {1}", id, e.Message);
                    }
                }
            }

            string[] allowedExtensions = new[] { ".ap", ".ap.LSoff" };
            string[] apFiles = Fs.GetFiles(routeDirectory, "*")
                .Where(file => allowedExtensions.Any(ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase))).ToArray();
            Regex scenarioPropFileRegex = new Regex(@"^(Scenarios/([a-f\d\-]{36})/)ScenarioProperties\.xml$");
            foreach (string apPath in apFiles)
            {
                try
                {
                    using (ZipArchive zipFile = ZipUtil.Open(apPath))
                    {
                        foreach (ZipArchiveEntry entry in zipFile.Entries)
                        {
                            Match match = scenarioPropFileRegex.Match(entry.FullName);
                            if (match.Success && !File.Exists(Path.Combine(routeDirectory, "Scenarios", match.Groups[2].Value, "ScenarioProperties.xml")))
                            {
                                try
                                {
                                    result.Add(new Scenario(route, match.Groups[2].Value, apPath));
                                }
                                catch (Exception e)
                                {
                                    Log.Error("Could not load scenario {0} from {1}: {2}", match.Groups[2].Value, apPath, e.Message);
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.Error("Couldn't read " + apPath + " for scenarios, " + e.Message);
                }
            }
            return result;
        }

        private bool ScenarioMatchesFilter(Scenario s)
        {
            if (HidePlayedScenariosCheckbox.IsChecked == true &&
                (s.Completion == ScenarioDb.ScenarioCompletion.CompletedSuccessfully ||
                 s.Completion == ScenarioDb.ScenarioCompletion.CompletedFailed))
            {
                return false;
            }
            return MatchesAllTokens(ScenarioFilterTextbox.Text, s.Id, s.Name, s.Description, s.PlayerTrainName, s.Author);
        }

        private void RebuildScenarioView()
        {
            var previous = ScenarioList.SelectedItems.Cast<Scenario>().ToList();
            var view = allScenarios
                .Where(ScenarioMatchesFilter)
                .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            ScenarioList.ItemsSource = view;
            foreach (var s in previous.Where(view.Contains)) ScenarioList.SelectedItems.Add(s);
            UpdateButtons();
        }

        private void ScenarioList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

        private void UpdateButtons()
        {
            int scenarios = ScenarioList.SelectedItems.Count;
            int routes = RouteList.SelectedItems.Count;
            EditScenarioButton.IsEnabled = scenarios == 1;
            OpenScenarioDirButton.IsEnabled = scenarios > 0;
            DeleteScenariosButton.IsEnabled = scenarios > 0;
            ArchiveToggleButton.IsEnabled = routes > 0;
            ArchiveRestButton.IsEnabled = routes > 0;
        }

        private void EmptyScenarioFilter_Click(object sender, RoutedEventArgs e) => ScenarioFilterTextbox.Text = "";

        private void EditScenarioButton_Click(object sender, RoutedEventArgs e)
        {
            if (RouteList.SelectedItem is Route route && ScenarioList.SelectedItem is Scenario scenario)
            {
                OpenEditor(route.Id, scenario);
            }
        }

        private void ScenarioList_DoubleTapped(object sender, TappedEventArgs e)
        {
            if ((e.Source as Control)?.DataContext is Scenario scenario && RouteList.SelectedItem is Route route)
            {
                OpenEditor(route.Id, scenario);
            }
        }

        private void OpenEditor(string routeId, Scenario scenario)
        {
            new ScenarioEditWindow(routeId, scenario).Show();
        }

        private void OpenScenarioDirButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (Scenario scenario in ScenarioList.SelectedItems.Cast<Scenario>().ToList())
            {
                if (scenario.ApFileName == "") Utilities.OpenPath(scenario.ScenarioDirectory);
            }
        }

        private async void Delete_Scenarios_Click(object sender, RoutedEventArgs e)
        {
            var answer = await MessageBox.Show(this,
                LocoSwap.Language.Resources.scenario_delete_prompt_message,
                LocoSwap.Language.Resources.scenario_delete_prompt_title, MsgButtons.YesNoCancel);
            if (answer != MsgResult.Yes) return;

            foreach (Scenario scenario in ScenarioList.SelectedItems.Cast<Scenario>().ToList())
            {
                try { scenario.Delete(); }
                catch (Exception ex) { Log.Error("Could not delete scenario {0}: {1}", scenario.Id, ex.Message); }
            }
            await RefreshScenarioList();
        }

        // --------------------------------------------------------------- archive

        private async void ToggleArchiveRoutes_Click(object sender, RoutedEventArgs e)
        {
            if (ScenarioDb.dbState != ScenarioDb.DBState.Loaded)
            {
                var answer = await MessageBox.Show(this,
                    LocoSwap.Language.Resources.archive_without_db_loaded_prompt_message,
                    LocoSwap.Language.Resources.archive_without_db_loaded_prompt_title, MsgButtons.YesNoCancel);
                if (answer != MsgResult.Yes) return;
            }
            foreach (Route route in RouteList.SelectedItems.Cast<Route>().ToList())
            {
                try { route.ToggleArchive(); }
                catch (Exception ex) { Log.Error("Could not toggle archive for {0}: {1}", route.Id, ex.Message); }
            }
        }

        private void ArchiveAllButSelectedRoutes_Click(object sender, RoutedEventArgs e)
        {
            var selected = RouteList.SelectedItems.Cast<Route>().ToList();
            foreach (Route route in allRoutes.ToList())
            {
                if (!route.IsArchived && !selected.Contains(route) && !(Settings.Default.DoNotAutoArchiveWorkshopRoutes && route.IsWorkshop)
                    ||
                    route.IsArchived && selected.Contains(route))
                {
                    try { route.ToggleArchive(); }
                    catch (Exception ex) { Log.Error("Could not toggle archive for {0}: {1}", route.Id, ex.Message); }
                }
            }
        }

        // ------------------------------------------------------------ SDBCache

        public async void ReadScenarioDb()
        {
            try
            {
                var running = Task.Run(() => ScenarioDb.ParseScenarioDb());
                // show "loading" in the completion column right away
                foreach (var s in allScenarios) s.NotifyCompletionChanged();
                await running;

                Log.Debug("SDB is read, refreshing scenarios list");
                foreach (var s in allScenarios) s.NotifyCompletionChanged();
                RebuildScenarioView();
            }
            catch (Exception ex)
            {
                Log.Error("Reading scenario DB failed: {0}", ex);
            }
        }

        private void StartSdbWatcher()
        {
            try
            {
                string contentDir = Fs.ResolveCI(Fs.Combine(Settings.Default.TsPath, "Content"));
                sdbWatcher = new FileSystemWatcher(contentDir)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                    Filter = "SDBCache.bin",
                    IncludeSubdirectories = false
                };
                sdbWatcher.Changed += OnSdbCacheUpdate;
                sdbWatcher.Created += OnSdbCacheUpdate;
                sdbWatcher.EnableRaisingEvents = true;
            }
            catch (Exception e)
            {
                Log.Warning("Could not watch SDBCache.bin: {0}", e.Message);
            }
        }

        private void OnSdbCacheUpdate(object sender, FileSystemEventArgs e)
        {
            try
            {
                var info = new FileInfo(e.FullPath);
                Log.Information("SDBCache.bin updated, size = {0}", info.Length);
                // When TS rewrites the SDBCache, a first event is triggered while the file is at 0 byte: ignore
                if (info.Length > 0) Dispatcher.UIThread.Post(() => ReadScenarioDb());
            }
            catch (Exception ex)
            {
                Log.Debug("SDBCache event ignored: {0}", ex.Message);
            }
        }

        // -------------------------------------------------------------- misc

        private async void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            bool previousCheck = Settings.Default.CheckScenarioConsists;
            string previousTsPath = Settings.Default.TsPath;
            string previousRunner = Settings.Default.RunnerCommand + "|" + Settings.Default.RunnerEnv;

            await new SettingsWindow().ShowDialog(this);

            if (previousTsPath != Settings.Default.TsPath)
            {
                VehicleAvailibility.ClearTable();
                await LoadRoutes();
                ReadScenarioDb();
            }
            else if (previousCheck != Settings.Default.CheckScenarioConsists ||
                     previousRunner != Settings.Default.RunnerCommand + "|" + Settings.Default.RunnerEnv)
            {
                await RefreshScenarioList();
            }
        }

        private void OpenManual_Click(object sender, RoutedEventArgs e) => Utilities.OpenManual();
    }
}
