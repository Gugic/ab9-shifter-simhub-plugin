using System;
using System.Collections.Generic;

namespace AB9ActiveShifter.Core
{
    /// <summary>
    /// AB9 CDC configuration facts, implemented independently of AZOM. No hardware access.
    /// These settings live on the base, not in a virtual shifter profile. See docs/native-ab9.md
    /// for the wire evidence, including Cockpit's DirectInput enum and the firmware floor.
    /// </summary>
    public static class Ab9NativeProtocol
    {
        public const int VendorId = 0x346E;
        public const int ProductId = 0x1000;
        public static readonly Version MinimumFirmware = new Version(1, 1, 5, 2);

        public static bool IsSupported(int vendor, int product, Version firmware)
        {
            return vendor == VendorId && product == ProductId && firmware != null
                && firmware.CompareTo(MinimumFirmware) >= 0;
        }

        public static Version FirmwareFromReply(byte[] payload)
        {
            return payload != null && payload.Length == 4
                // AB9 stores the final pair in build/patch order. Measured on the rig:
                // 01 01 02 05 is displayed by Cockpit as 1.1.5.2, not 1.1.2.5.
                ? new Version(payload[0], payload[1], payload[3], payload[2]) : null;
        }

        public static byte[] Read(Ab9Parameter parameter)
        {
            byte command = (byte)parameter;
            // Cockpit pads the 16-bit reads; input mode, layout and game gain use short reads.
            return parameter == Ab9Parameter.InputMode || parameter == Ab9Parameter.Layout
                || parameter == Ab9Parameter.GameGain
                ? Frame(0x1E, command) : Frame(0x1E, command, 0, 0);
        }

        public static byte[] Write(Ab9Parameter parameter, int value)
        {
            int maximum = parameter == Ab9Parameter.InputMode ? 1
                : parameter == Ab9Parameter.FfbMode ? 2
                : parameter == Ab9Parameter.Layout ? 9 : 100;
            if (!Enum.IsDefined(typeof(Ab9Parameter), parameter) || value < 0 || value > maximum)
                throw new ArgumentOutOfRangeException(nameof(value));
            return parameter == Ab9Parameter.InputMode
                ? Frame(0x1F, (byte)parameter, (byte)value)
                : Frame(0x1F, (byte)parameter, 0, (byte)value);
        }

        public static bool TryValue(Ab9Frame frame, Ab9Parameter parameter, out int value)
        {
            value = 0;
            if (frame == null || frame.Group != 0x9E || frame.Device != 0x21
                || frame.Payload.Length < 2 || frame.Payload[0] != (byte)parameter) return false;
            if (parameter == Ab9Parameter.InputMode)
            {
                if (frame.Payload.Length != 2 || frame.Payload[1] > 1) return false;
                value = frame.Payload[1];
                return true;
            }
            if (frame.Payload.Length != 3) return false;
            value = frame.Payload[1] * 256 + frame.Payload[2];
            int maximum = parameter == Ab9Parameter.FfbMode ? 2
                : parameter == Ab9Parameter.Layout ? 9 : 100;
            return value <= maximum;
        }

        public static byte[] Frame(byte group, params byte[] payload)
        {
            if (payload == null || payload.Length > 64) throw new ArgumentException("Invalid payload.");
            var bytes = new List<byte> { 0x7E, (byte)payload.Length };
            int checksum = 0x0D + 0x7E + payload.Length;
            var body = new List<byte> { group, 0x12 };
            body.AddRange(payload);
            foreach (byte b in body)
            {
                bytes.Add(b);
                checksum += b;
                if (b == 0x7E) { bytes.Add(b); checksum += b; }
            }
            byte tail = (byte)(checksum & 255);
            bytes.Add(tail);
            if (tail == 0x7E) bytes.Add(tail);
            return bytes.ToArray();
        }
    }

    public enum Ab9Parameter : byte
    {
        InputMode = 0x5D,
        FfbMode = 0x85,
        GameGain = 0x99,
        Torque = 0xA9,
        OverallIntensity = 0xAE,
        Spring = 0xAF,
        Damper = 0xB0,
        Inertia = 0xB1,
        Friction = 0xB2,
        Layout = 0xD3,
        MechanicalResistance = 0xD6
    }

    public sealed class Ab9Frame
    {
        public byte Group { get; }
        public byte Device { get; }
        public byte[] Payload { get; }
        public Ab9Frame(byte group, byte device, byte[] payload)
        {
            Group = group;
            Device = device;
            Payload = payload;
        }
    }

    /// <summary>Bounded, incremental decoder: tolerates split packets, escaped bytes and noise.</summary>
    public sealed class Ab9FrameReader
    {
        private readonly List<byte> _buffer = new List<byte>();

        public void Add(byte value)
        {
            if (_buffer.Count >= 4096) _buffer.Clear();
            _buffer.Add(value);
        }

        public bool TryTake(out Ab9Frame frame)
        {
            frame = null;
            while (_buffer.Count >= 2)
            {
                if (_buffer[0] != 0x7E || _buffer[1] > 64) { _buffer.RemoveAt(0); continue; }
                int length = _buffer[1], index = 2, checksum = 0x0D + 0x7E + length;
                var decoded = new List<byte>();
                bool corrupt = false;
                for (int count = 0; count < length + 3; count++)
                {
                    if (index >= _buffer.Count) return false;
                    byte b = _buffer[index++];
                    if (b == 0x7E)
                    {
                        if (index >= _buffer.Count) return false;
                        if (_buffer[index++] != 0x7E) { corrupt = true; break; }
                        if (count < length + 2) checksum += b;
                    }
                    decoded.Add(b);
                    if (count < length + 2) checksum += b;
                }
                if (corrupt || decoded[decoded.Count - 1] != (byte)(checksum & 255))
                {
                    _buffer.RemoveAt(0);
                    continue;
                }
                _buffer.RemoveRange(0, index);
                frame = new Ab9Frame(decoded[0], decoded[1], decoded.GetRange(2, length).ToArray());
                return true;
            }
            return false;
        }
    }
}
