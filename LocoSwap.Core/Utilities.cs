using LocoSwap.Properties;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Xml.Linq;

namespace LocoSwap
{
    public static class Utilities
    {
        private static XNamespace Namespace = "http://www.kuju.com/TnT/2003/Delta";

        public static string GetTempDir()
        {
            string dir = Path.Combine(Settings.CacheDir, "temp");
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static string GetSerzPath()
        {
            return Fs.ResolveCI(Fs.Combine(Settings.Default.TsPath, "serz.exe"));
        }

        public static void RemoveFile(string path)
        {
            if (File.Exists(path))
            {
                try { File.SetAttributes(path, FileAttributes.Normal); } catch (Exception) { }
                File.Delete(path);
            }
        }

        /// <summary>True if the folder looks like a Train Simulator install (contains RailWorks.exe).</summary>
        public static bool IsValidTsPath(string path)
        {
            return !string.IsNullOrEmpty(path) && Directory.Exists(path) && Fs.FileExistsCI(Path.Combine(path, "RailWorks.exe"));
        }

        public static void CopyUserLocalisedString(XElement dest, XElement orig)
        {
            if (dest == null || orig == null) return;
            var names = new string[] { "English", "French", "Italian", "German", "Spanish", "Dutch", "Polish", "Russian", "Key" };
            foreach (var name in names)
            {
                XElement destName = dest.Element(name);
                XElement origName = orig.Element(name);
                if (destName != null && origName != null)
                {
                    destName.Value = origName.Value;
                }
            }
            dest.Element("Other").Elements().Remove();
        }

        public static string DetermineDisplayName(XElement localisedString)
        {
            if (localisedString == null) return "";

            var lang = Settings.Default.Language;
            if (string.IsNullOrEmpty(lang)) lang = Thread.CurrentThread.CurrentCulture.TwoLetterISOLanguageName;
            var langConversionTable = new Dictionary<string, string>()
            {
                { "en", "English" },
                { "fr", "French" },
                { "it", "Italian" },
                { "de", "German" },
                { "es", "Spanish" },
                { "nl", "Dutch" },
                { "pl", "Polish" },
                { "ru", "Russian" }
            };
            var convertedLang = "en";
            if (langConversionTable.ContainsKey(lang))
            {
                convertedLang = langConversionTable[lang];
            }
            XElement preferredElement = localisedString.Element(convertedLang);
            if (preferredElement != null)
            {
                if (preferredElement.Value != "") return preferredElement.Value;
            }

            var result = "";
            foreach (XElement localisedName in localisedString.Elements())
            {
                if (localisedName.Name == "Other" || localisedName.Name == "Key") continue;
                if (localisedName.Value != "")
                {
                    result = localisedName.Value;
                    break;
                }
            }
            return result;
        }

        public static Tuple<ulong, ulong> GetUUIDLongs(Guid guid)
        {
            var result = new Tuple<ulong, ulong>(
                BitConverter.ToUInt64(guid.ToByteArray(), 0),
                BitConverter.ToUInt64(guid.ToByteArray(), 8));

            return result;
        }

        public static XElement GenerateCGUID()
        {
            var guid = Guid.NewGuid();
            var ulongs = GetUUIDLongs(guid);
            var cGUID = new XElement("cGUID");
            var UUID = new XElement("UUID");

            var e1 = new XElement("e");
            e1.SetAttributeValue(Namespace + "type", "sUInt64");
            e1.SetValue(ulongs.Item1);

            var e2 = new XElement("e");
            e2.SetAttributeValue(Namespace + "type", "sUInt64");
            e2.SetValue(ulongs.Item2);

            UUID.Add(e1, e2);

            var devString = new XElement("DevString");
            devString.SetAttributeValue(Namespace + "type", "cDeltaString");
            devString.SetValue(guid.ToString());

            cGUID.Add(UUID, devString);
            return cGUID;
        }

        public static XElement GenerateEntityContainerItem()
        {
            XElement newNode = new XElement("e");
            newNode.SetAttributeValue(Namespace + "numElements", "16");
            newNode.SetAttributeValue(Namespace + "elementType", "sFloat32");
            newNode.SetAttributeValue(Namespace + "precision", "string");
            newNode.SetValue("1.0000000 0.0000000 0.0000000 0.0000000 0.0000000 1.0000000 0.0000000 0.0000000 0.0000000 0.0000000 1.0000000 0.0000000 0.0000000 0.0000000 0.0000000 1.0000000");
            return newNode;
        }

        public static XElement GenerateCargoComponentItem(string val, string altEncoding)
        {
            XElement newNode = new XElement("e");
            newNode.SetAttributeValue(Namespace + "type", "sFloat32");
            newNode.SetAttributeValue(Namespace + "alt_encoding", altEncoding);
            newNode.SetAttributeValue(Namespace + "precision", "string");
            newNode.SetValue(val);
            return newNode;
        }

        public static class StaticRandom
        {
            private static int seed;

            private static ThreadLocal<Random> threadLocal = new ThreadLocal<Random>
                (() => new Random(Interlocked.Increment(ref seed)));

            static StaticRandom()
            {
                seed = Environment.TickCount;
            }

            public static Random Instance { get { return threadLocal.Value; } }
        }

        /// <summary>Opens a file or folder with the desktop's default application.</summary>
        public static void OpenPath(string path)
        {
            try
            {
                var psi = new ProcessStartInfo { UseShellExecute = false };
                if (OperatingSystem.IsWindows()) { psi.FileName = path; psi.UseShellExecute = true; }
                else if (OperatingSystem.IsMacOS()) { psi.FileName = "open"; psi.ArgumentList.Add(path); }
                else { psi.FileName = "xdg-open"; psi.ArgumentList.Add(path); }
                Process.Start(psi);
            }
            catch (Exception e)
            {
                Log.Warning("Could not open {0}: {1}", path, e.Message);
            }
        }

        public static void OpenManual()
        {
            foreach (var name in new[] { "LocoSwap_manual.pdf", "LocoSwap_manual.docx" })
            {
                string candidate = Path.Combine(AppContext.BaseDirectory, name);
                if (File.Exists(candidate)) { OpenPath(candidate); return; }
            }
        }

        /// <summary>Applies the language chosen in the settings (or the system language) to the resource lookup.</summary>
        public static void ApplyLanguage()
        {
            var lang = Settings.Default.Language;
            if (string.IsNullOrEmpty(lang)) lang = Thread.CurrentThread.CurrentCulture.TwoLetterISOLanguageName;
            Log.Debug("Set language to {0}", lang);
            switch (lang)
            {
                case "de": LocoSwap.Language.Resources.Culture = new System.Globalization.CultureInfo("de-DE"); break;
                case "fr": LocoSwap.Language.Resources.Culture = new System.Globalization.CultureInfo("fr-FR"); break;
                case "it": LocoSwap.Language.Resources.Culture = new System.Globalization.CultureInfo("it-IT"); break;
                case "nl": LocoSwap.Language.Resources.Culture = new System.Globalization.CultureInfo("nl-NL"); break;
                case "ru": LocoSwap.Language.Resources.Culture = new System.Globalization.CultureInfo("ru-RU"); break;
                case "en":
                default: LocoSwap.Language.Resources.Culture = new System.Globalization.CultureInfo("en-US"); break;
            }
        }
    }
}
