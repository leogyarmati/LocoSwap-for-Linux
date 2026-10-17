using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using LocoSwap.Properties;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace LocoSwap
{
    public enum MsgButtons { Ok, YesNoCancel }
    public enum MsgResult { Cancel = 0, Ok, Yes, No }

    /// <summary>Avalonia has no built-in message box, so this is a minimal one.</summary>
    public static class MessageBox
    {
        public static async Task<MsgResult> Show(Window owner, string message, string title, MsgButtons buttons = MsgButtons.Ok)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 460,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            var text = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20, 20, 20, 14)
            };
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Margin = new Thickness(20, 0, 20, 16)
            };

            void AddButton(string label, MsgResult result, bool isDefault)
            {
                var button = new Button { Content = label, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center, IsDefault = isDefault };
                button.Click += (s, e) => dialog.Close(result);
                row.Children.Add(button);
            }

            if (buttons == MsgButtons.YesNoCancel)
            {
                AddButton(LocoSwap.Language.Resources.yes, MsgResult.Yes, true);
                AddButton(LocoSwap.Language.Resources.no, MsgResult.No, false);
                AddButton(LocoSwap.Language.Resources.cancel, MsgResult.Cancel, false);
            }
            else
            {
                AddButton(LocoSwap.Language.Resources.ok, MsgResult.Ok, true);
            }

            var panel = new StackPanel();
            panel.Children.Add(text);
            panel.Children.Add(row);
            dialog.Content = panel;

            return await dialog.ShowDialog<MsgResult>(owner);
        }
    }

    public static class Dialogs
    {
        /// <summary>
        /// Lets the user pick the Train Simulator folder (the one containing RailWorks.exe).
        /// Offers an auto-detected Steam install first. Returns false if the user gave up.
        /// </summary>
        public static async Task<bool> ChooseTsPath(Window owner)
        {
            string current = Settings.Default.TsPath;
            var candidates = TsLocator.FindCandidates();

            if (candidates.Count > 0 && !Utilities.IsValidTsPath(current))
            {
                var answer = await MessageBox.Show(owner,
                    string.Format(LocoSwap.Language.Resources.msg_ts_detected, candidates[0]),
                    LocoSwap.Language.Resources.msg_message, MsgButtons.YesNoCancel);
                if (answer == MsgResult.Yes)
                {
                    Settings.Default.TsPath = candidates[0];
                    Settings.Default.Save();
                    return true;
                }
                if (answer == MsgResult.Cancel) return false;
            }

            while (true)
            {
                var options = new FolderPickerOpenOptions
                {
                    Title = LocoSwap.Language.Resources.select_ts_path,
                    AllowMultiple = false
                };
                try
                {
                    string start = Utilities.IsValidTsPath(current) ? current : (candidates.FirstOrDefault() ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                    options.SuggestedStartLocation = await owner.StorageProvider.TryGetFolderFromPathAsync(start);
                }
                catch (Exception) { }

                var folders = await owner.StorageProvider.OpenFolderPickerAsync(options);
                if (folders.Count == 0) return false;

                string path = folders[0].TryGetLocalPath();
                if (path == null || !Utilities.IsValidTsPath(path))
                {
                    await MessageBox.Show(owner, LocoSwap.Language.Resources.msg_ts_path_invalid, LocoSwap.Language.Resources.msg_error);
                    continue;
                }
                Settings.Default.TsPath = path;
                Settings.Default.Save();
                return true;
            }
        }
    }
}
