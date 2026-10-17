using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LocoSwap.Properties;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LocoSwap
{
    public partial class ScenarioEditWindow : Window
    {
        public class ScenarioViewModel : ModelBase
        {
            private string _loadingInformation = "";
            private int _loadingProgress = 0;
            private bool _vehicleScanInProgress = false;

            public Route Route { get; set; }
            public Scenario Scenario { get; set; }

            public string LoadingInformation
            {
                get => _loadingInformation;
                set => SetProperty(ref _loadingInformation, value);
            }
            public int LoadingProgress
            {
                get => _loadingProgress;
                set
                {
                    if (SetProperty(ref _loadingProgress, value))
                    {
                        OnPropertyChanged(new PropertyChangedEventArgs(nameof(LoadingGridVisible)));
                        OnPropertyChanged(new PropertyChangedEventArgs(nameof(SaveButtonEnabled)));
                    }
                }
            }
            public bool LoadingGridVisible => LoadingProgress < 100;
            public bool SaveButtonEnabled => LoadingProgress >= 100;
            public bool VehicleScanInProgress
            {
                get => _vehicleScanInProgress;
                set
                {
                    if (SetProperty(ref _vehicleScanInProgress, value))
                        OnPropertyChanged(new PropertyChangedEventArgs(nameof(ScanEnabled)));
                }
            }
            public bool ScanEnabled => !VehicleScanInProgress;

            public ObservableCollection<Consist> Consists { get; } = new ObservableCollection<Consist>();
            public ObservableCollection<ScenarioVehicle> Vehicles { get; } = new ObservableCollection<ScenarioVehicle>();
            public ObservableCollection<DirectoryItem> Directories { get; } = new ObservableCollection<DirectoryItem>();
        }

        private string RouteId;
        private readonly ScenarioViewModel ViewModel = new ScenarioViewModel();
        private CancellationTokenSource ScanCancellationTokenSource;
        private SwapPresetWindow PresetWindow;

        // Vehicles found by the scanner: filled from worker threads, shown through a UI timer
        private readonly ConcurrentQueue<AvailableVehicle> pendingVehicles = new ConcurrentQueue<AvailableVehicle>();
        private readonly List<AvailableVehicle> allAvailableVehicles = new List<AvailableVehicle>();
        private readonly HashSet<string> availableVehicleKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private DispatcherTimer scanTimer;

        // Needed by the XAML loader
        public ScenarioEditWindow()
        {
            InitializeComponent();
        }

        public ScenarioEditWindow(string routeId, Scenario scenario) : this()
        {
            RouteId = routeId;
            DataContext = ViewModel;
            ViewModel.Route = new Route(RouteId);
            ViewModel.Scenario = scenario;

            Title = string.Format(LocoSwap.Language.Resources.edit_scenario_title, scenario.Name);
            RouteNameBox.Text = ViewModel.Route.Name;
            ScenarioNameBox.Text = scenario.Name;
            ConsistListBox.ItemsSource = ViewModel.Consists;
            VehicleListBox.ItemsSource = ViewModel.Vehicles;
            DirectoryTree.ItemsSource = ViewModel.Directories;

            AvailableVehicleFilterTextbox.PropertyChanged += (s, e) => { if (e.Property == TextBox.TextProperty) RefreshAvailableView(); };
            Closed += Window_Closed;

            VehicleAvailibility.ClearTable();
            UpdateButtons();
            ReadScenario();
        }

        // ------------------------------------------------------------- loading

        public async void ReadScenario()
        {
            IProgress<int> progress = new Progress<int>(value => ViewModel.LoadingProgress = value);
            ViewModel.LoadingInformation = LocoSwap.Language.Resources.reading_scenario_files;

            try
            {
                var readConsistsTask = Task.Run(() =>
                {
                    ViewModel.Scenario.ReadScenario(progress);
                    return ViewModel.Scenario.GetConsists(progress);
                });
                var populateDirectoryTask = Task.Run(() =>
                {
                    DirectoryItem rootNode = new DirectoryItem
                    {
                        Name = "Assets",
                        Path = Fs.ResolveCI(Fs.Combine(Settings.Default.TsPath, "Assets"))
                    };
                    rootNode.PopulateSubDirectories();
                    return rootNode.SubDirectories.ToList();
                });

                var consists = await readConsistsTask;
                var directories = await populateDirectoryTask;

                ViewModel.Consists.Clear();
                foreach (Consist consist in consists) ViewModel.Consists.Add(consist);
                foreach (DirectoryItem item in directories) ViewModel.Directories.Add(item);
                progress.Report(100);
                ViewModel.LoadingProgress = 100;
                if (ViewModel.Consists.Count > 0) ConsistListBox.SelectedIndex = 0;
            }
            catch (Exception e)
            {
                Log.Error("Could not read scenario: {0}", e);
                ViewModel.LoadingProgress = 100;
                await MessageBox.Show(this,
                    string.Format(LocoSwap.Language.Resources.msg_conversion_failed, e.Message),
                    LocoSwap.Language.Resources.msg_error);
                Close();
            }
        }

        // ------------------------------------------------- consist / vehicle lists

        private Consist SelectedConsist => ConsistListBox.SelectedItem as Consist;

        private void ReloadVehicleList()
        {
            ViewModel.Vehicles.Clear();
            var consist = SelectedConsist;
            if (consist == null) return;
            foreach (ScenarioVehicle vehicle in consist.Vehicles) ViewModel.Vehicles.Add(vehicle);
        }

        private void ConsistListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ReloadVehicleList();
            UpdateButtons();
            UpdateSelectedVehicleInfo();
        }

        private void VehicleListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateButtons();
            UpdateSelectedVehicleInfo();

            // When an unfound scenario vehicle is selected, hint the user by selecting the vehicle's
            // asset folder in the tree: they may already own a similar vehicle there.
            if (VehicleListBox.SelectedItem is ScenarioVehicle selected && selected.Exists != VehicleExistance.Found)
            {
                SelectDirectoryHint(selected);
            }
        }

        private void UpdateSelectedVehicleInfo()
        {
            var v = VehicleListBox.SelectedItem as ScenarioVehicle;
            SelNameBox.Text = v?.DisplayName ?? "";
            SelNumberBox.Text = v?.Number ?? "";
            SelPathBox.Text = v == null ? "" : "\\Assets\\" + v.DisplayXmlPath;
            SelFlippedBox.IsChecked = v?.Flipped ?? false;
            InvolvedText.IsVisible = v != null && v.IsInvolvedInConsistOperation;
            SelImage.Source = v == null ? null : Converters.VehicleImage(v);
        }

        private void SelectDirectoryHint(ScenarioVehicle vehicle)
        {
            string[] parts = vehicle.XmlPath.Split('\\');
            if (parts.Length < 2) return;

            IEnumerable<DirectoryItem> level = ViewModel.Directories;
            DirectoryItem deepest = null;
            foreach (string part in parts)
            {
                var next = level.FirstOrDefault(d => d.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
                if (next == null) break;
                next.PopulateSubDirectories();
                next.IsExpanded = true;
                deepest = next;
                level = next.SubDirectories;
            }
            if (deepest != null) deepest.IsSelected = true;
        }

        private void DirectoryTree_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key < Key.A || e.Key > Key.Z) return;
            char ch = (char)('a' + (e.Key - Key.A));
            var dir = ViewModel.Directories.FirstOrDefault(d => d.Name.Length > 0 && char.ToLowerInvariant(d.Name[0]) == ch);
            if (dir != null) dir.IsSelected = true;
        }

        // ---------------------------------------------------------- button state

        private void UpdateButtons()
        {
            int selectedVehicles = VehicleListBox.SelectedItems.Count;
            bool hasAvailable = AvailableVehicleListBox.SelectedItem != null;
            ChangeNumberButton.IsEnabled = selectedVehicles == 1;
            FlipButton.IsEnabled = selectedVehicles > 0;
            RemoveButton.IsEnabled = selectedVehicles > 0;
            NumberListButton.IsEnabled = hasAvailable;
            ClearButton.IsEnabled = allAvailableVehicles.Count > 0;
        }

        private Task<MsgResult> Info(string message, string title) => MessageBox.Show(this, message, title);
        private Task<MsgResult> Warn(string message) => MessageBox.Show(this, message, LocoSwap.Language.Resources.msg_message);

        // -------------------------------------------------- scanning for vehicles

        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            DirectoryItem selected = DirectoryTree.SelectedItem as DirectoryItem;
            if (selected == null)
            {
                await Warn(LocoSwap.Language.Resources.msg_no_directory_selected);
                return;
            }
            LookupVehicles(selected.Path);
        }

        private void DrainPendingVehicles()
        {
            bool added = false;
            while (pendingVehicles.TryDequeue(out var vehicle))
            {
                string key = vehicle.DisplayXmlPath;
                if (!availableVehicleKeys.Add(key)) continue;
                allAvailableVehicles.Add(vehicle);
                added = true;
            }
            if (added) RefreshAvailableView();
        }

        private void RefreshAvailableView()
        {
            string filter = AvailableVehicleFilterTextbox.Text;
            var tokens = string.IsNullOrEmpty(filter) ? new string[0] : filter.Split(' ');
            var selected = AvailableVehicleListBox.SelectedItem as AvailableVehicle;

            var view = allAvailableVehicles
                .Where(v => tokens.All(t => v.DisplayName != null && v.DisplayName.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderBy(v => v.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            AvailableVehicleListBox.ItemsSource = view;
            if (selected != null && view.Contains(selected)) AvailableVehicleListBox.SelectedItem = selected;
            UpdateButtons();
        }

        private async void LookupVehicles(string path)
        {
            ScanCancellationTokenSource = new CancellationTokenSource();
            var token = ScanCancellationTokenSource.Token;
            IProgress<int> progress = new Progress<int>(value => ViewModel.LoadingProgress = value == 0 ? value : Math.Max(value, ViewModel.LoadingProgress));
            ViewModel.VehicleScanInProgress = true;
            ViewModel.LoadingProgress = 0;

            scanTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            scanTimer.Tick += (s, e) => DrainPendingVehicles();
            scanTimer.Start();

            string assetsDir = Fs.ResolveCI(Fs.Combine(Settings.Default.TsPath, "Assets"));
            ViewModel.LoadingInformation = LocoSwap.Language.Resources.scanning_bin_files;
            var startDateTime = DateTime.Now;

            List<string> files = null;
            List<string> apFiles = null;
            bool cancelled = false;

            try
            {
                var binTask = Task.Run(() =>
                {
                    files = Fs.GetFiles(path, "*.bin", SearchOption.AllDirectories).ToList();
                    apFiles = Fs.GetFiles(path, "*.ap", SearchOption.AllDirectories).ToList();
                    progress.Report(0);
                    Parallel.ForEach(files.Select((value, i) => (value, i)), (item) =>
                    {
                        token.ThrowIfCancellationRequested();
                        string binPath = Fs.RelativeToWin(assetsDir, item.value);
                        try
                        {
                            Log.Debug("Try: {0}", binPath);
                            pendingVehicles.Enqueue(new AvailableVehicle(binPath));
                        }
                        catch (Exception ex)
                        {
                            Log.Debug("{0}: {1}", ex.Message, binPath);
                        }
                        progress.Report((int)Math.Ceiling((float)item.i / files.Count * 100));
                    });
                }, token);
                await binTask;
                Log.Debug(".bin scan took {0} seconds", (DateTime.Now - startDateTime).TotalSeconds);
            }
            catch (Exception)
            {
                Log.Debug("Operation cancelled");
                cancelled = true;
            }

            if (!cancelled)
            {
                ViewModel.LoadingProgress = 0;
                ViewModel.LoadingInformation = LocoSwap.Language.Resources.scanning_ap_files;
                startDateTime = DateTime.Now;
                try
                {
                    var apTask = Task.Run(() =>
                    {
                        foreach (var item in apFiles.Select((value, i) => (value, i)))
                        {
                            token.ThrowIfCancellationRequested();
                            Log.Debug("Trying ap file {0}", item.value);
                            List<string> binNames;
                            try
                            {
                                using (var zipFile = ZipUtil.Open(item.value))
                                {
                                    var entries = zipFile.Entries.Where(entry => entry.FullName.StartsWith("railvehicles/", StringComparison.OrdinalIgnoreCase) && entry.FullName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)).ToList();
                                    if (!entries.Any())
                                    {
                                        entries = zipFile.Entries.Where(entry => entry.FullName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)).ToList();
                                    }
                                    binNames = entries.Select(entry => entry.FullName).ToList();
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Debug("Could not read {0}: {1}", item.value, ex.Message);
                                continue;
                            }

                            var baseProgress = (int)Math.Ceiling((float)item.i / apFiles.Count * 100);
                            string basePath = Fs.RelativeToWin(assetsDir, Path.GetDirectoryName(item.value));
                            Log.Debug("There are {0} bin entries", binNames.Count);
                            Parallel.ForEach(binNames.Select((value, i) => (value, i)), (binItem) =>
                            {
                                token.ThrowIfCancellationRequested();
                                string binPath = basePath + "\\" + binItem.value.Replace('/', '\\');
                                try
                                {
                                    Log.Debug("Try {0}", binPath);
                                    pendingVehicles.Enqueue(new AvailableVehicle(binPath));
                                }
                                catch (Exception)
                                {
                                }
                                var ownProgress = (int)Math.Ceiling((float)binItem.i / binNames.Count * 100 / apFiles.Count);
                                progress.Report(baseProgress + ownProgress);
                            });
                        }
                    }, token);
                    await apTask;
                    Log.Debug(".ap scan took {0} seconds", (DateTime.Now - startDateTime).TotalSeconds);
                }
                catch (Exception apException)
                {
                    Log.Warning("Exception caught during .ap scan: {0} - {1}", apException.GetType().ToString(), apException.Message);
                }
            }

            scanTimer.Stop();
            DrainPendingVehicles();
            ViewModel.LoadingProgress = 100;
            ViewModel.VehicleScanInProgress = false;
            UpdateButtons();
        }

        private void CancelScanningButton_Click(object sender, RoutedEventArgs e) => ScanCancellationTokenSource?.Cancel();

        private void AvailableVehiclesClearButton_Click(object sender, RoutedEventArgs e)
        {
            while (pendingVehicles.TryDequeue(out _)) { }
            allAvailableVehicles.Clear();
            availableVehicleKeys.Clear();
            RefreshAvailableView();
        }

        private void EmptyAvailableVehicleFilter_Click(object sender, RoutedEventArgs e) => AvailableVehicleFilterTextbox.Text = "";

        private void AvailableVehicleListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var v = AvailableVehicleListBox.SelectedItem as AvailableVehicle;
            NewNameBox.Text = v?.DisplayName ?? "";
            NewPathBox.Text = v == null ? "" : "\\Assets\\" + v.DisplayXmlPath;
            NewImage.Source = v == null ? null : Converters.VehicleImage(v);
            UpdateButtons();
        }

        // ------------------------------------------------------------ editing

        private async void InsertVehicle(bool after)
        {
            if (AvailableVehicleListBox.SelectedItem == null || VehicleListBox.SelectedItem == null)
            {
                await Warn(LocoSwap.Language.Resources.msg_no_vehicle_selected);
                return;
            }

            var insertIndex = after ? (VehicleListBox.SelectedIndex + 1) : VehicleListBox.SelectedIndex;
            Consist consist = SelectedConsist;
            AvailableVehicle newVehicle = (AvailableVehicle)AvailableVehicleListBox.SelectedItem;

            try
            {
                ScenarioVehicle vehicle = ViewModel.Scenario.InsertVehicle(consist.Idx, insertIndex, newVehicle);
                consist.Vehicles.Insert(insertIndex, vehicle);
                for (var i = 0; i < consist.Vehicles.Count; i++) consist.Vehicles[i].Idx = i;
                ReloadVehicleList();
            }
            catch (Exception ex)
            {
                Log.Error("Insert failed: {0}", ex);
                await MessageBox.Show(this, ex.Message, LocoSwap.Language.Resources.msg_error);
            }
        }

        private void InsertBeforeButton_Click(object sender, RoutedEventArgs e) => InsertVehicle(false);
        private void InsertAfterButton_Click(object sender, RoutedEventArgs e) => InsertVehicle(true);

        private async void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = VehicleListBox.SelectedItems.Cast<ScenarioVehicle>().ToList();
            if (selected.Count == 0)
            {
                await Warn(LocoSwap.Language.Resources.msg_no_vehicle_selected);
                return;
            }

            Consist consist = SelectedConsist;
            if (consist.Vehicles.Count == selected.Count)
            {
                await Warn(LocoSwap.Language.Resources.msg_consist_empty);
                return;
            }

            foreach (ScenarioVehicle vehicle in selected)
            {
                ViewModel.Scenario.RemoveVehicle(consist.Idx, vehicle.Idx);
                consist.Vehicles.RemoveAt(vehicle.Idx);
                for (var i = 0; i < consist.Vehicles.Count; i++) consist.Vehicles[i].Idx = i;
            }
            ReloadVehicleList();
        }

        /// <summary>The shared replace step used by all the replace commands.</summary>
        private void SwapVehicle(Consist consist, ScenarioVehicle vehicle, AvailableVehicle newVehicle)
        {
            vehicle.CopyFrom(newVehicle);
            ViewModel.Scenario.ReplaceVehicle(consist.Idx, vehicle.Idx, newVehicle);
            ViewModel.Scenario.ChangeVehicleNumber(consist.Idx, vehicle.Idx, vehicle.Number);
            vehicle.PossibleSubstitutionDisplayName = null;
        }

        private async void ReplaceButton_Click(object sender, RoutedEventArgs e)
        {
            var oldVehicles = VehicleListBox.SelectedItems.Cast<ScenarioVehicle>().ToList();
            if (oldVehicles.Count == 0 || AvailableVehicleListBox.SelectedItem == null)
            {
                await Warn(LocoSwap.Language.Resources.msg_no_vehicle_selected);
                return;
            }
            Consist consist = SelectedConsist;
            AvailableVehicle newVehicle = (AvailableVehicle)AvailableVehicleListBox.SelectedItem;

            foreach (var vehicle in oldVehicles) SwapVehicle(consist, vehicle, newVehicle);
            consist.DetermineCompletenessAfterReplace();
            UpdateSelectedVehicleInfo();
        }

        private async void ReplaceIdenticalButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = VehicleListBox.SelectedItems.Cast<ScenarioVehicle>().ToList();
            if (selected.Count == 0 || AvailableVehicleListBox.SelectedItem == null)
            {
                await Warn(LocoSwap.Language.Resources.msg_no_vehicle_selected);
                return;
            }

            if (AddAsRule.IsChecked == true) await AddToRules(true);

            var identicalXmlPaths = new HashSet<string>(selected.Select(v => v.XmlPath));
            AvailableVehicle newVehicle = (AvailableVehicle)AvailableVehicleListBox.SelectedItem;

            foreach (Consist consist in ViewModel.Consists)
            {
                foreach (ScenarioVehicle vehicle in consist.Vehicles)
                {
                    if (identicalXmlPaths.Contains(vehicle.XmlPath))
                    {
                        SwapVehicle(consist, vehicle, newVehicle);
                        consist.DetermineCompletenessAfterReplace();
                    }
                }
            }
            UpdateSelectedVehicleInfo();
        }

        private async void ChangeNumberButton_Click(object sender, RoutedEventArgs e)
        {
            var list = new List<string> { LocoSwap.Language.Resources.numbering_list_not_found };
            Consist consist = SelectedConsist;
            ScenarioVehicle vehicle = VehicleListBox.SelectedItem as ScenarioVehicle;
            if (consist == null || vehicle == null) return;

            try
            {
                var actualVehicle = await Task.Run(() => new AvailableVehicle(Path.ChangeExtension(vehicle.XmlPath, "bin")));
                list = actualVehicle.NumberingList;
            }
            catch (Exception)
            {
            }

            var window = new VehicleNumberSelectionWindow(list, true, vehicle.Number ?? "");
            string number = await window.ShowAsync(this);
            if (number != null)
            {
                vehicle.Number = number;
                ViewModel.Scenario.ChangeVehicleNumber(consist.Idx, vehicle.Idx, number);
                UpdateSelectedVehicleInfo();
            }
        }

        private async void AvailableVehicleNumberListButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(AvailableVehicleListBox.SelectedItem is AvailableVehicle vehicle)) return;
            await new VehicleNumberSelectionWindow(vehicle.NumberingList ?? new List<string>(), false).ShowAsync(this);
        }

        private async void FlipButton_Click(object sender, RoutedEventArgs e)
        {
            Consist consist = SelectedConsist;
            foreach (ScenarioVehicle vehicle in VehicleListBox.SelectedItems.Cast<ScenarioVehicle>().ToList())
            {
                vehicle.Flipped = !vehicle.Flipped;
                Log.Debug("consist {0}, vehicle {1}, flipped = {2}", consist.Idx, vehicle.Idx, vehicle.Flipped);
                ViewModel.Scenario.ChangeVehicleFlipped(consist.Idx, vehicle.Idx, vehicle.Flipped);
            }
            UpdateSelectedVehicleInfo();
            await Info(LocoSwap.Language.Resources.msg_flip_completed, LocoSwap.Language.Resources.msg_message);
        }

        private async void AllVehiclesListButton_Click(object sender, RoutedEventArgs e)
        {
            var dict = new Dictionary<string, ScenarioVehicle>();
            foreach (Consist consist in ViewModel.Consists)
            {
                foreach (ScenarioVehicle vehicle in consist.Vehicles)
                {
                    string identifier = vehicle.IsReskin ? vehicle.ReskinXmlPath : vehicle.XmlPath;
                    if (!dict.ContainsKey(identifier)) dict.Add(identifier, vehicle);
                }
            }
            await new AllVehiclesWindow(dict.Values.ToList()).ShowDialog(this);
        }

        // ------------------------------------------------------------- saving

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.LoadingInformation = LocoSwap.Language.Resources.saving_scenario;
            ViewModel.LoadingProgress = 20;
            string error = null;
            await Task.Run(() =>
            {
                try { ViewModel.Scenario.Save(); }
                catch (Exception ex) { error = ex.Message; Log.Error("Save failed: {0}", ex); }
            });
            ViewModel.LoadingProgress = 100;

            if (error == null)
            {
                await Info(LocoSwap.Language.Resources.msg_scenario_saved, LocoSwap.Language.Resources.msg_message);
            }
            else
            {
                await Info(LocoSwap.Language.Resources.msg_write_access_denied + "\n\n" + error, LocoSwap.Language.Resources.msg_error);
            }
        }

        // -------------------------------------------------------------- rules

        private async Task AddToRules(bool overwriteWithoutPrompting)
        {
            var targets = new List<ScenarioVehicle>();
            foreach (ScenarioVehicle vehicle in VehicleListBox.SelectedItems.Cast<ScenarioVehicle>().ToList())
            {
                if (targets.Contains(vehicle)) continue;
                if (!overwriteWithoutPrompting && Settings.Default.Preset.Contains(vehicle.XmlPath))
                {
                    var result = await MessageBox.Show(this,
                        string.Format(LocoSwap.Language.Resources.msg_vehicle_already_in_rules, vehicle.Name),
                        LocoSwap.Language.Resources.msg_message, MsgButtons.YesNoCancel);
                    if (result == MsgResult.Yes) targets.Add(vehicle);
                    else if (result != MsgResult.No) return;
                }
                else
                {
                    targets.Add(vehicle);
                }
            }

            AvailableVehicle newVehicle = (AvailableVehicle)AvailableVehicleListBox.SelectedItem;
            foreach (ScenarioVehicle vehicle in targets)
            {
                var existing = Settings.Default.Preset.Find(vehicle.XmlPath);
                if (existing != null) Settings.Default.Preset.List.Remove(existing);
                Settings.Default.Preset.List.Add(new SwapPresetItem
                {
                    TargetName = vehicle.Name,
                    TargetXmlPath = vehicle.XmlPath,
                    NewName = newVehicle.DisplayName,
                    NewXmlPath = newVehicle.XmlPath,
                    NewLength = newVehicle.Length
                });
            }
            Settings.Default.Save();
        }

        private async void AddToRulesButton_Click(object sender, RoutedEventArgs e)
        {
            if (VehicleListBox.SelectedItems.Count == 0 || AvailableVehicleListBox.SelectedItem == null)
            {
                await Warn(LocoSwap.Language.Resources.msg_no_vehicle_selected);
                return;
            }
            await AddToRules(false);
            BringUpPresetWindow();
        }

        private void PresetButton_Click(object sender, RoutedEventArgs e) => BringUpPresetWindow();

        private void BringUpPresetWindow()
        {
            if (PresetWindow != null)
            {
                PresetWindow.Activate();
                return;
            }
            PresetWindow = new SwapPresetWindow();
            PresetWindow.ApplyClicked += PresetWindow_ApplyClicked;
            PresetWindow.Closed += (s, e) => PresetWindow = null;
            PresetWindow.Show();
        }

        private async Task<Dictionary<string, AvailableVehicle>> LoadRuleTargets(IEnumerable<SwapPresetItem> rules)
        {
            var loaded = new Dictionary<string, AvailableVehicle>();
            foreach (var item in rules)
            {
                if (loaded.ContainsKey(item.NewXmlPath)) continue;
                var binPath = Path.ChangeExtension(item.NewXmlPath, "bin");
                try
                {
                    loaded[item.NewXmlPath] = await Task.Run(() => new AvailableVehicle(binPath));
                }
                catch (Exception)
                {
                    await MessageBox.Show(this,
                        string.Format(LocoSwap.Language.Resources.msg_cannot_load_vehicle, item.NewName),
                        LocoSwap.Language.Resources.msg_error);
                    return null;
                }
            }
            return loaded;
        }

        private async void PresetWindow_ApplyClicked(object sender, EventArgs e)
        {
            List<SwapPresetItem> rules = PresetWindow.SelectedItems;
            var available = await LoadRuleTargets(rules);
            if (available == null) return;

            foreach (Consist consist in ViewModel.Consists)
            {
                foreach (ScenarioVehicle vehicle in consist.Vehicles)
                {
                    var rule = rules.FirstOrDefault(item => item.TargetXmlPath.Equals(vehicle.XmlPath, StringComparison.OrdinalIgnoreCase));
                    if (rule == null) continue;
                    SwapVehicle(consist, vehicle, available[rule.NewXmlPath]);
                    consist.DetermineCompletenessAfterReplace();
                }
            }
            UpdateSelectedVehicleInfo();
            await Info(LocoSwap.Language.Resources.msg_swap_completed, LocoSwap.Language.Resources.msg_message);
        }

        private void ApplyAllRulesOnlyOnMissingStock_Click(object sender, RoutedEventArgs e) => ApplyAllRules(true);
        private void ApplyAllRulesOnAllStock_Click(object sender, RoutedEventArgs e) => ApplyAllRules(false);

        private async void ApplyAllRules(bool onlyOnMissingStock)
        {
            var available = new Dictionary<string, AvailableVehicle>();

            foreach (Consist consist in ViewModel.Consists)
            {
                if (onlyOnMissingStock && (consist.IsComplete == ConsistVehicleExistance.Found || consist.IsComplete == ConsistVehicleExistance.FullyReplaced))
                    continue;

                foreach (ScenarioVehicle vehicle in consist.Vehicles)
                {
                    if (!(vehicle.Exists != VehicleExistance.Missing && !onlyOnMissingStock || vehicle.Exists == VehicleExistance.MissingWithRule))
                        continue;

                    var item = Settings.Default.Preset.Find(vehicle.XmlPath);
                    if (item == null) continue;

                    if (!available.ContainsKey(item.NewXmlPath))
                    {
                        var loaded = await LoadRuleTargets(new[] { item });
                        if (loaded == null) return;
                        available[item.NewXmlPath] = loaded[item.NewXmlPath];
                    }

                    SwapVehicle(consist, vehicle, available[item.NewXmlPath]);
                    consist.DetermineCompletenessAfterReplace();
                }
            }
            UpdateSelectedVehicleInfo();
        }

        // ----------------------------------------------------------- misc

        private void Window_Closed(object sender, EventArgs e)
        {
            scanTimer?.Stop();
            ScanCancellationTokenSource?.Cancel();
            PresetWindow?.Close();
        }

        private void OpenManual_Click(object sender, RoutedEventArgs e) => Utilities.OpenManual();
    }
}
