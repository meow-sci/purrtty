# Modding — extending and hosting kFS from a mod

What a StarMap/Harmony mod can add to kFS, how to do it safely, and how to run and test the result
outside the game. Start with the extension-point map; the service recipe and the offline harness
are the two documents backed by executed experiments.

## Start here

- [kFS extension-point map](extension-points.md) — Every way a mod can extend or hook kFS, ranked by cost and risk, with the mechanism, the evidence level and the document that has the detail.
- [Risks and compatibility rules for kFS mods](risks-and-compatibility.md) — The invariants a kFS-extending mod must keep, the ways it can break the player's game or programs, how each failure shows up, and what to re-check when the game updates.

## Recipes

- [Adding a service to kFS from a mod](adding-a-service.md) — The complete, tested recipe for a mod-defined service: table row and validation rules, kC header and builtin registration, the two host hooks, threading rules and constraints.
- [Running kFS offline — the test harness and what it proved](offline-harness.md) — How to load KSA.dll outside the game and drive the real tables, compiler, VM, host and Workbench, with the complete spike program and the results of each experiment.

## Tooling

- [What kFS exposes for a language server](language-tooling-surface.md) — The compiler, workbench and table APIs an LSP for kC and kASM can be built on, feature by feature, with threading and caching constraints.

## Per-area extension detail (in the area directories)

- [Toolchain extension points](/compiler/toolchain-extension-points.md) — compiler, assembler and image side.
- [Host extension surface](/host/host-extension-surface.md) — host computer, messages and layout side.
- [Patch targets and seams in the kFS game glue](/integration/patch-targets-and-seams.md) — Harmony targets with signatures, threads and patchability concerns.
- [Tooling extension points](/tooling/tooling-extension-points.md) — workbench, console, editor and reference window.

## The game systems the parts extension drives

- [Control recipes for lights and part animations](/game-targets/control-recipes.md) — C# for each read and write, with required thread and side effects.
- [Part and module model](/game-targets/part-and-module-model.md), [LightModule](/game-targets/light-module.md), [KeyframeAnimationModule](/game-targets/keyframe-animation-module.md), [stock control paths](/game-targets/stock-control-paths.md).

## Related outside this bundle

- `plans/KFS_EXTENSION_FOR_PARTS_PLAN.md` — the implementation plan built on these documents.
- `plans/investigations/kfs-spikes/` — the spike programs behind the offline-harness results.
