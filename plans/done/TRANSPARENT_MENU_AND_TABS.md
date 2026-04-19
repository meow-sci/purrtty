Right now we have a feature which will "hide" the window menu and tab bar when enabled.

To do this, we're just doing something like not rendering them and then leaving the space blank or using a placeholder element of the same height.

The issue I have is that because we use the theme background for the whole window, the space where the tab bar is still has a bg color so it looks weird.

Attempt to fix this by changing it so that instead of the theme bg color being the entire window bg color, it is applied to the terminal content/canvas area only.  I DO NOT want this to paint per cell, it still should be a single contiguous bg color painted over that whole terminal area.