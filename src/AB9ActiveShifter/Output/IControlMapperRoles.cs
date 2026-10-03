using System.Collections.Generic;

namespace AB9ActiveShifter.Output
{
    /// <summary>The role API, isolated so held-state and release behavior can be tested without I/O.</summary>
    public interface IControlMapperRoles
    {
        ICollection<string> GetRoles();
        bool StartRole(string role);
        bool StopRole(string role);
    }
}
