the old purrtty terminal before libghosty conversion had a bunch of UI/UX features like multiple tabs, serializable settings that let you change fonts, themes, font sizes, opacity of fg, bg, cell bg colors and restored terminal size and position

I DO NOT want to restore these verbatim, but i want to reference how these were achieved in ImGui for a new set of features

a copy of the purrtty repo before switching to libghostty is at /Users/asherwin/repos/meow-sci/purrtty_pre-libghostty which is available for reference

We have a system currently in place that inserts top-level game menus, in two different ways:

- if the "ModMenu" companion mod is available, it uses that system to add game menus to play nice with mutiple mods
- if "ModMenu" is not available, purrtty registers its own game menus

The contents of the menus should be the same either way, just a difference of how they are registered to be accessed.

Right now the mod only shows a single terminal window with a hard-coded theme, and the old mod applied themes globally to all tabs.

Instead, I want to:

- allow multiple ImGui windows for terminal sessions
- allow tabs for multiple terminal sessions in one window (hide tab bar if only one)
- have user savable themes, the user should be able (using the top-level menus) save the currently focused windows current settings as a theme, show a dialog to input a name and persist it in TOML format (see the old mod on how we can serialize/deserialize TOML for this)
- be able to apply a saved theme to a given window from the game menus
- the themes should have all the same features as the old mod (all the built-in themes we have plus font size and opacity settings and font family choices)
- the game menus should be used to launch new sessions (in current window as a tab or new window)
- support launching WSL2, PowerShell or Cmd as we did before with smart detection if they are available
- restore the old features that basically hid all ImGui window chrome and edges and borders etc when the mouse was not hovering.  this includes hiding al lthat when focused but not mouse over, so that a user can focus is and move the mouse away and have a nice transparent bg with no borders etc for nice immersion