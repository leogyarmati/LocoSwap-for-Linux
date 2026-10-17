using Avalonia.Controls;
using Avalonia.Interactivity;
using LocoSwap.Properties;
using System.Collections.Generic;

namespace LocoSwap
{
    public partial class SettingsWindow : Window
    {
        private class LanguageListItem
        {
            public string Name;
            public string Value;
            public override string ToString() => Name;
        }

        private readonly List<LanguageListItem> languages = new List<LanguageListItem>
        {
            new LanguageListItem { Name = LocoSwap.Language.Resources.use_system_language, Value = "" },
            new LanguageListItem { Name = "Deutsch", Value = "de" },
            new LanguageListItem { Name = "English", Value = "en" },
            new LanguageListItem { Name = "Français", Value = "fr" },
            new LanguageListItem { Name = "Italiano", Value = "it" },
            new LanguageListItem { Name = "Nederlands", Value = "nl" },
            new LanguageListItem { Name = "Русский", Value = "ru" },
        };

        public SettingsWindow()
        {
            InitializeComponent();
            var s = Settings.Default;
            TsPathBox.Text = s.TsPath;
            LanguageComboBox.ItemsSource = languages;
            int idx = languages.FindIndex(l => l.Value == s.Language);
            LanguageComboBox.SelectedIndex = idx < 0 ? 0 : idx;
            SuffixBox.Text = s.ScenarioNameSuffix;
            CheckConsistsBox.IsChecked = s.CheckScenarioConsists;
            NoArchiveWorkshopBox.IsChecked = s.DoNotAutoArchiveWorkshopRoutes;
            RunnerBox.Text = s.RunnerCommand;
            RunnerEnvBox.Text = s.RunnerEnv;
            Closed += (sender, e) => Persist();
        }

        private async void TsPathBrowseButton_Click(object sender, RoutedEventArgs e)
        {
            if (await Dialogs.ChooseTsPath(this)) TsPathBox.Text = Settings.Default.TsPath;
        }

        private void Persist()
        {
            var s = Settings.Default;
            var lang = LanguageComboBox.SelectedItem as LanguageListItem;
            s.Language = lang?.Value ?? "";
            s.ScenarioNameSuffix = SuffixBox.Text ?? "";
            s.CheckScenarioConsists = CheckConsistsBox.IsChecked == true;
            s.DoNotAutoArchiveWorkshopRoutes = NoArchiveWorkshopBox.IsChecked == true;
            s.RunnerCommand = (RunnerBox.Text ?? "").Trim();
            s.RunnerEnv = RunnerEnvBox.Text ?? "";
            s.Save();
            Utilities.ApplyLanguage();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
