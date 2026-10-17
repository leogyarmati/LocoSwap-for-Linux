using Avalonia.Controls;
using Avalonia.Interactivity;
using LocoSwap.Properties;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;

namespace LocoSwap
{
    public partial class SwapPresetWindow : Window
    {
        public event EventHandler ApplyClicked;

        public List<SwapPresetItem> SelectedItems => PresetList.SelectedItems.Cast<SwapPresetItem>().ToList();

        public SwapPresetWindow()
        {
            InitializeComponent();
            // Clicking an entry toggles it, like the check boxes of the original list
            PresetList.SelectionMode = SelectionMode.Multiple | SelectionMode.Toggle;

            PresetsFilterTextbox.PropertyChanged += (s, e) => { if (e.Property == TextBox.TextProperty) RefreshView(); };
            Settings.Default.Preset.List.CollectionChanged += OnPresetChanged;
            Closed += (s, e) => Settings.Default.Preset.List.CollectionChanged -= OnPresetChanged;
            RefreshView();
        }

        private void OnPresetChanged(object sender, NotifyCollectionChangedEventArgs e) => RefreshView();

        private void RefreshView()
        {
            string filter = PresetsFilterTextbox.Text;
            var previous = PresetList.SelectedItems.Cast<SwapPresetItem>().ToList();
            var view = Settings.Default.Preset.List.Where(item => Matches(item, filter)).ToList();
            PresetList.ItemsSource = view;
            foreach (var item in previous.Where(view.Contains)) PresetList.SelectedItems.Add(item);
        }

        private static bool Matches(SwapPresetItem item, string filter)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            string[] props = { item.NewName, item.NewXmlPath, item.TargetName, item.TargetXmlPath };
            return filter.Split(' ').All(token => props.Any(p => p != null && p.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in SelectedItems) Settings.Default.Preset.List.Remove(item);
            Settings.Default.Save();
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e) => ApplyClicked?.Invoke(this, EventArgs.Empty);

        private void EmptyPresetFilter_Click(object sender, RoutedEventArgs e) => PresetsFilterTextbox.Text = "";
    }
}
