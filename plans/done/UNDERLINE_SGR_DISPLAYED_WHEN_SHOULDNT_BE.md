when nvim first launches it renders most of the content with a underline and this is not correct.

when i page up/down to get the screen to repaint everything fixes itself, so it's only an issue during the initial nvim full screen TUI painting.

somehow this must be related to a SGR command missed, or some kind of SGR convention not resetting a styles when it was expected to.

analyze SGR underlying related handling and ensure they are all ECMA/vt100/xterm compliant behaviors, look for any kind of stale state bugs etc that might arise.

# notes from gemini 3 pro

> The user reported that the previous fix (handling erased cells correctly) did not > solve the issue. They provided a log (seqtrace.mylog) and a screenshot showing double > underlines under all text.
> 
> The trace log snippet ul-style:Double->None indicated a transition from Double > Underline to None, which happens during SGR resets, but the visual artifact > persisted. This strongly suggested that the application (nvim or starship) was > enabling Double Underline unintentionally.
> 
> I investigated 
> SgrMessageFactory.cs
>  and confirmed that SGR 21 was mapped to sgr.doubleUnderline. While standard ECMA-48 > defines SGR 21 as "Double Underline", it is historically and commonly used as > "Normal Intensity" (disable bold) by the Linux console and other terminals. > Applications like starship (used in the screenshot) often use SGR 21 to disable > bold, assuming the Linux console behavior.
> 
> If caTTY interprets 21 as Double Underline, an application trying to disable bold > will inadvertently enable double underlines for all subsequent text, exactly matching > the user's report.
> 
> I modified 
> caTTY.Core/Parsing/Sgr/SgrMessageFactory.cs
>  to map SGR 21 to sgr.normalIntensity instead of sgr.doubleUnderline. This aligns > behavior with the likely expectations of the running applications. Double Underline > functionality is preserved for applications that use the unambiguous SGR 4:2 > sequence, which caTTY already supports.
> 
> Changes
> Modified 
> caTTY.Core/Parsing/Sgr/SgrMessageFactory.cs
> :
> Changed case 21 to return sgr.normalIntensity instead of sgr.doubleUnderline.
> Added a comment explaining the historical context and the reason for the change > (compatibility with apps using 21 for "disable bold").
