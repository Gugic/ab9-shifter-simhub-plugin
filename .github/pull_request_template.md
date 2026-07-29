<!--
This description is squash-merged into main as the commit message, so write it as one: prose,
saying why rather than listing files. Delete any heading that does not apply.
-->

## What and why



## How it was verified

<!--
`dotnet test` is table stakes and CI already runs it. What matters here is the two things CI
cannot do:

- `powershell -File tools\Verify-StubBuild.ps1` on a machine with SimHub, if this touches how the
  plugin uses SimHub's API or its dependencies.
- The rig, if this touches how anything feels. Say what was tried and what it felt like. A feel
  change verified only by arithmetic is not verified - see docs/force-model.md for the list of
  approaches that were correct on paper and wrong in the hand.
-->



## Invariants

<!--
Delete this section unless the change touches one. If it does, say so plainly: the disclaimers,
the 10% polarity cap, the buttons-before-forces ordering, the shipped profiles shipping with
forces off, what a shared profile is allowed to carry. AGENTS.md has the full list, and each
entry is there because breaking it caused a bug on real hardware.
-->
