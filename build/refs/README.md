# Reference stubs

The plugin compiles against nine assemblies that ship inside SimHub's install folder. A build
machine that has no SimHub — a CI runner, a fresh clone — cannot resolve them, and SimHub's
assemblies are not ours to redistribute.

These four projects declare **only the API surface this plugin actually uses**, so the compiler
has something to bind against. They contain no logic; every method body is a stub that is never
executed. At runtime the real SimHub assemblies are the ones loaded.

| Stub | Replaces | Why it is safe |
| --- | --- | --- |
| `SimHub.Plugins` | SimHub's plugin SDK, the `SHxxx` WPF controls and `TitledSlider` | Not strong-named, so the CLR binds it by simple name and ignores the version |
| `GameReaderCommon` | `GameData` / `StatusDataBase` telemetry | Not strong-named |
| `SimHub.Logging` | `SimHub.Logging.Current` | Not strong-named; its `log4net` dependency comes from NuGet at the exact version SimHub ships (2.0.15) |
| `vJoyInterfaceWrap` | The vJoy wrapper | Not strong-named. The real one is a mixed-mode x86 assembly that a 64-bit build host cannot even load |

`SharpDX`, `SharpDX.DirectInput` and `log4net` are **not** stubbed — they are public NuGet
packages, referenced at the exact versions SimHub ships (4.2.0, 4.2.0, 2.0.15) so the assembly
identities the compiler writes into our DLL match the ones SimHub loads.

## The rule that makes this work

Every signature here must match the real assembly **exactly** — parameter types, return types,
property-versus-field, and enum values. The compiler bakes those into our IL: a field read is a
different instruction from a property call, and `case VjdStat.VJD_STAT_MISS:` compiles to the
literal `3`. A stub that merely *looks* right but declares a property where SimHub has a field
produces a DLL that builds cleanly and then throws `MissingFieldException` on the rig.

The signatures here were taken by reflecting over the real assemblies rather than by reading
documentation. To prove a stub-built DLL still binds, build with the stubs and run:

```powershell
powershell -File tools\Verify-StubBuild.ps1
```

It loads that DLL with the real SimHub assemblies on the resolve path and asks the JIT to
prepare every method, which forces the runtime to resolve every external type, method and field
without executing any of it. Anything a stub got wrong fails there instead of on the rig. It
needs a local SimHub install, so it is a local check, not a CI one — run it before tagging a
release.

## Which reference set a build uses

`AB9ActiveShifter.csproj` decides automatically: if `$(SimHubDir)SimHub.Plugins.dll` exists it
uses the real assemblies, otherwise it falls back to these stubs. Force either way with
`-p:UseSimHubStubs=true` or `=false`.

A local build on a machine with SimHub installed therefore never touches these stubs, which is
what you want — the local build is the one that proves the real API still matches.
