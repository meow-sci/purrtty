After resize action finishes, flip the ImGui window auto size to content flag on for one tick, then disable on next tick, to get auto sizing ImGui to terminal size exactly.

We don't want to leave the auto size content flag on permanently because we've observed it automatically constantly shrinking if it thinks its slightly smaller then the current row/col count allows.

When changing fonts or font sizes we also need to enable it for a tick to let it recalculate both the new row/col count (which already happens) and then autosize the window to fit the new sized content.