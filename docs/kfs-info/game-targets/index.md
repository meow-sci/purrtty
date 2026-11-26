# Game targets

Non-kFS game systems the kFS extension must drive: the part and module model, part lights and part
animations, the ways the stock game and existing mods control them, and ready-to-adapt recipes.
Everything here was read from the decompiled assemblies and content XML of KSA build
`2026.11.25.5949`, plus two mod code bases as precedent; nothing was verified in the running game.

## Object model

- [Vehicle, part tree, parts, sub-parts and modules](part-and-module-model.md) — How a KSA vehicle is composed of a PartTree of Part objects carrying typed modules, how to enumerate and identify them, how ModuleStateful state buffers work, and what staging, docking, save/load and scene changes do to those objects.

## The systems to drive

- [LightModule — part lights, their switch, and how they reach the renderer](light-module.md) — Complete reference for KSA part lights - LightModule and its shared TemplateData, the PowerConsumer light switch that provides on/off, colour, intensity, range and cone angles with units and limits, the per-frame render path, the XML schema with real examples, the stock parts that carry lights, and persistence.
- [KeyframeAnimationModule — deploy and actuation animations](keyframe-animation-module.md) — Complete reference for KSA part animations - one module class whose TimeGoal field is slewed toward by the physics step, how the ShowDeployRetract flag separates deploy/retract parts from 0-to-1 actuated parts, the animation data, the solar-tracking extension, side effects on colliders, solar power and on-rails state, the XML schema with real examples, stock parts, and persistence.

## How they are controlled today

- [Stock control paths for lights and part animations](stock-control-paths.md) — Every way the unmodded game changes light and animation state, traced end to end with file and line and the thread each step runs on, ending with which path a mod should reuse for each operation and whether it may run inside the physics step.
- [zippo light control — how an existing mod enumerates and mutates KSA lights](zippo-light-control-reference.md) — A code-level account of unscience's zippo light mod - its two mutation paths (shared-template reflection writes and per-instance template replacement), the exact game members it touches, where it runs, how it identifies parts, how it persists and restores changes, and every pitfall its sources and documents record.
- [gatOS and other unscience submods — part, module, light and animation precedents](gatos-and-other-mod-precedents.md) — How gatOS and the non-zippo unscience submods enumerate vehicle parts and modules, address them over time, schedule reads and writes relative to the physics workers, and control lights and keyframe animations, with the pitfalls their own documents record.

## Putting it to use

- [Control recipes for lights and part animations](control-recipes.md) — Concrete C# for enumerating a vehicle's lights and animation modules, reading their state, switching lights, changing colour, cone angles and brightness, toggling deployment and setting an actuation value, each labelled with its provenance, required thread and stock side effects.
