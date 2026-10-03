using System;
using System.Collections.Generic;

namespace AB9ActiveShifter.Core
{
    public enum GearOutputMode
    {
        VJoy = 0,
        ControlMapper = 1
    }

    /// <summary>
    /// The output mapping belongs to the rig, like its vJoy id. Indexes retain the existing
    /// button meanings; index zero adds an optional H-neutral role for keyboard bindings.
    /// Blank roles deliberately send nothing. This is configuration only, with no SimHub I/O.
    /// </summary>
    public static class GearOutputConfig
    {
        public const int RoleCount = 15;

        public static string Label(int button)
        {
            if (button == 0) return "H-pattern neutral (optional)";
            if (button >= 1 && button <= 7) return "Gear " + button;
            switch (button)
            {
                case 8: return "Reverse";
                case 9: return "Sequential up";
                case 10: return "Sequential down";
                case 11: return "PRND: P";
                case 12: return "PRND: R";
                case 13: return "PRND: N";
                case 14: return "PRND: D";
                default: return "";
            }
        }

        public static string[] CopyRoles(string[] roles)
        {
            var copy = new string[RoleCount];
            for (int i = 0; i < copy.Length; i++)
                copy[i] = roles != null && i < roles.Length ? roles[i] ?? "" : "";
            return copy;
        }

        public static bool RolesEqual(string[] a, string[] b)
        {
            for (int i = 0; i < RoleCount; i++)
            {
                string left = a != null && i < a.Length ? a[i] ?? "" : "";
                string right = b != null && i < b.Length ? b[i] ?? "" : "";
                if (!string.Equals(left, right, StringComparison.Ordinal)) return false;
            }
            return true;
        }

        public static bool UsesButton(GatePattern pattern, int button)
        {
            if (pattern == GatePattern.Sequential) return button == 9 || button == 10;
            if (pattern == GatePattern.Prnd) return button >= 11 && button <= 14;
            if (button == 0) return true;
            if (button == 8) return pattern != GatePattern.H6;
            int gears = pattern == GatePattern.H5R ? 5 : pattern == GatePattern.H7R ? 7 : 6;
            return button >= 1 && button <= gears;
        }

        public static string[] RolesForPattern(string[] roles, GatePattern pattern)
        {
            string[] copy = CopyRoles(roles);
            for (int i = 0; i < copy.Length; i++)
                if (!UsesButton(pattern, i)) copy[i] = "";
            return copy;
        }

        public static bool OutputChanged(EngineConfig before, EngineConfig after)
        {
            if (before == null || after == null) return true;
            if (before.OutputMode != after.OutputMode) return true;
            if (after.OutputMode == GearOutputMode.VJoy)
                return before.VJoyDeviceId != after.VJoyDeviceId;
            return !RolesEqual(before.ControlMapperRoles, after.ControlMapperRoles)
                   || before.Pattern != after.Pattern;
        }

        public static string ControlMapperProblem(bool isAvailable, string[] roles, ICollection<string> available)
        {
            if (!isAvailable)
                return "Control Mapper is not loaded. Enable it in Base and output, or in SimHub's Add/remove features, then restart SimHub.";
            if (available == null || available.Count == 0)
                return "Control Mapper is enabled, but no roles exist yet. Open Configure Control Mapper, choose Assign roles, and add keyboard, controller or SimHub control roles. Then refresh roles here. "
                    + MappingProblem(roles, available);
            return MappingProblem(roles, available);
        }

        public static string MappingProblem(string[] roles, ICollection<string> available)
        {
            bool assigned = false;
            for (int i = 0; i < RoleCount; i++)
            {
                string role = roles != null && i < roles.Length ? roles[i] : null;
                if (string.IsNullOrEmpty(role)) continue;
                assigned = true;
                if (available == null || !available.Contains(role))
                    return Label(i) + ": Control Mapper role '" + role + "' is unavailable. Configure it in Control Mapper, then refresh.";
            }
            return assigned ? null : "Choose at least one Control Mapper role below. Blank mappings send nothing.";
        }
    }
}
