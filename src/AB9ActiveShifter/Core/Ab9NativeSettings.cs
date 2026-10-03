using System;
using System.Collections.Generic;

namespace AB9ActiveShifter.Core
{
    /// <summary>A transaction snapshot of the base's settings; profiles persist their own scalar dials.</summary>
    public sealed class Ab9NativeSettings
    {
        public int Torque { get; set; }
        public int OverallIntensity { get; set; }
        public int Spring { get; set; }
        public int Damper { get; set; }
        public int Inertia { get; set; }
        public int Friction { get; set; }
        public int GameGain { get; set; }
        public int FfbMode { get; set; }
        public int Layout { get; set; }
        public int MechanicalResistance { get; set; }

        public static readonly Ab9Parameter[] ReadParameters =
        {
            Ab9Parameter.InputMode, Ab9Parameter.FfbMode, Ab9Parameter.Torque,
            Ab9Parameter.OverallIntensity, Ab9Parameter.Spring, Ab9Parameter.Damper,
            Ab9Parameter.Inertia, Ab9Parameter.Friction, Ab9Parameter.GameGain,
            Ab9Parameter.Layout, Ab9Parameter.MechanicalResistance
        };

        public static Ab9NativeSettings FromValues(IDictionary<Ab9Parameter, int> values)
        {
            return new Ab9NativeSettings
            {
                Torque = values[Ab9Parameter.Torque],
                OverallIntensity = values[Ab9Parameter.OverallIntensity],
                Spring = values[Ab9Parameter.Spring],
                Damper = values[Ab9Parameter.Damper],
                Inertia = values[Ab9Parameter.Inertia],
                Friction = values[Ab9Parameter.Friction],
                GameGain = values[Ab9Parameter.GameGain],
                FfbMode = values[Ab9Parameter.FfbMode],
                Layout = values[Ab9Parameter.Layout],
                MechanicalResistance = values[Ab9Parameter.MechanicalResistance]
            };
        }

        public Ab9NativeSettings Copy() { return (Ab9NativeSettings)MemberwiseClone(); }

        public IEnumerable<KeyValuePair<Ab9Parameter, int>> Writes(bool native)
        {
            // Validate the entire draft before sending even its first value. Spring goes first
            // so applying the virtual setup cannot lift hardware gain while centring remains on.
            var values = new List<KeyValuePair<Ab9Parameter, int>>
            {
                Pair(Ab9Parameter.Spring, Spring), Pair(Ab9Parameter.Damper, Damper),
                Pair(Ab9Parameter.Inertia, Inertia), Pair(Ab9Parameter.Friction, Friction),
                Pair(Ab9Parameter.FfbMode, FfbMode), Pair(Ab9Parameter.GameGain, GameGain),
                Pair(Ab9Parameter.OverallIntensity, OverallIntensity), Pair(Ab9Parameter.Torque, Torque)
            };
            if (native)
            {
                values.Add(Pair(Ab9Parameter.Layout, Layout));
                values.Add(Pair(Ab9Parameter.MechanicalResistance, MechanicalResistance));
            }
            foreach (var value in values) Ab9NativeProtocol.Write(value.Key, value.Value);
            return values;
        }

        public static Ab9NativeSettings VirtualSetup()
        {
            return new Ab9NativeSettings
            {
                Torque = 100,
                OverallIntensity = 100,
                GameGain = 100,
                FfbMode = 1,
                Spring = 0,
                Damper = 15,
                Inertia = 0,
                Friction = 0
            };
        }

        public IEnumerable<KeyValuePair<Ab9Parameter, int>> TransactionWrites(int? inputMode)
        {
            var tune = Writes(inputMode != 0);
            if (inputMode.HasValue) Ab9NativeProtocol.Write(Ab9Parameter.InputMode, inputMode.Value);
            var result = new List<KeyValuePair<Ab9Parameter, int>> { Pair(Ab9Parameter.Torque, 0) };
            foreach (var value in tune) if (value.Key != Ab9Parameter.Torque) result.Add(value);
            if (inputMode.HasValue) result.Add(Pair(Ab9Parameter.InputMode, inputMode.Value));
            result.Add(Pair(Ab9Parameter.Torque, Torque));
            return result;
        }

        private static KeyValuePair<Ab9Parameter, int> Pair(Ab9Parameter parameter, int value)
        {
            return new KeyValuePair<Ab9Parameter, int>(parameter, value);
        }
    }

    public sealed class Ab9NativeSnapshot
    {
        public string Port { get; }
        public Version Firmware { get; }
        public int? InputMode { get; }
        public Ab9NativeSettings Settings { get; }
        public string Status { get; }
        public bool CanManage { get; }
        public bool IsNative { get { return InputMode == 1; } }

        public Ab9NativeSnapshot(string status, string port = null, Version firmware = null,
            int? inputMode = null, Ab9NativeSettings settings = null, bool connected = false)
        {
            Status = status;
            Port = port;
            Firmware = firmware;
            InputMode = inputMode;
            Settings = settings;
            CanManage = connected && settings != null && inputMode.HasValue
                && Ab9NativeProtocol.IsSupported(Ab9NativeProtocol.VendorId, Ab9NativeProtocol.ProductId, firmware);
        }
    }
}
