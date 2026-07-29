# Notices and attribution

## This project

AB9 Active Shifter is licensed under the MIT License (see `LICENSE`).

## No affiliation

This is an independent, unofficial project. It is **not** affiliated with, endorsed by, sponsored
by or supported by MOZA Racing, by Wotever (SimHub), or by the vJoy project. None of them have
reviewed it, and a problem caused by this plugin is not theirs to answer for.

MOZA, MOZA Racing, MOZA Pit House, MOZA Cockpit, AB9, SimHub and vJoy are the trademarks or
product names of their respective owners. They appear here only descriptively — to identify which
hardware and which host application this plugin works with — which is the whole of their use and
implies no endorsement or association.

## No warranty, and the risk that comes with the hardware

The MIT License above disclaims all warranties and all liability, in the two paragraphs beginning
`THE SOFTWARE IS PROVIDED "AS IS"`. That is not boilerplate here, so it is worth restating in
plain words:

This software commands an active force feedback device capable of roughly 12 Nm. It renders forces
from a software loop, and forces rendered that way can become unstable and oscillate. A defect, an
unfortunate combination of settings, or a stalled loop can make the base shake, kick, or drive to
its stops without warning, which can cause **physical injury and damage to hardware**.

You choose to run it, and you accept that risk. The authors and copyright holders accept no
responsibility or liability for injury, or for damage to your equipment or anything attached to
it. Bounds on output — forces off by default, a 10% cap until polarity has been measured, a
watchdog that stops output if the loop stalls — reduce the risk; they do not remove it and are not
a guarantee.

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

The same is true of the third-party libraries SimHub ships and this plugin uses
— SharpDX (MIT), log4net (Apache-2.0) and
[Newtonsoft.Json](https://www.newtonsoft.com/json) (MIT), the last of which
serialises exported profiles. None are redistributed here: they are compiled
against at the versions SimHub ships and loaded from SimHub's own copies.

## vJoy

Virtual joystick output uses [vJoy](https://sourceforge.net/projects/vjoystick/)
via the `vJoyInterfaceWrap` managed wrapper shipped with SimHub. vJoy is not
redistributed here.
