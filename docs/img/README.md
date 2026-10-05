# Documentation screenshots

The plugin screenshots were captured from the running SimHub application on 2026-10-04,
using SimHub 9.12.9 and a local `0.14.1-preview` build of commit `0557b36`. They show the
merged Main/Options layout and Control Mapper output introduced after v0.14.0.

The AB9 reported firmware 1.1.5.2. Its plugin settings and SimHub's rolling plugin backups
were backed up and cleared with the owner's permission before capture. **Prepare base**,
all four polarity probes at 10%, and **Finish setup** completed on the real base. The master
switch stayed off after calibration. The screenshots are actual UI captures, cropped to the
relevant panel or dialog; labels, values and connection states have not been retouched.

| Image | State shown |
| --- | --- |
| `setup-ab9.png` | First-run Moza AB9 mode, before Prepare base, with direct vJoy selected |
| `setup-generic.png` | First-run Generic FFB stick manual checklist and output selection |
| `setup-polarity.png` | Unmeasured polarity, default probe force, Finish setup disabled |
| `setup-complete.png` | Successful measurement and Finish setup available |
| `main.png` | Completed setup, H-pattern profile, master off |
| `options.png` | Rig controls after setup, with groups collapsed |
| `geometry.png` | Geometry dialog with its monitor above the scroller |
| `feel.png` | Moza AB9 Base-driven effects and shared profile percentages |
| `effects.png` | Native SimHub effect rows and Lever routing |
| `output-control-mapper.png` | Loaded Control Mapper feature, before assigning gear roles |
| `pit-house-mode.png` | Retained earlier Pit House capture for the manual fallback |

Values and device numbers are examples, not starting-strength recommendations. In particular,
the profile's overall gain can be higher than a new user's first test should use. Control
Mapper was inspected with the master off; the capture does not verify external game bindings.

To refresh these images, build current main against the installed SimHub, use the real app's
theme, and capture each matching state. For a genuine first-run capture, back up settings and
get authorization before clearing them; use the [fresh-start procedure](../../DEVELOPMENT.md#deploy-to-simhub) while
SimHub is stopped, including its rolling backups. **Review setup** is useful for reviewing an
existing rig, but it does not reproduce unmeasured first-run state. Keep force output off for
documentation captures except for an explicitly authorized polarity measurement. Record the
build and any hardware steps actually verified here, and match the prose to the captured UI.
