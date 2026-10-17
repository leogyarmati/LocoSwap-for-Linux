using System.Reflection;

namespace LocoSwap
{
    public static class AppInfo
    {
        /// <summary>Version as set in the project file, e.g. "0.1.0-beta.1" (without the +commit suffix).</summary>
        public static string Version
        {
            get
            {
                var asm = typeof(AppInfo).Assembly;
                var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                           ?? asm.GetName().Version?.ToString() ?? "";
                int plus = info.IndexOf('+');
                return plus >= 0 ? info.Substring(0, plus) : info;
            }
        }
    }
}
