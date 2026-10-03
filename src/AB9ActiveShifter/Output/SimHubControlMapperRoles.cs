using System;
using System.Collections.Generic;
using SimHub.Plugins;

namespace AB9ActiveShifter.Output
{
    /// <summary>
    /// Uses SimHub's public role interface. Keep ONE interface for the lifetime of an output:
    /// it owns its presses, so a newly created interface cannot release an older one's roles.
    /// No controller is acquired here; Control Mapper owns the user's configured output.
    /// </summary>
    public sealed class SimHubControlMapperRoles : IControlMapperRoles
    {
        private readonly PluginManager _manager;
        private ControlMapperInterface _mapper;

        public SimHubControlMapperRoles(PluginManager manager)
        {
            if (manager == null) throw new ArgumentNullException(nameof(manager));
            _manager = manager;
        }

        private ControlMapperInterface Mapper
        {
            get { return _mapper ?? (_mapper = _manager.GetControlMapperInterface()); }
        }

        public ICollection<string> GetRoles()
        {
            var roles = new HashSet<string>(StringComparer.Ordinal);
            ControlMapperInterface mapper = Mapper;
            if (mapper == null) return roles;
            Add(roles, mapper.GetAvailableButtonRoles());
            Add(roles, mapper.GetAvailableKeyboardSimulatedKeysRoles());
            Add(roles, mapper.GetAvailableSimHubControlRoles());
            return roles;
        }

        private static void Add(HashSet<string> target, List<string> source)
        {
            if (source == null) return;
            foreach (string role in source)
                if (!string.IsNullOrEmpty(role)) target.Add(role);
        }

        public bool StartRole(string role) { return Mapper != null && Mapper.StartRole(role); }
        public bool StopRole(string role) { return Mapper != null && Mapper.StopRole(role); }
    }
}
