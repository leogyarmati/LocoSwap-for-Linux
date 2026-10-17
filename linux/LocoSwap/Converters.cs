using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;
using System.Collections.Generic;

namespace LocoSwap
{
    /// <summary>Value converters and image helpers shared by the XAML views.</summary>
    public static class Converters
    {
        private static readonly Dictionary<string, Bitmap> Cache = new Dictionary<string, Bitmap>();

        /// <summary>Loads (and caches) one of the bundled pictures from Assets/.</summary>
        public static Bitmap Asset(string fileName)
        {
            lock (Cache)
            {
                if (!Cache.TryGetValue("asset:" + fileName, out var bmp))
                {
                    using (var stream = AssetLoader.Open(new Uri("avares://LocoSwap/Assets/" + fileName)))
                    {
                        bmp = new Bitmap(stream);
                    }
                    Cache["asset:" + fileName] = bmp;
                }
                return bmp;
            }
        }

        /// <summary>Preview picture for a vehicle (from the game files / .ap), or the bundled placeholder.</summary>
        public static IImage VehicleImage(Vehicle vehicle)
        {
            if (vehicle == null) return Asset("PreviewNotAvailable.png");
            string path = VehicleAvailibility.GetVehicleImage(vehicle);
            if (path == VehicleAvailibility.NoPreview) return Asset("PreviewNotAvailable.png");
            lock (Cache)
            {
                if (Cache.TryGetValue(path, out var cached)) return cached;
                try
                {
                    var bmp = new Bitmap(path);
                    Cache[path] = bmp;
                    return bmp;
                }
                catch (Exception e)
                {
                    Log.Debug("Could not load image {0}: {1}", path, e.Message);
                    return Asset("PreviewNotAvailable.png");
                }
            }
        }

        public static readonly IValueConverter VehicleStatus = new FuncValueConverter<VehicleExistance, IImage>(status =>
        {
            switch (status)
            {
                case VehicleExistance.Found: return Asset("BulletGreen.png");
                case VehicleExistance.Replaced: return Asset("Replaced.png");
                case VehicleExistance.MissingWithRule: return Asset("BulletYellow.png");
                default: return Asset("BulletRed.png");
            }
        });

        public static readonly IValueConverter ConsistStatus = new FuncValueConverter<ConsistVehicleExistance, IImage>(status =>
        {
            switch (status)
            {
                case ConsistVehicleExistance.Found: return Asset("BulletGreen.png");
                case ConsistVehicleExistance.FullyReplaced: return Asset("ReplacedGreen.png");
                case ConsistVehicleExistance.PartiallyReplaced: return Asset("ReplacedRed.png");
                case ConsistVehicleExistance.MissingWithRulesPartiallyReplaced: return Asset("ReplacedYellow.png");
                case ConsistVehicleExistance.MissingWithRules: return Asset("BulletYellow.png");
                default: return Asset("BulletRed.png");
            }
        });

        public static readonly IValueConverter ScenarioStatus = new FuncValueConverter<ScenarioVehicleExistance, IImage>(status =>
        {
            switch (status)
            {
                case ScenarioVehicleExistance.NotChecked: return null;
                case ScenarioVehicleExistance.AllFound: return Asset("BulletGreen.png");
                case ScenarioVehicleExistance.MissingWithRules: return Asset("BulletYellow.png");
                default: return Asset("BulletRed.png");
            }
        });

        public static readonly IValueConverter Star = new FuncValueConverter<bool, IImage>(
            favorite => Asset(favorite ? "StarActive.png" : "Star.png"));

        public static readonly IValueConverter ArchivedBrush = new FuncValueConverter<bool, IBrush>(
            archived => archived ? Brushes.LightGray : Brushes.Transparent);

        /// <summary>Scenarios that live inside an .ap archive are shown in italics.</summary>
        public static readonly IValueConverter ApFontStyle = new FuncValueConverter<string, FontStyle>(
            apFile => string.IsNullOrEmpty(apFile) ? FontStyle.Normal : FontStyle.Italic);
    }
}
