- focus options (animated cursors.. blinking box, blinking vertical line, blinking underline, window chrome options .. when hovered, when focused show a border.  have a border opacity setting in the focus settings area)
- add a terminal "lock mode" (make mouse clicks click-through on the main terminal canvas so all mouse clicks interact with the underlying game, not the terminal)
  - keep a focus hot zone option when lock is active configurable to a selectable corner or side (in the middle of each side), with a configurable width/height and color with opacity options when the hot zone is hovered, when clicked, focus the terminal.

think hard about adding these cursor and focus features, the settings for them should be saved as paart of the custom named themes when saved

implement them expertly with a goal of a good user experience for the player utilizing them for high degree of terminal customizability and behaviors to avoid it being too intrusive to the game behavior.

for example with the lock mode, when locked, mouse clicks should pass through the terminal when the terminal is not focused, and if a hot zone is enabled, and the user clicks it, focus the terminal and mouse clicks on the canvas should then work as they do now on the main canvas

I THINK (check on this) we have an invisible button over the terminal canvas for mouse clicks, if ths is true, the lock feature and click-through shouldn't be too hard I think?  without the button the clicking on the canvas would do ImGui window moves if i recall, so that will have to be addressed to avoid these imgui click registrations on the window at all in this new feature

use the ksa, imgui skills as needed
