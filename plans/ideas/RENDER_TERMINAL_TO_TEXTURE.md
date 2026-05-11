# problem

this repo is purrTTY, a KSA (kitten space agency) game mod which provides an ImGui based frontend ontop of a custom terminal emulator.

this is all working perfectly fine in this repo right now

what I want to create is the ability to render an ImGui window to an off-screen target/texture and then render that texture preferably to an in-game 3d surface, or, just projected in 3d space if possible.

i am led to believe this should be possible from developers of the game

note that the ImGui in-game is a 1:1 with real ImGui, but uses a custom C# binding so the syntax varies slightly.  You can use the imgui skill for vanilla C based ImGui references, but you will need to look at the mods existing code to see how the in-game "BRUTAL ImGui" syntax looks

Also the entire KSA game source code which is C# based is decompiled and available under `decomp/ksa` folder

The `decomp/ksa/Content/Core` folder holds non-DLL game assets like XML data files and fragment and vertex shaders, texture files, game audio, etc.

The game considers all content to be a form of "mod" and "Core" is the built-in mod with all the game code.

The game is not based on any game engine, KSA itself is a bespoke game engine purpose built for the game to be a completely custom and bespoke space flight simulator game

It uses a "framework" called "BRUTAL" which is primarily a custom high-level language C# binding to Vulkan, which is the underlying rendering technology used exclusively.  The BRUTAL decompiled sources are included under `decomp/ksa` as well.

# goals

do a very deep dive and thorough analysis of KSA/BRUTAL, looking into the BRUTAL Vulkan rendering and BRUTAL ImGui bindings in great details

determine how to take a given ImGui window and render it to an off-screen texture and then render that texture in-game preferably on a game object surface (e.g. on a SubPart mesh, if possible)

this can be done any way necessary that you can determine, including:

- preferably using game code and APIs directly from our mod
- using harmony if necessary for runtime patching/interception (avoid if possible)
- modifying or adding shaders (also avoid if possible, but if absolutely required, that is acceptable)

# instructions

make a detailed implementation plan with highly specific, unambiguous detail about the solution and each task should contain fine very detailed information, examples, code references, code sampples etc such that future coding agents can be provided the task largely in isolation and have everything it needs to unambiguously implement the task.  this may include references back to decompiled sources etc if it would be necessary for the task to have fine details of it.

# plan