// Reference-only declarations of the telemetry types this plugin reads.
//
// NewData and OldData really are fields on the real GameData, not properties, and the
// StatusDataBase members really are properties. That distinction is not cosmetic: a field read
// compiles to ldfld and a property read to callvirt, so getting it backwards yields a DLL that
// builds and then throws on the rig.

namespace GameReaderCommon
{
    public class GameData
    {
        public bool GameRunning { get; set; }

        public StatusDataBase OldData;

        public StatusDataBase NewData;
    }

    public class StatusDataBase
    {
        /// <summary>Engine speed, rpm.</summary>
        public double Rpms { get; set; }

        /// <summary>Redline, rpm.</summary>
        public double MaxRpm { get; set; }

        public double SpeedKmh { get; set; }

        /// <summary>Clutch pedal, 0..100 with 100 fully pressed.</summary>
        public double Clutch { get; set; }

        /// <summary>The game's own gear label, e.g. "N", "1", "R".</summary>
        public string Gear { get; set; }

        /// <summary>Non-zero while ABS is modulating.</summary>
        public int ABSActive { get; set; }

        /// <summary>Non-zero while traction control is cutting.</summary>
        public int TCActive { get; set; }

        /// <summary>Vertical acceleration in G. Null when the game does not report it.</summary>
        public double? AccelerationHeave { get; set; }
    }
}
