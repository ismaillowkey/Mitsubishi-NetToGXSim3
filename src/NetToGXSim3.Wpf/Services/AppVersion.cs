using System;
using System.Reflection;

namespace NetToGXSim3.Wpf.Services
{
    /// <summary>
    /// Centralized application version provider that reads version information
    /// dynamically from the executing assembly (populated from Directory.Build.props).
    /// </summary>
    public static class AppVersion
    {
        private static string? _version;
        private static string? _displayVersion;

        /// <summary>
        /// Gets the clean semantic version string (e.g. "0.6.1").
        /// </summary>
        public static string Version => _version ??= ResolveVersion();

        /// <summary>
        /// Gets the display version with leading 'v' (e.g. "v0.6.1").
        /// </summary>
        public static string DisplayVersion => _displayVersion ??= $"v{Version}";

        private static string ResolveVersion()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();

                // 1. Try InformationalVersion (e.g. "0.6.1" or "0.6.1+sha")
                var infoAttr = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                if (infoAttr != null && !string.IsNullOrWhiteSpace(infoAttr.InformationalVersion))
                {
                    string infoVer = infoAttr.InformationalVersion;
                    int plusIdx = infoVer.IndexOf('+');
                    return plusIdx > 0 ? infoVer.Substring(0, plusIdx).Trim() : infoVer.Trim();
                }

                // 2. Fallback to AssemblyName.Version
                var asmVer = asm.GetName().Version;
                if (asmVer != null)
                {
                    return $"{asmVer.Major}.{asmVer.Minor}.{asmVer.Build}";
                }
            }
            catch { }

            return "0.6.1";
        }
    }
}
