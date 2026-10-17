using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace LocoSwap
{
    /// <summary>Looks for Steam installs of Train Simulator Classic ("RailWorks").</summary>
    public static class TsLocator
    {
        public static List<string> FindCandidates()
        {
            var result = new List<string>();
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var steamRoots = new List<string>
            {
                Path.Combine(home, ".local/share/Steam"),
                Path.Combine(home, ".steam/steam"),
                Path.Combine(home, ".steam/root"),
                Path.Combine(home, ".var/app/com.valvesoftware.Steam/.local/share/Steam"),
                Path.Combine(home, "snap/steam/common/.local/share/Steam"),
            };

            var libraries = new List<string>();
            foreach (var root in steamRoots)
            {
                string steamapps = Path.Combine(root, "steamapps");
                if (!Directory.Exists(steamapps)) continue;
                libraries.Add(steamapps);
                string vdf = Path.Combine(steamapps, "libraryfolders.vdf");
                if (!File.Exists(vdf)) continue;
                try
                {
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    {
                        libraries.Add(Path.Combine(m.Groups[1].Value.Replace("\\\\", "\\"), "steamapps"));
                    }
                }
                catch (Exception) { }
            }

            foreach (var lib in libraries)
            {
                string candidate = Fs.ResolveCI(Path.Combine(lib, "common", "RailWorks"));
                if (Utilities.IsValidTsPath(candidate) && !result.Contains(candidate)) result.Add(candidate);
            }
            return result;
        }
    }
}
