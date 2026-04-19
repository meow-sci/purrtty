The imgui drawlist optimizations now has aggressive caching that reuses texture data if no state changes.

This works generally, but if the window is dragged/moved and no terminal state changes, it causes rendering artifacts because the content stays painting in the SAME spot in the game coordinates but the window moves.

So the state needs invalidation if the ImGui window position moves.
