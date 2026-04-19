# FEATURES

The base custom shell code which supports line editing and history via up/down is missing some features:

* ctrl+c shortcut should cancel/clear the current command buffer being edited
* ctrl+backspace shortcut should delete by "word" on the current command buffer
* cursor position for command buffer not implemented
    * left/right arrows should allow per-character moving of cursor
    * ctrl+left/right arrow should jump by word
    * home/end should jump to start or end of line
    * del should delete the next char (to the right of cursor), opposite of what backspace does by default

Note that the cursor position management may mean significantly more complex command buffer behavior and may require a re-architecture of the current implementation (I'm unsure about what is currently available in the design).

These features should be added to the base layer of the custom game shells that currently handles the command buffer and ctrl+l etc.


# NOT WORKING

* ctrl+backspace not working - this doesn't send a single char code like others do (ctrl+h works for this for now).  would need to check whole escape sequences, not just single normal bytes.
* ctrl+left, ctrl+right not working - same problem as ctrl+backspace?