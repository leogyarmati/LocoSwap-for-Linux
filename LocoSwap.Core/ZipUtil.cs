using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace LocoSwap
{
    /// <summary>Thin helpers over System.IO.Compression (replaces DotNetZip).</summary>
    public static class ZipUtil
    {
        public static ZipArchive Open(string path) => ZipFile.OpenRead(path);

        public static ZipArchiveEntry Find(ZipArchive zip, string entryName, bool ignoreCase = false)
        {
            var cmp = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, entryName, cmp));
        }

        /// <summary>Extracts an entry into 'directory' ignoring any folders inside the archive; returns the file path.</summary>
        public static string ExtractFlat(ZipArchiveEntry entry, string directory)
        {
            Directory.CreateDirectory(directory);
            string dest = Path.Combine(directory, Path.GetFileName(entry.FullName));
            entry.ExtractToFile(dest, true);
            return dest;
        }

        /// <summary>Extracts every entry below 'prefix' (e.g. "Scenarios/ID/") keeping its relative path under destRoot.</summary>
        public static void ExtractPrefix(string archivePath, string prefix, string destRoot)
        {
            using (var zip = Open(archivePath))
            {
                string root = Path.GetFullPath(destRoot);
                foreach (var entry in zip.Entries)
                {
                    if (!entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    if (entry.FullName.EndsWith("/")) continue; // directory entry
                    string dest = Path.GetFullPath(Path.Combine(destRoot, Fs.ToNative(entry.FullName)));
                    if (!dest.StartsWith(root, StringComparison.Ordinal)) continue; // zip-slip guard
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    entry.ExtractToFile(dest, true);
                }
            }
        }
    }
}
