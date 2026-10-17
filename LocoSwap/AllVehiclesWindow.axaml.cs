using Avalonia.Controls;
using System.Collections.Generic;

namespace LocoSwap
{
    public partial class AllVehiclesWindow : Window
    {
        public AllVehiclesWindow()
        {
            InitializeComponent();
        }

        public AllVehiclesWindow(List<ScenarioVehicle> vehicles) : this()
        {
            VehicleListBox.ItemsSource = vehicles;
        }

        private void VehicleListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var v = VehicleListBox.SelectedItem as ScenarioVehicle;
            NameBox.Text = v?.DisplayName ?? "";
            NumberBox.Text = v?.Number ?? "";
            PathBox.Text = v == null ? "" : "\\Assets\\" + v.DisplayXmlPath;
            FlippedBox.IsChecked = v?.Flipped ?? false;
            PreviewImage.Source = Converters.VehicleImage(v);
        }
    }
}
