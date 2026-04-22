hide window border and menu bar when not hovered

one of these ImGui commands might help

```csharp
ImGui.IsWindowHovered();
ImGui.IsAnyItemHovered();
```


what i want to have happen is that when the window has focus but the mouse is not hovered, we hide the menubar and window borders.  this will have a slicker appearance in game.

also, when there is NO FOCUS, we should always hide the menu bar and borders.

