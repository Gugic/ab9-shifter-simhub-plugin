using System;

namespace AB9ActiveShifter.Core
{
    /// <summary>
    /// The shift pattern the gate renders. The H patterns share one geometry engine - they
    /// differ only in column count and which slots hold a gear - while Sequential and Prnd
    /// bypass the gate entirely: one for a sprung fore/aft lever with pulsed up/down buttons,
    /// the other for a detented lane of four fixed positions with a button held at each.
    /// </summary>
    public enum GatePattern
    {
        /// <summary>Four columns, 1-7 plus R bottom-right, lockout before the 7/R column.</summary>
        H7R = 0,

        /// <summary>Four columns, 1-6 plus R bottom-right; the slot where 7 would sit does not exist.</summary>
        H6R = 1,

        /// <summary>Three columns, 1-5 plus R bottom-right, no lockout.</summary>
        H5R = 2,

        /// <summary>Fore/aft lever sprung to centre; push fires an up/down button pulse.</summary>
        Sequential = 3,

        /// <summary>
        /// An automatic's selector: one lane, four detented positions, a button held at each.
        /// Not a gate with one column - there is no neutral to come back through and no gear to
        /// engage, only a position the lever is always in. See <see cref="PrndLane"/>.
        /// </summary>
        Prnd = 4,

        /// <summary>
        /// Three columns, two rows, six plain slots on buttons 1-6 and no reverse anywhere in
        /// the map - the sixth slot is just button 6, and the game decides what it means. The
        /// truck layout: issue-style Eaton-Fuller boxes are this pattern with a lockout
        /// configured between the first two columns. Appended last - the pattern is stored as
        /// an int in saved settings, so the existing values must never be renumbered.
        /// </summary>
        H6 = 5
    }

    /// <summary>
    /// Where the lockout lives, if anywhere. Gap numbers are gear-map-relative - Gap1 is the
    /// crossing between the columns holding 1/2 and 3/4 - so <c>MirrorColumns</c> relocates
    /// the gate together with the gears, the same rule that moves 6+R's missing slot.
    /// </summary>
    public enum LockoutPlacement
    {
        /// <summary>What the pattern has always shipped with: 7+R and 6+R guard their last gap, the rest have none.</summary>
        PatternDefault = 0,

        /// <summary>No lockout anywhere, whatever the pattern.</summary>
        Off = 1,

        /// <summary>Between the first and second gear columns.</summary>
        Gap1 = 2,

        /// <summary>Between the second and third gear columns.</summary>
        Gap2 = 3,

        /// <summary>Between the third and fourth gear columns. Repaired to the last gap on a three-column pattern.</summary>
        Gap3 = 4,

        /// <summary>On a single slot's mouth - extra resistance into or out of one gear.</summary>
        Slot = 5
    }

    /// <summary>
    /// Which crossing of a gap lockout pays the toll, in gear-map terms. Names the crossing,
    /// not the force: the force always pushes back toward the side the paying crossing comes
    /// from. TowardHigh is the gate the 7+R pattern has always had.
    /// </summary>
    public enum LockoutGapDirection
    {
        /// <summary>Crossing toward the higher gears pays; coming back is assisted out.</summary>
        TowardHigh = 0,

        /// <summary>Crossing toward the lower gears pays; coming back is assisted out.</summary>
        TowardLow = 1,

        /// <summary>Both crossings pay. Rendered with an edge-flip latch - see ForceComposer.</summary>
        Both = 2
    }

    /// <summary>Which way through a slot lockout pays the toll.</summary>
    public enum LockoutSlotDirection
    {
        /// <summary>Going into the gear pays; pulling out is the ordinary detent.</summary>
        Entry = 0,

        /// <summary>Coming out of the gear pays; going in is the ordinary snick.</summary>
        Exit = 1,

        /// <summary>Both ways pay.</summary>
        Both = 2
    }

    /// <summary>
    /// How the lockout is defeated. Push-through is a toll the hand pays; the two hard modes
    /// pin the force to 100% of the effective gain and hand the key to a SimHub action instead,
    /// differing only in whether the gate re-arms itself.
    /// </summary>
    public enum LockoutMode
    {
        /// <summary>A toll at the configured force; push through it. The default, and today's gate.</summary>
        PushThrough = 0,

        /// <summary>Full force until the release action fires; stays released until engaged again.</summary>
        HotkeyToggle = 1,

        /// <summary>Full force until the release action fires; re-arms itself once the crossing completes.</summary>
        HotkeyAutoRearm = 2
    }

    /// <summary>
    /// Which pair of adjacent selector positions a PRND lockout sits between. Label-relative,
    /// so mirroring the lane moves the lockout with P, R, N and D - the same rule that keeps
    /// each position's button on its label.
    /// </summary>
    public enum PrndLockoutGap
    {
        Off = 0,

        /// <summary>Between P and R - the out-of-park gate.</summary>
        PR = 1,

        /// <summary>Between R and N - the reverse guard.</summary>
        RN = 2,

        /// <summary>Between N and D.</summary>
        ND = 3
    }

    /// <summary>Which way along the lane a PRND lockout charges, named by the labels.</summary>
    public enum PrndLockoutDirection
    {
        /// <summary>Moving toward D's end of the lane pays.</summary>
        TowardD = 0,

        /// <summary>Moving toward P's end of the lane pays.</summary>
        TowardP = 1,

        /// <summary>Both ways pay.</summary>
        Both = 2
    }

    /// <summary>Gate columns, left to right. The last column of the pattern holds reverse.</summary>
    public enum Column
    {
        None = -1,
        C1 = 0,
        C2 = 1,
        C3 = 2,
        C4 = 3
    }

    /// <summary>Which end of a column a gear sits at. Fwd is stick-away (low Y).</summary>
    public enum ShiftDir
    {
        None = 0,
        Fwd = 1,
        Back = 2
    }

    /// <summary>
    /// Shape of a divider's end where it meets the neutral tunnel - the mouth of a slot.
    /// </summary>
    public enum SlotMouthShape
    {
        /// <summary>A rectangular notch: the slot is the same width all the way up. The default.</summary>
        Square,

        /// <summary>Filleted on both flanks, so entering or leaving a slot is eased rather than cornered.</summary>
        Rounded,

        /// <summary>
        /// Chamfered on one flank only - the side the next sequential gear lies on - so withdrawing
        /// with a little lateral pressure is carried that way. A real gate's shift assist.
        /// </summary>
        Angled
    }

    public enum GateState
    {
        /// <summary>In the horizontal neutral channel, no gear selected.</summary>
        Neutral,

        /// <summary>Out of the channel, moving along a latched column, not yet deep enough.</summary>
        Traveling,

        /// <summary>Seated in a gear; the vJoy button is held.</summary>
        Engaged
    }

    public enum EnginePhase
    {
        Stopped,
        SearchDevice,
        OpenDevice,
        Run,
        Faulted
    }

    /// <summary>
    /// One DirectInput condition, in DirectInput units (positions and offsets ±10000,
    /// coefficients and saturations 0..10000). Compared by value so the effect layer can
    /// skip redundant device writes.
    /// </summary>
    public struct SpringPreset : IEquatable<SpringPreset>
    {
        public int Offset;
        public int PositiveCoefficient;
        public int NegativeCoefficient;
        public int PositiveSaturation;
        public int NegativeSaturation;
        public int DeadBand;

        public static readonly SpringPreset Off = new SpringPreset
        {
            Offset = 0,
            PositiveCoefficient = 0,
            NegativeCoefficient = 0,
            PositiveSaturation = 10000,
            NegativeSaturation = 10000,
            DeadBand = 0
        };

        /// <summary>Symmetric spring pulling toward <paramref name="offset"/>.</summary>
        public static SpringPreset Centering(int offset, int coefficient, int deadBand)
        {
            return new SpringPreset
            {
                Offset = offset,
                PositiveCoefficient = coefficient,
                NegativeCoefficient = coefficient,
                PositiveSaturation = 10000,
                NegativeSaturation = 10000,
                DeadBand = deadBand
            };
        }

        public bool Equals(SpringPreset other)
        {
            return Offset == other.Offset
                && PositiveCoefficient == other.PositiveCoefficient
                && NegativeCoefficient == other.NegativeCoefficient
                && PositiveSaturation == other.PositiveSaturation
                && NegativeSaturation == other.NegativeSaturation
                && DeadBand == other.DeadBand;
        }

        public override bool Equals(object obj)
        {
            return obj is SpringPreset && Equals((SpringPreset)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int h = Offset;
                h = (h * 397) ^ PositiveCoefficient;
                h = (h * 397) ^ NegativeCoefficient;
                h = (h * 397) ^ PositiveSaturation;
                h = (h * 397) ^ NegativeSaturation;
                h = (h * 397) ^ DeadBand;
                return h;
            }
        }
    }

    /// <summary>Everything the device layer needs for one tick.</summary>
    public struct ForceFrame
    {
        public SpringPreset SpringX;
        public SpringPreset SpringY;

        /// <summary>Lockout push, ±10000. Negative pushes toward -X (left, out of the 7/R column).</summary>
        public int ConstantX;

        /// <summary>Gate detent, ±10000. Positive pushes toward +Y (back).</summary>
        public int ConstantY;

        /// <summary>Anti-oscillation damping, 0..10000. Carried per frame so a gain change
        /// can be applied without recreating the effect.</summary>
        public int DamperCoefficient;
    }

    /// <summary>Result of one sequential state machine step.</summary>
    public struct SeqTransition
    {
        /// <summary>+1 fires an upshift this tick, -1 a downshift, 0 nothing.</summary>
        public int Shift;

        /// <summary>Whether the lever is back near centre and a new shift can fire.</summary>
        public bool Armed;

        /// <summary>The direction currently held past its threshold, for display.</summary>
        public ShiftDir Pushed;
    }

    /// <summary>Result of one state machine step.</summary>
    public struct StateTransition
    {
        public GateState State;
        public Column Column;
        public ShiftDir Direction;

        /// <summary>0 = neutral, 1..8 where 8 is reverse.</summary>
        public int Gear;

        public bool GearChanged;
        public int PreviousGear;
    }

    /// <summary>Immutable copy of engine state, safe to read from the UI and property delegates.</summary>
    public sealed class EngineSnapshot
    {
        public EnginePhase Phase = EnginePhase.Stopped;
        public bool DeviceConnected;
        public bool VJoyConnected;
        public int RawX = GateGeometry.AxisCenter;
        public int RawY = GateGeometry.AxisCenter;
        public int X = GateGeometry.AxisCenter;
        public int Y = GateGeometry.AxisCenter;
        public GateState State = GateState.Neutral;
        public Column Column = Column.None;
        public int Gear;
        public string GearLabel = "N";
        public double LoopHz;
        public string StatusMessage = "Stopped";
        public string DeviceName = "";

        /// <summary>
        /// Whether a hard-mode lockout is currently armed. True whenever no hard mode is
        /// configured - the gate is then never "released" - so dashboards can key on it alone.
        /// </summary>
        public bool LockoutEngaged = true;
    }
}
