using System;
using System.Collections.Generic;
using AB9ActiveShifter.Core;

namespace AB9ActiveShifter.Output
{
    /// <summary>
    /// Publishes held roles, not one-shot actions: H gears and PRND stay down until released,
    /// and the engine still times sequential presses. Only our own roles are released. A shared
    /// role stays down until its last mapped button lifts. ReleaseAll also covers the optional
    /// neutral role, and works after a failed write so the watchdog can still attempt cleanup.
    /// </summary>
    public sealed class ControlMapperGearOutput : IGearOutput
    {
        private readonly IControlMapperRoles _mapper;
        private readonly string[] _roles;
        private readonly bool _holdNeutral;
        private readonly bool[] _down = new bool[GearOutputConfig.RoleCount];
        private readonly object _sync = new object();
        private int _heldButton = -1;
        private bool _connected;

        public ControlMapperGearOutput(IControlMapperRoles mapper, string[] roles, bool holdNeutral)
        {
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _roles = GearOutputConfig.CopyRoles(roles);
            _holdNeutral = holdNeutral;
        }

        public bool IsConnected { get { return _connected; } }
        public string LastError { get; private set; }

        public bool CheckConnection()
        {
            lock (_sync)
            {
                if (!_connected) return false;
                try
                {
                    if (!_mapper.IsAvailable) Fail("Control Mapper is no longer loaded.");
                }
                catch (Exception ex) { Fail(ex.Message); }
                return _connected;
            }
        }

        public bool Connect()
        {
            lock (_sync)
            {
                try
                {
                    bool available = _mapper.IsAvailable;
                    LastError = GearOutputConfig.ControlMapperProblem(available, _roles,
                        available ? _mapper.GetRoles() : null);
                    if (LastError != null) { _connected = false; return false; }
                    if (!ClearRoles()) return false;
                    _heldButton = -1;
                    _connected = true;
                    return true;
                }
                catch (Exception ex) { Fail(ex.Message); return false; }
            }
        }

        public void SetGear(int gear)
        {
            lock (_sync)
            {
                if (!_connected || gear == _heldButton) return;
                if (_heldButton >= 0 && !ChangeButton(_heldButton, false)) return;
                _heldButton = -1;
                if (gear >= 0 && gear < _down.Length && (gear != 0 || _holdNeutral))
                    if (!ChangeButton(gear, true)) return;
                _heldButton = gear;
            }
        }

        public void SetButton(int button, bool down)
        {
            lock (_sync)
            {
                if (!_connected || button < 1 || button >= _down.Length) return;
                ChangeButton(button, down);
            }
        }

        private bool ChangeButton(int button, bool down)
        {
            if (_down[button] == down) return true;
            string role = _roles[button];
            if (string.IsNullOrEmpty(role)) return true;

            // Different logical buttons may intentionally share a role. One release must not
            // cancel the other press, including overlapping sequential pulses.
            for (int i = 0; i < _down.Length; i++)
            {
                if (i != button && _down[i] && string.Equals(_roles[i], role, StringComparison.Ordinal))
                {
                    _down[button] = down;
                    return true;
                }
            }
            try
            {
                // A throwing press may have reached SimHub before throwing. Remember the
                // attempted press so cleanup still tries to release it on this same owner.
                if (down) _down[button] = true;
                if (!(down ? _mapper.StartRole(role) : _mapper.StopRole(role)))
                {
                    Fail("Could not " + (down ? "press" : "release") + " Control Mapper role '" + role + "'. Check Control Mapper is enabled and the role exists.");
                    return false;
                }
                _down[button] = down;
                return true;
            }
            catch (Exception ex) { Fail(ex.Message); return false; }
        }

        private void Fail(string problem)
        {
            LastError = problem;
            _connected = false;
        }

        private bool ClearRoles()
        {
            bool cleared = true;
            for (int i = 0; i < _down.Length; i++)
                if (_down[i] && !ChangeButton(i, false)) cleared = false;
            if (cleared) _heldButton = -1;
            return cleared;
        }

        public void ReleaseAll() { lock (_sync) { ClearRoles(); } }
        public void Disconnect() { lock (_sync) { ClearRoles(); _connected = false; } }
    }
}
