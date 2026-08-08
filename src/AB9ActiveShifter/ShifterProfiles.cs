using System;
using System.Collections.Generic;
using System.Reflection;

namespace AB9ActiveShifter
{
    /// <summary>One named configuration: a pattern plus every dial tuned for it.</summary>
    public sealed class ShifterProfile
    {
        public string Name { get; set; }
        public ShifterSettings Settings { get; set; }

        /// <summary>
        /// Optional vehicle ids (whatever the game's car-model telemetry reports) that should
        /// activate this profile automatically. Never travels through <see cref="ProfileTransfer"/> -
        /// that only reflects over <see cref="ShifterSettings"/> - so a shared tune cannot silently
        /// steal another car's mapping on import.
        /// </summary>
        public List<string> CarModels { get; set; } = new List<string>();
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

        /// <summary>
        /// Whether the shifter is running, and whether the stick is free. These are the only two
        /// switches that describe the <em>session</em> rather than a gate, and they live here
        /// rather than in a profile for a reason learned the hard way.
        /// <para>
        /// They used to be per-profile like everything else, so switching profiles handed the
        /// decision to whichever one you landed on: every shipped profile starts disabled, so
        /// moving off the one you had enabled stopped the base. The first attempt at a fix copied
        /// the switch from the outgoing profile onto the incoming one, which is worse - it makes
        /// the profile you happen to be leaving the authority, and it <em>writes</em> that onto
        /// the profile you arrive at. Starting on a disabled profile and switching away therefore
        /// destroyed the enabled flag on the profile you switched to, permanently, because the
        /// store is saved on every activation. Measured on the rig within a minute of deploying it.
        /// </para>
        /// <para>
        /// Nullable so that a settings file written before this existed can be told apart from one
        /// that genuinely says "off": null means migrate from whichever profile is active.
        /// </para>
        /// </summary>
        public bool? SessionEnabled { get; set; }

        public bool? SessionFreeStick { get; set; }

        /// <summary>
        /// Whether a profile switch thumps the lever once per profile number, so which one
        /// arrived can be counted by hand rather than read off a screen. On by default: the whole
        /// reason profile switching moved onto a hotkey was to stop needing to look at the screen.
        /// </summary>
        public bool ConfirmProfileSwitch { get; set; } = true;

        /// <summary>
        /// Where the active profile sits in the list, zero-based, or -1 if there is no match. The
        /// confirmation count is built from this, so it follows the order shown in the dropdown -
        /// the same order a user would count in their head.
        /// </summary>
        public int IndexOfActive()
        {
            if (Profiles == null) return -1;

            for (int i = 0; i < Profiles.Count; i++)
            {
                ShifterProfile p = Profiles[i];
                if (p != null && p.Name == ActiveProfile) return i;
            }
            return -1;
        }

        /// <summary>
        /// The profiles a bound Next/Previous hotkey walks through, in order. Empty means every
        /// profile, which is what a user who never opens the list gets and is the obvious reading
        /// of "cycle profiles".
        /// <para>
        /// It lives on the store rather than in <see cref="ShifterSettings"/> deliberately: a
        /// per-profile list would be a different list in every profile, so cycling would change
        /// the very thing defining the cycle and the second press would go somewhere the first
        /// press did not promise.
        /// </para>
        /// </summary>
        public List<string> CycleProfiles { get; set; }

        /// <summary>
        /// The profile a hotkey press should activate, walking <paramref name="direction"/> (+1 or
        /// -1) around the cycle. Returns null when there is nowhere to go.
        /// <para>
        /// Names in the cycle that no longer exist are skipped rather than treated as an error -
        /// a profile can be renamed or deleted while the list still mentions it, and a hotkey that
        /// silently did nothing would be blamed on the binding rather than on the stale entry.
        /// </para>
        /// </summary>
        public string NextInCycle(string current, int direction)
        {
            List<string> ring = CycleOrder();
            if (ring.Count == 0) return null;

            int index = ring.IndexOf(current ?? "");

            // Not in the ring at all - a hotkey press should still land somewhere sensible, so
            // going forward starts at the beginning and going back starts at the end.
            if (index < 0) return direction >= 0 ? ring[0] : ring[ring.Count - 1];

            if (ring.Count == 1) return null;

            int step = direction >= 0 ? 1 : -1;
            int next = ((index + step) % ring.Count + ring.Count) % ring.Count;
            return ring[next];
        }

        /// <summary>The cycle as it actually stands: chosen names that still exist, else all.</summary>
        public List<string> CycleOrder()
        {
            var order = new List<string>();
            if (Profiles == null) return order;

            if (CycleProfiles != null && CycleProfiles.Count > 0)
            {
                foreach (string name in CycleProfiles)
                {
                    if (name != null && NameTaken(name) && !order.Contains(name)) order.Add(name);
                }
                if (order.Count > 0) return order;
            }

            foreach (ShifterProfile p in Profiles)
            {
                if (p != null && p.Name != null && p.Settings != null) order.Add(p.Name);
            }
            return order;
        }

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

        /// <summary>
        /// Whether any profile has opted into car-model auto-switching at all.
        ///
        /// Exists to keep the feature off the telemetry path for everyone who does not use it.
        /// <c>DataUpdate</c> runs on SimHub's critical path and the car-model read is a property
        /// system lookup; this is a walk over three or four profiles whose lists are empty, which
        /// short-circuits on the first count. The custom-property effect right beside it is gated
        /// the same way, on its own enable flag - a feature nobody has configured should cost
        /// nothing per tick.
        /// </summary>
        public bool AnyCarModels
        {
            get
            {
                if (Profiles == null) return false;

                foreach (ShifterProfile p in Profiles)
                {
                    if (p != null && p.CarModels != null && p.CarModels.Count > 0) return true;
                }

                return false;
            }
        }

        /// <summary>
        /// The first profile whose vehicle list names this car, or null if none claims it. Order
        /// follows <see cref="Profiles"/>, so if more than one profile lists the same car, the
        /// earlier one wins - deliberately simple, since resolving a genuine conflict is the
        /// user's call, not something to guess at automatically.
        /// </summary>
        public ShifterProfile FindByCarModel(string carModel)
        {
            if (string.IsNullOrEmpty(carModel) || Profiles == null) return null;

            foreach (ShifterProfile p in Profiles)
            {
                if (p == null || p.CarModels == null) continue;
                foreach (string model in p.CarModels)
                {
                    if (string.Equals(model, carModel, StringComparison.OrdinalIgnoreCase)) return p;
                }
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
