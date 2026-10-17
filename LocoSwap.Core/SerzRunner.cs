using LocoSwap.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace LocoSwap
{
    /// <summary>
    /// Runs DTG's serz.exe (the .bin &lt;-&gt; .xml converter shipped with Train Simulator).
    /// On Linux this goes through Wine or Proton, as configured in the settings.
    /// </summary>
    public static class SerzRunner
    {
        // Wine start-up is slow and parallel scans can launch hundreds of conversions.
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(4);
        private const int TimeoutMs = 5 * 60 * 1000;

        /// <summary>Overridable for tests: given the file to convert, returns the process start info.</summary>
        public static Func<string, ProcessStartInfo> StartInfoFactory = DefaultStartInfo;

        public static string Run(string filePath)
        {
            Gate.Wait();
            try
            {
                var psi = StartInfoFactory(filePath);
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;

                var output = new StringBuilder();
                Process process;
                try
                {
                    process = new Process { StartInfo = psi };
                    process.OutputDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                    process.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                    process.Start();
                }
                catch (Win32Exception e)
                {
                    throw new InvalidOperationException(
                        string.Format("Could not start '{0}' to run serz.exe ({1}). Check the runner command in Settings (is Wine installed?).", psi.FileName, e.Message), e);
                }

                using (process)
                {
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    if (!process.WaitForExit(TimeoutMs))
                    {
                        try { process.Kill(true); } catch (Exception) { }
                        throw new TimeoutException("serz.exe did not finish in time for " + filePath);
                    }
                    process.WaitForExit(); // make sure async output handlers have flushed
                }
                string text;
                lock (output) text = output.ToString();
                Log.Debug("serz output for {0}: {1}", filePath, text);
                return text;
            }
            finally
            {
                Gate.Release();
            }
        }

        private static ProcessStartInfo DefaultStartInfo(string filePath)
        {
            var settings = Settings.Default;
            string serz = Utilities.GetSerzPath();
            var psi = new ProcessStartInfo();

            if (OperatingSystem.IsWindows() && string.IsNullOrWhiteSpace(settings.RunnerCommand))
            {
                psi.FileName = serz;
                psi.ArgumentList.Add(filePath);
                return psi;
            }

            var tokens = Tokenize(string.IsNullOrWhiteSpace(settings.RunnerCommand) ? "wine" : settings.RunnerCommand);
            psi.FileName = tokens[0];
            for (int i = 1; i < tokens.Count; i++) psi.ArgumentList.Add(tokens[i]);
            psi.ArgumentList.Add(serz);
            psi.ArgumentList.Add(Fs.ToWinePath(filePath));

            psi.Environment["WINEDEBUG"] = "-all";
            foreach (var kv in ParseEnv(settings.RunnerEnv)) psi.Environment[kv.Key] = kv.Value;
            return psi;
        }

        public static List<KeyValuePair<string, string>> ParseEnv(string text)
        {
            var result = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrWhiteSpace(text)) return result;
            foreach (var part in text.Split(new[] { '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                result.Add(new KeyValuePair<string, string>(part.Substring(0, eq).Trim(), part.Substring(eq + 1).Trim()));
            }
            return result;
        }

        /// <summary>Splits a command line on spaces, honouring double quotes.</summary>
        public static List<string> Tokenize(string commandLine)
        {
            var tokens = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;
            bool hasToken = false;
            foreach (char c in commandLine)
            {
                if (c == '"') { inQuotes = !inQuotes; hasToken = true; }
                else if (char.IsWhiteSpace(c) && !inQuotes)
                {
                    if (hasToken) { tokens.Add(current.ToString()); current.Clear(); hasToken = false; }
                }
                else { current.Append(c); hasToken = true; }
            }
            if (hasToken) tokens.Add(current.ToString());
            if (tokens.Count == 0) tokens.Add("wine");
            return tokens;
        }
    }
}
