using System;
using System.IO;
using System.Linq;

namespace LocoSwap
{
    /// <summary>
    /// Filesystem helpers that bridge Train Simulator's Windows-style data
    /// (backslash separators, case-insensitive names) and a Linux filesystem.
    /// </summary>
    public static class Fs
    {
        /// <summary>Converts any mix of '\' and '/' to the native separator.</summary>
        public static string ToNative(string path)
        {
            if (path == null) return null;
            char sep = Path.DirectorySeparatorChar;
            return path.Replace('\\', sep).Replace('/', sep);
        }

        /// <summary>Path.Combine that accepts Windows-style relative parts.</summary>
        public static string Combine(params string[] parts)
        {
            return Path.Combine(parts.Select(ToNative).ToArray());
        }

        /// <summary>Converts a native absolute path to what a Windows program under Wine/Proton expects (Z:\...).</summary>
        public static string ToWinePath(string nativePath)
        {
            if (OperatingSystem.IsWindows()) return nativePath;
            return "Z:" + nativePath.Replace('/', '\\');
        }

        /// <summary>
        /// Returns the real on-disk spelling of a path, matching each segment case-insensitively.
        /// Windows data (blueprint ids etc.) often differs in case from the files on a case-sensitive FS.
        /// If nothing matches, the input is returned unchanged.
        /// </summary>
        public static string ResolveCI(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            if (File.Exists(path) || Directory.Exists(path)) return path;
            if (OperatingSystem.IsWindows() || !Path.IsPathRooted(path)) return path;

            string current = Path.DirectorySeparatorChar.ToString();
            var segments = path.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            foreach (var segment in segments)
            {
                string candidate = Path.Combine(current, segment);
                if (File.Exists(candidate) || Directory.Exists(candidate))
                {
                    current = candidate;
                    continue;
                }
                if (!Directory.Exists(current)) return path;
                string match = null;
                try
                {
                    foreach (var entry in Directory.EnumerateFileSystemEntries(current))
                    {
                        if (string.Equals(Path.GetFileName(entry), segment, StringComparison.OrdinalIgnoreCase))
                        {
                            match = entry;
                            break;
                        }
                    }
                }
                catch (Exception)
                {
                    return path;
                }
                if (match == null) return path;
                current = match;
            }
            return current;
        }

        public static bool FileExistsCI(string path) => File.Exists(ResolveCI(path));
        public static bool DirectoryExistsCI(string path) => Directory.Exists(ResolveCI(path));

        private static EnumerationOptions Options(SearchOption option) => new EnumerationOptions
        {
            MatchCasing = MatchCasing.CaseInsensitive,
            MatchType = MatchType.Simple,
            RecurseSubdirectories = option == SearchOption.AllDirectories,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System
        };

        public static string[] GetFiles(string directory, string pattern, SearchOption option = SearchOption.TopDirectoryOnly)
        {
            return Directory.GetFiles(directory, pattern, Options(option));
        }

        public static string[] GetDirectories(string directory)
        {
            return Directory.GetDirectories(directory, "*", Options(SearchOption.TopDirectoryOnly));
        }

        /// <summary>Relative path of 'full' below 'baseDir', written with backslashes like TS blueprint ids.</summary>
        public static string RelativeToWin(string baseDir, string full)
        {
            return Path.GetRelativePath(baseDir, full).Replace('/', '\\');
        }
    }
}
