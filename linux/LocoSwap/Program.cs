using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using LocoSwap.Properties;
using System;
using System.IO;

namespace LocoSwap
{
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Log.Init(Path.Combine(Settings.ConfigDir, "debug.log"));
            Log.Debug("LocoSwap {0} starting up..", AppInfo.Version);

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                Log.Error("Uncaught exception: {0}", e.ExceptionObject);

            var lifetime = new ClassicDesktopStyleApplicationLifetime
            {
                Args = args,
                ShutdownMode = ShutdownMode.OnLastWindowClose
            };
            BuildAvaloniaApp().SetupWithLifetime(lifetime);
            lifetime.Start(args);
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>().UsePlatformDetect();
    }
}
