using System.Collections.Generic;
using System.Reflection;

namespace AB9ActiveShifter
{
    /// <summary>One named configuration: a pattern plus every dial tuned for it.</summary>
    public sealed class ShifterProfile
    {
        public string Name { get; set; }
        public ShifterSettings Settings { get; set; }
    }

    /// <summary>
    /// What is actually persisted: every profile plus which one is live. Replaces the single
    /// flat <see cref="ShifterSettings"/> that used to be the whole settings file; a legacy
    /// file deserialises into an empty store (its properties simply do not match), which is
    /// the migration signal - the plugin then re-reads it as settings and wraps it as the
    /// first profile, so nothing a user tuned is ever lost.
    /// </summary>
    public sealed class ProfileStore
    {
        public List<ShifterProfile> Profiles { get; set; }
        public string ActiveProfile { get; set; }

        public ShifterProfile FindActive()
        {
            if (Profiles == null || Profiles.Count == 0) return null;

            foreach (ShifterProfile p in Profiles)
            {
                if (p != null && p.Name == ActiveProfile && p.Settings != null) return p;
            }

            foreach (ShifterProfile p in Profiles)
            {
                if (p != null && p.Settings != null) return p;
            }

            return null;
        }

        public bool NameTaken(string name)
        {
            if (Profiles == null) return false;
            foreach (ShifterProfile p in Profiles)
            {
                if (p != null && p.Name == name) return true;
            }
            return false;
        }

        /// <summary>A name not yet in the store, built from the requested one.</summary>
        public string UniqueName(string requested)
        {
            string baseName = string.IsNullOrWhiteSpace(requested) ? "Profile" : requested.Trim();
            if (!NameTaken(baseName)) return baseName;

            for (int i = 2; ; i++)
            {
                string candidate = baseName + " " + i;
                if (!NameTaken(candidate)) return candidate;
            }
        }
    }

    public static class SettingsCloner
    {
        /// <summary>
        /// Copies every public read/write property by reflection, the same trick the trace
        /// header uses: new dials are included automatically, and no event subscriptions ride
        /// along the way a MemberwiseClone would carry them.
        /// </summary>
        public static ShifterSettings Clone(ShifterSettings source)
        {
            ShifterSettings copy = new ShifterSettings();
            if (source == null) return copy;

            foreach (PropertyInfo p in typeof(ShifterSettings).GetProperties(
                         BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanRead || !p.CanWrite) continue;
                p.SetValue(copy, p.GetValue(source, null), null);
            }

            return copy;
        }
    }
}
