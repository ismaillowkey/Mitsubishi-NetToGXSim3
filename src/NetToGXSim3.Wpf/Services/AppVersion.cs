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
        /// Gets the clean semantic version string (e.g. "0.7.2").
        /// </summary>
        public static string Version
        {
            get
            {
                if (_version == null)
                {
                    _version = ResolveVersion();
                }
                return _version;
            }
        }

        /// <summary>
        /// Gets the display version with leading 'v' (e.g. "v0.7.2").
        /// </summary>
        public static string DisplayVersion
        {
            get
            {
                if (_displayVersion == null)
                {
                    _displayVersion = "v" + Version;
                }
                return _displayVersion;
            }
        }

        private static string ResolveVersion()
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();

                // 1. Coba baca dari AssemblyInformationalVersion (dihasilkan otomatis dari Directory.Build.props)
                AssemblyInformationalVersionAttribute? infoAttr = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                if (infoAttr != null && !string.IsNullOrWhiteSpace(infoAttr.InformationalVersion))
                {
                    string infoVer = infoAttr.InformationalVersion;
                    int plusIdx = infoVer.IndexOf('+');
                    if (plusIdx > 0)
                    {
                        return infoVer.Substring(0, plusIdx).Trim();
                    }
                    return infoVer.Trim();
                }

                // 2. Fallback baca dari Assembly Version
                Version? asmVer = asm.GetName().Version;
                if (asmVer != null)
                {
                    return $"{asmVer.Major}.{asmVer.Minor}.{asmVer.Build}";
                }
            }
            catch
            {
                // Abaikan error dan gunakan versi Core
            }

            // 3. Fallback dinamis ke NetToGXSim3.Core.AppVersionInfo (juga membaca dari assembly assembly version)
            return NetToGXSim3.Core.AppVersionInfo.Version;
        }
    }
}
