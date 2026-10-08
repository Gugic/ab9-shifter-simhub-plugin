# Troubleshooting

[Documentation](README.md) · [Setup guide](setup.md) · [Tuning reference](tuning.md)

**"No device with VID 346E / PID 1000 found"** — the base is off, in a different mode, or another
program has it. The message lists what was detected.

**"The stick is held exclusively by another program"** — MOZA Cockpit or a Pit House tuning page,
occasionally a game. Close them; the plugin retries automatically.

**"vJoy device 1 is owned by another program"** — the message names the owning process. Close it,
or pick a different device under **Options → Base and output → Device**, with **vJoy (direct)** selected, which shows every device
vJoy reports along with its button count and whether anything already holds it.

**The stick fights you everywhere, or drifts to the stops** — check polarity and background
centring. Under **Options → Polarity calibration**, use **Measure again**, then **Measure polarity**
to check whether the gate's forces are pushing the wrong way.
To isolate the plugin's forces, turn on **Release all forces (free stick)** on Main. In
**Moza AB9** mode, the **Base-driven effects** in Feel can still provide onboard resistance;
check **Base spring (%)** there. In generic mode, re-check that centring is off in the base's
own software.

**A wall buzzes, or kicks back like ABS** — see [docs/tuning.md](tuning.md). Short answer:
adjust *wall bite distance* first, then *wall attack*.

**A fast short-throw shift pulls into the wrong end-stop and reports neutral** — check the
loaded plugin build before changing the tune. Release 0.15.0 predates the tunnel-return fix
merged in [PR #48](https://github.com/Gugic/ab9-shifter-simhub-plugin/pull/48). Install a build
containing that fix and restart SimHub. Pulling newer source does not replace the installed DLL.
New traces include `build=` in the first comment line; it identifies the assembly loaded by
SimHub. Older traces need the version from **Options → About and acknowledgements**.
Retest a fast 5→6 shift while keeping the lever in the 5/6 column. The gear should hold and
rear end-stop contact should push toward neutral. A sideways move through the tunnel can
select another column, so include that motion when interpreting the requested gear.

**Everything feels dead, especially the lockout and the detents** — those are constant forces.
Confirm *Measure polarity* reported a result for both push axes rather than "barely moved", and
that overall gain is not near zero.

**Control Mapper sends nothing** — check its feature status in **Base and output**. Use
**Enable Control Mapper and restart SimHub** if disabled, then **Configure Control Mapper**
to configure its output and check that each assigned role still exists. An enabled mapper
without roles needs configuration. Press **Refresh roles** after editing its configuration. For keyboard output,
check the game's keyboard bindings and neutral behavior; for controller output, inspect its
vJoy or bridge device in `joy.cpl`.

**Direct vJoy gears do not register in the game** — check `joy.cpl`: the vJoy device should light button *i*
while gear *i* is held. If it does, the binding is the problem, not the plugin.
