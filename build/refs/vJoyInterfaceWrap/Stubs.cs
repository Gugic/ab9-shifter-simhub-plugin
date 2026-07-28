// Reference-only declaration of the vJoy wrapper. The real one is a mixed-mode C++/CLI
// assembly wrapping a 32-bit native DLL, which a 64-bit build host cannot load at all - so
// this stub is what makes the plugin compilable off a gaming rig.

/// <summary>
/// Device state. Declared in the global namespace because that is where the real C++/CLI
/// assembly puts it, and the underlying type is uint for the same reason.
///
/// The numeric values matter: a switch over this enum compiles the constants into our IL, so
/// VJD_STAT_MISS has to be 3 here for the same reason it is 3 there.
/// </summary>
public enum VjdStat : uint
{
    VJD_STAT_OWN = 0,
    VJD_STAT_FREE = 1,
    VJD_STAT_BUSY = 2,
    VJD_STAT_MISS = 3,
    VJD_STAT_UNKN = 4,
}

namespace vJoyInterfaceWrap
{
    public class vJoy
    {
        public bool vJoyEnabled() { return false; }

        public VjdStat GetVJDStatus(uint rID) { return VjdStat.VJD_STAT_MISS; }

        public bool AcquireVJD(uint rID) { return false; }

        public void RelinquishVJD(uint rID) { }

        public bool ResetButtons(uint rID) { return false; }

        public bool SetBtn(bool Value, uint rID, uint nBtn) { return false; }

        public int GetVJDButtonNumber(uint rID) { return 0; }

        public int GetOwnerPid(uint rID) { return 0; }
    }
}
