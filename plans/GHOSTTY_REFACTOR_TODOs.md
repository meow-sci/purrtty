# known caveats

- The reader deliberately does not carry hyperlink/semantic-prompt/protected/kitty-graphics data. That's safe today because purrtty renders none of those. If you later add, say, OSC 8 hyperlink underlining or OSC 133 prompt shading, that data must come from GridRef.GetCell() (still fully populated) or by re-adding the specific read — it won't silently appear. I documented this in the code and CLAUDE.md.

