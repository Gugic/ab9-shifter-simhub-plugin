# Notices and attribution

## This project

AB9 Active Shifter is licensed under the MIT License (see `LICENSE`).

## FanaBridge (MIT)

Project scaffolding patterns — the SDK-style `net48` csproj layout, the
`$(SimHubDir)` HintPath reference scheme with `Private=false`, the
`Directory.Build.props` / `Directory.Build.targets` split with an opt-in
copy-to-SimHub install target, and the general shape of the plugin shell
(`IPlugin` / `IDataPlugin` / `IWPFSettingsV2` / `IReusable`, settings POCO
persisted through `ReadCommonSettings` / `SaveCommonSettings`, WPF settings
control constructed with the plugin instance) — follow
[kelchm/FanaBridge](https://github.com/kelchm/FanaBridge), which is
distributed under the MIT License, Copyright (c) 2026 kelchm.

## BonusFFB (GPL-3.0) — no code used

[kgmonteith/BonusFFB](https://github.com/kgmonteith/BonusFFB) is licensed
under the GNU General Public License v3.0. **No BonusFFB source code is
copied, translated, or derived into this project.** BonusFFB is a C++/Qt
application; this is an independently written C# implementation.

What was taken is factual and conceptual only, and is not protectable
expression:

- The general idea of simulating an H-pattern shift gate on a force-feedback
  joystick using DirectInput condition and constant-force effects.
- Publicly documented DirectInput constants and conventions (axis range
  `0..65535`, force magnitude range `±10000`), which come from the Microsoft
  DirectInput API, not from BonusFFB.
- Order-of-magnitude starting values for gate geometry, taken as a sanity
  reference and then re-derived for this plugin's four-column 7+R layout.
  These are re-tuned against the hardware and are user-adjustable settings.
- Hardware facts reported in the BonusFFB issue tracker and documentation,
  notably that some MOZA AB9 firmware revisions invert the direction of certain
  DirectInput effects, and that the base's own centering spring must be set to
  zero in MOZA Cockpit.
- Observations about *technique* made while reading its source for comparison,
  recorded in `docs/force-model.md`: that it renders gate structure with spring
  condition effects whose anchor is re-aimed past the target each update, and
  that it plays a short one-shot ramp-force effect as a detent click. These are
  descriptions of approach, discussed there alongside why this project chose
  differently; no expression of them was copied.
- The behavioural idea that a clutchless shift should grind and balk — observed
  as a user of its truck-shifter module, not taken from its source. This
  project's grind (telemetry conditions, balk-wall render, engagement refusal)
  was designed and implemented independently; see `docs/force-model.md`.

Its source was read for architectural comparison. The force model, state
machine, effect set, threading design, and all code in this repository were
written independently for this project, and the two implementations make
opposite core choices — BonusFFB puts the fast loop in the firmware via springs,
this plugin renders shaped constant forces from a software loop.

## SimHub

This plugin links against assemblies distributed with
[SimHub](https://www.simhubdash.com/) by Wotever. Those assemblies are not
redistributed here; they are referenced from a local SimHub installation at
build time and resolved from the SimHub process at runtime.

## vJoy

Virtual joystick output uses [vJoy](https://sourceforge.net/projects/vjoystick/)
via the `vJoyInterfaceWrap` managed wrapper shipped with SimHub. vJoy is not
redistributed here.
