using LocoSwap.Properties;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace LocoSwap
{
    public struct VehicleAvailibilityResult
    {
        public bool Available;
        public bool InApFile;
        public string ApPath;
        public string PathWithinAp;
    }
    public static class VehicleAvailibility
    {
        private static ConcurrentDictionary<string, VehicleAvailibilityResult> _vehicleTable;
        private static ConcurrentDictionary<string, string> _vehicleImageTable;
        private static ConcurrentDictionary<string, string> _vehicleDisplayNameTable;
        private static ConcurrentDictionary<string, List<string>> _numberingListCache;

        static VehicleAvailibility()
        {
            _vehicleTable = new ConcurrentDictionary<string, VehicleAvailibilityResult>();
            _vehicleImageTable = new ConcurrentDictionary<string, string>();
            _vehicleDisplayNameTable = new ConcurrentDictionary<string, string>();
            _numberingListCache = new ConcurrentDictionary<string, List<string>>();
        }

        /// <summary>Marker returned by GetVehicleImage when no preview picture exists (UI shows its bundled placeholder).</summary>
        public const string NoPreview = "avares-resource:PreviewNotAvailable.png";

        public static string GetVehicleImage(Vehicle vehicle)
        {
            var xmlPath = vehicle.XmlPath;
            if (vehicle.IsReskin) xmlPath = vehicle.ReskinXmlPath;
            if (_vehicleImageTable.ContainsKey(xmlPath))
            {
                return _vehicleImageTable[xmlPath];
            }

            var availibility = IsVehicleAvailable(vehicle);
            if (!availibility.Available)
            {
                _vehicleImageTable[xmlPath] = NoPreview;
                return _vehicleImageTable[xmlPath];
            }

            if (!availibility.InApFile)
            {
                var vehicleDirectory = Path.GetDirectoryName(Fs.Combine(Settings.Default.TsPath, "Assets", xmlPath));
                var imagePath = Fs.ResolveCI(Path.Combine(vehicleDirectory, "LocoInformation", "image.png"));
                if (File.Exists(imagePath)) _vehicleImageTable[xmlPath] = imagePath;
                else _vehicleImageTable[xmlPath] = NoPreview;

                return _vehicleImageTable[xmlPath];
            }

            try
            {
                using (var zipFile = ZipUtil.Open(availibility.ApPath))
                {
                    var components = availibility.PathWithinAp.Split('/');
                    var componentsList = components.ToList();
                    componentsList.RemoveAt(componentsList.Count - 1);
                    componentsList.Add("LocoInformation");
                    componentsList.Add("image.png");
                    var imageEntry = ZipUtil.Find(zipFile, string.Join("/", componentsList), true);
                    if (imageEntry == null)
                    {
                        _vehicleImageTable[xmlPath] = NoPreview;
                        return _vehicleImageTable[xmlPath];
                    }
                    var extractPath = Path.Combine(
                        Utilities.GetTempDir(),
                        "image-" + Utilities.StaticRandom.Instance.Next(10000, 99999).ToString() + ".png");

                    Utilities.RemoveFile(extractPath);
                    imageEntry.ExtractToFile(extractPath, true);

                    _vehicleImageTable[xmlPath] = extractPath;
                    return extractPath;
                }
            }
            catch (Exception e)
            {
                Log.Debug("GetVehicleImage: Could not extract image from .ap file! {0}", e);

                _vehicleImageTable[xmlPath] = NoPreview;
                return _vehicleImageTable[xmlPath];
            }
        }

        public static string GetVehicleDisplayName(Vehicle vehicle)
        {
            if (_vehicleDisplayNameTable.ContainsKey(vehicle.XmlPath)) return _vehicleDisplayNameTable[vehicle.XmlPath];
            Log.Debug("GetVehicleDisplayName: {0} is not in table, looking up..", vehicle.XmlPath);
            var binPath = Path.ChangeExtension(vehicle.XmlPath, "bin");
            try
            {
                AvailableVehicle actualVehicle = new AvailableVehicle(binPath);
                _vehicleDisplayNameTable[vehicle.XmlPath] = actualVehicle.DisplayName;
            }
            catch (Exception)
            {
                _vehicleDisplayNameTable[vehicle.XmlPath] = vehicle.Name;
            }
            return _vehicleDisplayNameTable[vehicle.XmlPath];
        }

        public static List<string> GetNumberingList(string location)
        {
            if (_numberingListCache.ContainsKey(location))
            {
                return _numberingListCache[location];
            }
            var dcsvPath = Fs.ResolveCI(Fs.Combine(Settings.Default.TsPath, "Assets", location) + ".dcsv");
            if (!File.Exists(dcsvPath))
            {
                var components = location.Split('\\');
                if (components.Length < 3) throw new Exception("Numbering list not found");
                var apDirectory = Fs.ResolveCI(Fs.Combine(Settings.Default.TsPath, "Assets", components[0], components[1]));
                if (!Directory.Exists(apDirectory)) throw new Exception("Numbering list not found");
                var apFiles = Fs.GetFiles(apDirectory, "*.ap");
                bool found = false;
                foreach (var ap in apFiles)
                {
                    try
                    {
                        using (var zipFile = ZipUtil.Open(ap))
                        {
                            var dcsvEntry = ZipUtil.Find(zipFile, string.Join("/", components.Skip(2)) + ".dcsv", true);
                            if (dcsvEntry == null) continue;
                            Utilities.RemoveFile(Path.Combine(Utilities.GetTempDir(), Path.GetFileName(dcsvEntry.FullName)));
                            dcsvPath = ZipUtil.ExtractFlat(dcsvEntry, Utilities.GetTempDir());
                            found = true;
                            break;
                        }
                    }
                    catch (Exception)
                    {

                    }
                }
                if (!found) throw new Exception("Numbering list not found");
            }
            List<string> list = new List<string>();
            XDocument dcsv = XmlDocumentLoader.Load(dcsvPath);
            IEnumerable<XElement> cCSVItems = dcsv.Descendants("cCSVItem");
            foreach (XElement cCSVItem in cCSVItems)
            {
                if (cCSVItem.Element("Name") == null) continue;
                list.Add(cCSVItem.Element("Name").Value);
            }
            _numberingListCache[location] = list;
            return list;
        }

        public static VehicleAvailibilityResult IsVehicleAvailable(Vehicle vehicle)
        {
            VehicleAvailibilityResult ret = new VehicleAvailibilityResult
            {
                Available = false,
                InApFile = false,
                ApPath = null,
                PathWithinAp = null
            };
            if (vehicle.IsReskin)
            {
                // We should determine if the reskin itself exists first
                Vehicle reskinAsVehicle = new Vehicle(vehicle.ReskinProvider, vehicle.ReskinProduct, vehicle.ReskinBlueprintId, "Reskin", 0);
                Log.Debug("IsVehicleAvailable: check for reskin {0}", reskinAsVehicle.XmlPath);
                VehicleAvailibilityResult reskinAvailability = IsVehicleAvailable(reskinAsVehicle);
                if (!reskinAvailability.Available)
                {
                    return ret;
                }
            }
            if (_vehicleTable.ContainsKey(vehicle.XmlPath))
            {
                return _vehicleTable[vehicle.XmlPath];
            }
            var binPath = vehicle.FullBinPath;
            if (File.Exists(binPath))
            {
                ret.Available = true;
                _vehicleTable[vehicle.XmlPath] = ret;
                return ret;
            }

            var apDirectory = Fs.ResolveCI(Fs.Combine(Settings.Default.TsPath, "Assets", vehicle.Provider, vehicle.Product));
            if (Directory.Exists(apDirectory))
            {
                var apFiles = Fs.GetFiles(apDirectory, "*.ap");
                var found = false;
                string foundApPath = "";
                var binName = Path.ChangeExtension(vehicle.BlueprintId, "bin").Replace('\\', '/');
                foreach (var apPath in apFiles)
                {
                    var result = false;
                    try
                    {
                        using (var zipFile = ZipUtil.Open(apPath))
                        {
                            result = zipFile.Entries.Any(entry => entry.FullName.Equals(binName, StringComparison.OrdinalIgnoreCase));
                        }
                    }
                    catch (Exception e) when (e is InvalidDataException || e is IOException)
                    {
                        Log.Debug("Error while reading zip file: {0}", apPath);
                    }
                   
                    if (result)
                    {
                        found = true;
                        foundApPath = apPath;
                        break;
                    }
                }
                if (found)
                {
                    ret.Available = true;
                    ret.InApFile = true;
                    ret.ApPath = foundApPath;
                    ret.PathWithinAp = binName;
                    _vehicleTable[vehicle.XmlPath] = ret;
                    return ret;
                }
            }

            _vehicleTable[vehicle.XmlPath] = ret;
            return ret;
        }

        public static void ClearTable()
        {
            _vehicleTable.Clear();
            _vehicleImageTable.Clear();
            _vehicleDisplayNameTable.Clear();
            _numberingListCache.Clear();
        }
    }
}
