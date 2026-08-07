using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using AB9ActiveShifter.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AB9ActiveShifter
{
    /// <summary>A file that is not a profile, or is one this build cannot read at all.</summary>
    public class ProfileTransferException : Exception
    {
        public ProfileTransferException(string message) : base(message) { }
    }

    /// <summary>What an import actually did, so the user is told rather than guessing.</summary>
    public sealed class ProfileImportResult
    {
        public ShifterProfile Profile { get; set; }

        /// <summary>Dials taken from the file.</summary>
        public int Applied { get; set; }

        /// <summary>Dials taken but pulled back into range - the file asked for something silly.</summary>
        public int Clamped { get; set; }

        /// <summary>Keys in the file this build has no dial for, usually a newer version's.</summary>
        public int Unknown { get; set; }
    }

    /// <summary>
    /// Reading and writing a single profile as a file, so a tune can be shared.
    /// <para>
    /// Two rules shape the format. First, a profile is <em>tuning</em>: the things that are true
    /// of a machine rather than of a feel - measured polarity, device and vJoy ids, loop rate -
    /// are not written, and on import the receiving machine's own values are kept. A shared file
    /// that carried someone else's polarity would drive the gate backwards on arrival, and one
    /// that carried their confirmation flag would lift the 10% cap on a base nobody had measured.
    /// Second, nothing that arrives from outside is trusted: every value is range-checked on the
    /// way in, and <c>Enabled</c> is forced off whatever the file says. Opening a file must never
    /// start applying force.
    /// </para>
    /// <para>
    /// The clamps here are a safety envelope, not a copy of the sliders' limits - they exist so a
    /// hand-edited or corrupt file cannot ask for a thousand percent gain, and they are
    /// deliberately looser than the UI so a legitimately tuned file is never quietly altered.
    /// </para>
    /// </summary>
    public static class ProfileTransfer
    {
        public const string FormatId = "AB9ActiveShifter.Profile";

        /// <summary>Bumped only when an older build could misread a newer file, not on every dial.</summary>
        public const int FormatVersion = 1;

        public const string FileExtension = ".ab9profile.json";

        /// <summary>Longest name accepted from a file; longer ones are cut, not rejected.</summary>
        public const int MaxNameLength = 60;

        private const int MaxStringLength = 200;

        /// <summary>
        /// Properties that describe this machine or this moment, never a feel. Excluded from the
        /// file, and on import left at the receiving machine's values.
        /// </summary>
        private static readonly HashSet<string> NotShared = new HashSet<string>(StringComparer.Ordinal)
        {
            // Measured, per unit. Someone else's answer is worse than no answer.
            "PolarityConfirmed", "InvertConstantX", "InvertConstantY", "CalibrationForcePct",

            // This machine's hardware and loop.
            "VendorId", "ProductId", "VendorIdHex", "ProductIdHex", "VJoyDeviceId", "TickHz",

            // The clutch pedal binding: a device id that means nothing on another machine, and a
            // travel measured on pedals nobody here owns. ClutchSource goes with them, because it
            // is only meaningful if the binding is - a file arriving with it set to Pedal against
            // no calibration would read the clutch as permanently released and grind on every
            // shift. The bite point and the grind mode DO travel: those are feel, not hardware.
            "PedalDeviceId", "PedalAxisIndex", "PedalRawMin", "PedalRawMax",
            "PedalDeadzoneLow", "PedalDeadzoneHigh", "PedalInvert", "ClutchSource",

            // Live switches. An imported file must not arm anything or take the device.
            "Enabled", "FreeStick",

            // Adapters the XAML binds, each fully derived from a dial that is written.
            "PatternIndex", "MouthShapeIndex", "SeqThrow", "ClutchSourceIndex",
            "GrindClutchModeIndex",

            // The Feel tab's percent-of-column-spacing display toggle: each is fully derived
            // from the raw dial it shares a backing field with. Sharing both would write the
            // same fact twice under two keys, applied back in alphabetical order on import -
            // whichever name sorts last would silently win over the other.
            "WallRampPercent", "SlotHalfWidthPercent", "LockoutHalfWidthPercent",
            "BarrierWidthPercent", "WallBlendPercent", "ColumnEdgeEnterPercent",
            "ColumnInnerHalfEnterPercent", "ColumnInnerHalfExitPercent", "DetentHysteresisPercent"
        };

        /// <summary>The dials a profile file carries, in a stable order.</summary>
        private static IEnumerable<PropertyInfo> SharedProperties()
        {
            List<PropertyInfo> shared = new List<PropertyInfo>();
            foreach (PropertyInfo p in typeof(ShifterSettings).GetProperties(
                         BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanRead || !p.CanWrite) continue;
                if (NotShared.Contains(p.Name)) continue;
                shared.Add(p);
            }
            shared.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return shared;
        }

        public static string Export(ShifterProfile profile)
        {
            if (profile == null || profile.Settings == null)
            {
                throw new ProfileTransferException("There is no profile to export.");
            }

            JObject dials = new JObject();
            foreach (PropertyInfo p in SharedProperties())
            {
                object value = p.GetValue(profile.Settings, null);
                if (value == null) continue;

                // Enums by name: a shared file is read by people, and "H7R" survives a
                // renumbering of the enum in a way that "0" does not.
                dials.Add(p.Name, p.PropertyType.IsEnum
                    ? new JValue(value.ToString())
                    : JToken.FromObject(value));
            }

            JObject root = new JObject();
            root.Add("Format", FormatId);
            root.Add("FormatVersion", FormatVersion);
            root.Add("ExportedBy", PluginInfo.Version);
            root.Add("Name", profile.Name ?? "Profile");
            root.Add("Settings", dials);

            // JsonConvert rather than JToken.ToString(Formatting): the same call through the
            // token added an overload between 13.0.3 and 13.0.4, and this one has been stable
            // across every version SimHub might ship.
            return JsonConvert.SerializeObject(root, Formatting.Indented);
        }

        /// <summary>
        /// Reads a profile file. <paramref name="localFacts"/> supplies everything the file does
        /// not carry - this machine's polarity, ids and loop rate - so pass the settings currently
        /// in use. Throws <see cref="ProfileTransferException"/> only when the file is not a
        /// profile at all; anything merely odd inside it is clamped or skipped and reported.
        /// </summary>
        public static ProfileImportResult Import(string json, ShifterSettings localFacts)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ProfileTransferException("That file is empty.");
            }

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonException ex)
            {
                throw new ProfileTransferException("That file is not valid JSON: " + ex.Message);
            }

            JToken format = root["Format"];
            if (format == null || !string.Equals((string)format, FormatId, StringComparison.Ordinal))
            {
                throw new ProfileTransferException(
                    "That is not an AB9 Active Shifter profile. Profiles are the files this plugin's " +
                    "Export button writes, not the settings file SimHub keeps.");
            }

            int fileVersion = 0;
            JToken versionToken = root["FormatVersion"];
            if (versionToken != null) int.TryParse(versionToken.ToString(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out fileVersion);
            if (fileVersion > FormatVersion)
            {
                throw new ProfileTransferException(
                    "That profile was written by a newer version of the plugin (format " + fileVersion +
                    ", this build reads " + FormatVersion + "). Update the plugin and try again.");
            }

            JObject dials = root["Settings"] as JObject;
            if (dials == null)
            {
                throw new ProfileTransferException("That profile has no settings in it.");
            }

            // Start from this machine's settings so everything the file does not carry stays as
            // measured here, then overlay the shared dials.
            ShifterSettings settings = SettingsCloner.Clone(localFacts);
            ProfileImportResult result = new ProfileImportResult();

            HashSet<string> known = new HashSet<string>(StringComparer.Ordinal);

            foreach (PropertyInfo p in SharedProperties())
            {
                known.Add(p.Name);

                JToken token = dials[p.Name];
                if (token == null || token.Type == JTokenType.Null) continue;

                bool clamped;
                object value;
                if (!TryRead(p, token, out value, out clamped)) continue;

                try
                {
                    p.SetValue(settings, value, null);
                    result.Applied++;
                    if (clamped) result.Clamped++;
                }
                catch
                {
                    // A dial that refuses the value keeps the local one. One bad key must not
                    // cost the user the other eighty.
                }
            }

            foreach (KeyValuePair<string, JToken> pair in dials)
            {
                if (!known.Contains(pair.Key)) result.Unknown++;
            }

            if (result.Applied == 0)
            {
                throw new ProfileTransferException(
                    "That profile has no settings this version understands.");
            }

            // Whatever the file said. Opening someone's tune must never take the base or apply force.
            settings.Enabled = false;
            settings.FreeStick = false;

            result.Profile = new ShifterProfile
            {
                Name = CleanName((string)root["Name"]),
                Settings = settings
            };
            return result;
        }

        /// <summary>A file's name, made fit to be a profile name: no control characters, bounded.</summary>
        public static string CleanName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "Imported profile";

            // Control characters become spaces, and runs of whitespace collapse: a name pasted
            // out of a forum post arrives with newlines in it and has to read as one line in a
            // combo box.
            StringBuilder sb = new StringBuilder(raw.Length);
            bool lastWasSpace = false;
            foreach (char c in raw.Trim())
            {
                bool isSpace = char.IsControl(c) || char.IsWhiteSpace(c);
                if (isSpace && lastWasSpace) continue;

                sb.Append(isSpace ? ' ' : c);
                lastWasSpace = isSpace;
            }

            string cleaned = sb.ToString().Trim();
            if (cleaned.Length == 0) return "Imported profile";
            if (cleaned.Length > MaxNameLength) cleaned = cleaned.Substring(0, MaxNameLength).Trim();
            return cleaned;
        }

        /// <summary>
        /// Converts one token to the property's type and holds it inside a sane envelope. False
        /// means the token cannot be that type at all, and the local value should stand.
        /// </summary>
        private static bool TryRead(PropertyInfo p, JToken token, out object value, out bool clamped)
        {
            value = null;
            clamped = false;

            try
            {
                if (p.PropertyType.IsEnum)
                {
                    object parsed = Enum.Parse(p.PropertyType, token.ToString(), true);
                    if (!Enum.IsDefined(p.PropertyType, parsed)) return false;
                    value = parsed;
                    return true;
                }

                if (p.PropertyType == typeof(bool))
                {
                    value = token.ToObject<bool>();
                    return true;
                }

                if (p.PropertyType == typeof(string))
                {
                    string s = token.ToObject<string>() ?? string.Empty;
                    if (s.Length > MaxStringLength)
                    {
                        s = s.Substring(0, MaxStringLength);
                        clamped = true;
                    }
                    value = s;
                    return true;
                }

                if (p.PropertyType == typeof(double))
                {
                    double d = token.ToObject<double>();
                    if (double.IsNaN(d) || double.IsInfinity(d)) return false;
                    double held = Clamp(d, 0.0, 100.0);
                    clamped = held != d;
                    value = held;
                    return true;
                }

                if (p.PropertyType == typeof(int))
                {
                    long raw = token.ToObject<long>();
                    int lo, hi;
                    RangeFor(p.Name, out lo, out hi);
                    int held = (int)Clamp(raw, lo, hi);
                    clamped = held != raw;
                    value = held;
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }

            return false;
        }

        /// <summary>
        /// The envelope a dial has to stay inside, by what it measures. Deliberately coarse and
        /// deliberately wider than the sliders: this is here to stop a corrupt file commanding a
        /// 12 Nm base, not to second-guess a tune.
        /// </summary>
        private static void RangeFor(string name, out int lo, out int hi)
        {
            if (name.EndsWith("Pct", StringComparison.Ordinal))
            {
                // Everything that scales torque. The tightest clamp here, and the reason for it.
                lo = 0; hi = 100; return;
            }
            if (name.EndsWith("Hz", StringComparison.Ordinal) || name == "FxEngineFreqAt1000Rpm")
            {
                lo = 1; hi = 250; return;
            }
            if (name.EndsWith("Ms", StringComparison.Ordinal))
            {
                lo = 0; hi = 5000; return;
            }
            if (name == "GrindMinSpeedKmh")
            {
                lo = 0; hi = 500; return;
            }
            if (name == "MinEngageTicks")
            {
                lo = 1; hi = 200; return;
            }
            if (name == "DamperCoeff")
            {
                lo = 0; hi = 10000; return;
            }

            // Everything else is a distance along an axis, and an axis is 16 bits.
            lo = 0; hi = GateGeometry.AxisMax;
        }

        private static double Clamp(double v, double lo, double hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }
    }
}
