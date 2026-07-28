// Reference-only declaration of SimHub's logger entry point.
//
// Current is typed as log4net.ILog by the real assembly, so calls to it emit references to
// log4net's strong-named identity - which is why log4net comes from NuGet pinned to 2.0.15,
// the version SimHub ships, rather than being stubbed as well.

namespace SimHub
{
    public class Logging
    {
        public static log4net.ILog Current { get { return null; } }
    }
}
