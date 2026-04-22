For the custom game shell implementation

* Ctrl+L should CONTINUE to clear the current screen but not erase the historical scrollback buffer
* The "clear" command MUST clear the scrollback buffer and clear the screen (so all data is gone, cursor back to 0,0)
