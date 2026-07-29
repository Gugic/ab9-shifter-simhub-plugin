using System;
using System.Reflection;

namespace AB9ActiveShifter
{
    /// <summary>What build this is, for anything that has to say so.</summary>
    public static class PluginInfo
    {
        private static string _version;

        /// <summary>
        /// The version stamped into the assembly at build time. A release build carries a plain
        /// number; CI adds a suffix and the commit it was built from, trimmed here to the short
        /// hash - which is the one thing worth having in a bug report, and unreadable at forty
        /// characters. Falls back to the assembly version if nothing stamped an informational one.
        /// </summary>
        public static string Version
        {
            get
            {
                if (_version != null) return _version;

                Assembly asm = typeof(PluginInfo).Assembly;
                AssemblyInformationalVersionAttribute info = (AssemblyInformationalVersionAttribute)
                    Attribute.GetCustomAttribute(asm, typeof(AssemblyInformationalVersionAttribute));

                string version = info != null && !string.IsNullOrEmpty(info.InformationalVersion)
                    ? info.InformationalVersion
                    : asm.GetName().Version.ToString();

                int plus = version.IndexOf('+');
                if (plus >= 0 && version.Length > plus + 8) version = version.Substring(0, plus + 8);

                _version = version;
                return _version;
            }
        }
    }
}
