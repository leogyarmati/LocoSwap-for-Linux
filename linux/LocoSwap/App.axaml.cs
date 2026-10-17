using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LocoSwap.Properties;
using System;
using System.IO;

namespace LocoSwap
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            Utilities.ApplyLanguage();

            try
            {
                foreach (string file in Directory.GetFiles(Utilities.GetTempDir()))
                {
                    try { File.Delete(file); } catch (Exception) { }
                }
            }
            catch (Exception e)
            {
                Log.Debug("Could not clean temp directory: {0}", e.Message);
            }

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
