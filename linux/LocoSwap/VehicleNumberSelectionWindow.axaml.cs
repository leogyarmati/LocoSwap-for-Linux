using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LocoSwap
{
    public partial class VehicleNumberSelectionWindow : Window
    {
        private readonly bool isSelection;

        public VehicleNumberSelectionWindow()
        {
            InitializeComponent();
        }

        /// <param name="isSelection">true: pick or type a number; false: just list the numbers.</param>
        public VehicleNumberSelectionWindow(IEnumerable<string> numbers, bool isSelection, string previousNumber = "") : this()
        {
            this.isSelection = isSelection;
            NumberListBox.ItemsSource = new List<string>(numbers);
            ManualPanel.IsVisible = isSelection;
            CancelButton.IsVisible = isSelection;
            NumberBox.Text = previousNumber;
        }

        /// <summary>Shows the dialog; returns the chosen number, or null if cancelled.</summary>
        public async Task<string> ShowAsync(Window owner)
        {
            return await ShowDialog<string>(owner);
        }

        private void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isSelection && NumberListBox.SelectedItem is string number) NumberBox.Text = number;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e) => Close(isSelection ? NumberBox.Text : null);
        private void CancelButton_Click(object sender, RoutedEventArgs e) => Close(null);
    }
}
