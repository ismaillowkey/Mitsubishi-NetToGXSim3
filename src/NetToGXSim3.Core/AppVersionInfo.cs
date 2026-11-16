using System;
using System.Reflection;

namespace NetToGXSim3.Core
{
    public static class AppVersionInfo
    {
        private static readonly Version _version = typeof(AppVersionInfo).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);

        public static Version VersionObject => _version;
        public static string Version => $"{_version.Major}.{_version.Minor}.{_version.Build}";
        public static int Major => _version.Major;
        public static int Minor => _version.Minor;
        public static int Build => _version.Build;
    }
}
