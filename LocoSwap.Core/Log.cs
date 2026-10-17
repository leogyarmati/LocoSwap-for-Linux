using System;
using System.IO;

namespace LocoSwap
{
    /// <summary>
    /// Tiny replacement for Serilog (keeps the core free of NuGet dependencies).
    /// Accepts the same "{0}"-style positional format strings the original code used.
    /// </summary>
    public static class Log
    {
        private static readonly object Sync = new object();
        private static StreamWriter _writer;
        public static bool Echo { get; set; } = true;

        public static void Init(string logFilePath)
        {
            lock (Sync)
            {
                try
                {
                    _writer?.Dispose();
                    Directory.CreateDirectory(Path.GetDirectoryName(logFilePath));
                    _writer = new StreamWriter(new FileStream(logFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
                }
                catch (Exception)
                {
                    _writer = null;
                }
            }
        }

        public static void Debug(string format, params object[] args) => Write("DBG", format, args);
        public static void Information(string format, params object[] args) => Write("INF", format, args);
        public static void Warning(string format, params object[] args) => Write("WRN", format, args);
        public static void Error(string format, params object[] args) => Write("ERR", format, args);

        private static void Write(string level, string format, object[] args)
        {
            string message;
            try
            {
                message = (args == null || args.Length == 0) ? format : string.Format(format, args);
            }
            catch (FormatException)
            {
                message = format + " | " + string.Join(" | ", args);
            }
            var line = string.Format("{0:HH:mm:ss.fff} [{1}] {2}", DateTime.Now, level, message);
            lock (Sync)
            {
                if (Echo) Console.Error.WriteLine(line);
                try { _writer?.WriteLine(line); } catch (Exception) { }
            }
        }
    }
}
