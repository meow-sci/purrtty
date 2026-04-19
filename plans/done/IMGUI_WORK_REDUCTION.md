# ImGui Render Loop Work Reduction Plan

> **Created:** January 6, 2026  
> **Status:** Planning  
> **Goal:** Reduce call counts in render hot path by skipping unnecessary work

## Overview

This document outlines optimizations focused on **eliminating work entirely** rather than micro-optimizing existing code paths. Based on profiling data from `IMGUI_TIMING_RESULTS.md`, the render loop processes **618,840 cells per 60 frames** (10,314 cells/frame for an 80×129 viewport), but the vast majority of these cells are empty/default and require no actual rendering.

### Current Hot Path Timing (60 frames, detailed mode)

| Operation | Total Calls | Time (ms) | Notes |
|-----------|-------------|-----------|-------|
| `RenderCell` | 618,840 | 570.48 | Every cell processed |
| `ResolveColors` | 618,840 | 44.49 | 2 color lookups per cell |
| `ApplyAttributes` | 618,840 | 33.93 | Style processing per cell |
| `ApplyOpacity` | 618,840 | 53.65 | Opacity applied per cell |
| `SelectionCheck` | 618,840 | 31.70 | Selection test per cell |
| `DrawBackground` | 33,120 | 2.00 | Only non-default backgrounds |
| `FlushRun` (batched text) | 55,129 | 36.45 | Text rendering calls |

### Key Insight

The code already skips drawing characters for empty cells (`' '` or `'\0'`), but it still performs **all preprocessing** (color resolution, style application, opacity, selection check) for every cell. Most terminal content has large regions of empty/default cells where this work is wasted.

---

## Task 1: Early Exit for Default Empty Cells

### Goal
Skip all processing for cells that are empty spaces with default attributes and no selection. This is the highest-impact optimization, potentially eliminating 50-70% of all cell processing.

### Current Behavior

**File:** [caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs](caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs)

The cell rendering loop (lines 207-330) processes every cell regardless of content:

```csharp
for (int col = 0; col < colsToRender; col++)
{
    // Lines 214-218: Position calculation (always runs)
    float x = terminalDrawPos.X + (col * currentCharacterWidth);
    float y = terminalDrawPos.Y + (row * currentLineHeight);
    var pos = new float2(x, y);
    Cell cell = rowSpan[col];

    // Lines 221-233: Selection check (always runs)
    bool isSelected;
    if (currentSelection.IsEmpty)
        isSelected = false;
    else
        isSelected = currentSelection.Contains(row, col);

    // Lines 240-243: Color resolution (always runs) - EXPENSIVE
    float4 baseForeground = _colorResolver.Resolve(cell.Attributes.ForegroundColor, false);
    float4 baseBackground = _colorResolver.Resolve(cell.Attributes.BackgroundColor, true);

    // Lines 246: Style application (always runs)
    var (fgColor, bgColor) = _styleManager.ApplyAttributes(cell.Attributes, baseForeground, baseBackground);

    // Lines 249-252: Opacity application (always runs)
    fgColor = OpacityManager.ApplyForegroundOpacity(fgColor);
    bgColor = OpacityManager.ApplyCellBackgroundOpacity(bgColor);

    // ... rest of rendering
    
    // Lines 283-285: Character skip (too late - already did all the work!)
    if (cell.Character != ' ' && cell.Character != '\0')
    {
        // Text batching logic
    }
}
```

### Problem

A cell with `Character == ' '` and `Attributes == SgrAttributes.Default` and not selected requires:
- Zero background drawing (no explicit background color)
- Zero text drawing (space character)
- Zero decoration rendering (no underline/strikethrough)

Yet we still perform 6+ function calls per such cell.

### Proposed Solution

Add an early-exit check at the **beginning** of the cell loop that skips all processing for "truly empty" cells.

### Implementation Steps

#### Step 1: Add Helper Method to Detect Default Cells

Add a new method to `SgrAttributes` or create an extension method:

**File:** [caTTY.Core/Types/SgrAttributes.cs](caTTY.Core/Types/SgrAttributes.cs)

Add after line 393 (after `public static SgrAttributes Default => new();`):

```csharp
/// <summary>
/// Returns true if these attributes are completely default (no styling that requires rendering).
/// Used for early-exit optimization in render loop.
/// </summary>
public bool IsDefault => 
    !Bold && 
    !Faint && 
    !Italic && 
    !Underline && 
    !Blink && 
    !Inverse && 
    !Hidden && 
    !Strikethrough && 
    !ForegroundColor.HasValue && 
    !BackgroundColor.HasValue &&
    !UnderlineColor.HasValue;
```

#### Step 2: Add Early Exit in Cell Loop

**File:** [caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs](caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs)

Modify the cell loop starting at line 207. Insert early-exit logic immediately after reading the cell:

**Before (current code at lines 207-237):**
```csharp
for (int col = 0; col < colsToRender; col++)
{
//  _perfWatch.Start("RenderCell");
    try
    {
//    _perfWatch.Start("RenderCell.Setup");
        float x = terminalDrawPos.X + (col * currentCharacterWidth);
        float y = terminalDrawPos.Y + (row * currentLineHeight);
        var pos = new float2(x, y);

        Cell cell = rowSpan[col];

        // Check if this cell is selected
//      _perfWatch.Start("RenderCell.SelectionCheck");
        bool isSelected;
        if (currentSelection.IsEmpty)
        {
            isSelected = false;
        }
        else
        {
//          _perfWatch.Start("RenderCell.SelectionCheck.Contains");
            isSelected = currentSelection.Contains(row, col);
//          _perfWatch.Stop("RenderCell.SelectionCheck.Contains");
        }
//      _perfWatch.Stop("RenderCell.SelectionCheck");
        // ... continues with color resolution
```

**After (with early exit):**
```csharp
for (int col = 0; col < colsToRender; col++)
{
//  _perfWatch.Start("RenderCell");
    try
    {
        Cell cell = rowSpan[col];
        
        // EARLY EXIT: Skip completely default empty cells
        // This is the most common case in typical terminal output
        bool isEmptyChar = cell.Character == ' ' || cell.Character == '\0';
        if (isEmptyChar && cell.Attributes.IsDefault)
        {
            // Check selection only for potentially skippable cells
            // (avoid Contains() call for most cells)
            bool needsSelectionCheck = !currentSelection.IsEmpty 
                && row >= currentSelection.StartRow 
                && row <= currentSelection.EndRow;
            
            if (!needsSelectionCheck || !currentSelection.Contains(row, col))
            {
                // Truly empty cell with no selection - skip all processing
                // Must still flush any pending text run
                FlushRun();
                continue;
            }
        }
        
        // Non-empty or styled cell - proceed with full processing
//      _perfWatch.Start("RenderCell.Setup");
        float x = terminalDrawPos.X + (col * currentCharacterWidth);
        float y = terminalDrawPos.Y + (row * currentLineHeight);
        var pos = new float2(x, y);

        // Selection check (already computed for early-exit cells, need for others)
//      _perfWatch.Start("RenderCell.SelectionCheck");
        bool isSelected;
        if (currentSelection.IsEmpty)
        {
            isSelected = false;
        }
        else
        {
            isSelected = currentSelection.Contains(row, col);
        }
//      _perfWatch.Stop("RenderCell.SelectionCheck");
        // ... rest of existing code unchanged
```

#### Step 3: Handle Run Flushing Correctly

The early exit calls `FlushRun()` to ensure any pending text run is rendered before skipping. This maintains correct rendering order. The existing `FlushRun()` already handles the case of `runLength == 0` with an early return.

### Testing

1. **Visual regression test:** Render terminal with mixed content (colored text, empty regions, selections) and verify identical output.
2. **Performance test:** Measure render time reduction using the existing `_perfWatch` infrastructure.
3. **Edge cases:**
   - Selection spanning empty cells (should still highlight)
   - Cells with background color but space character (should draw background)
   - Inverse attribute on space (should show as colored block)

### Expected Impact

| Metric | Before | After | Improvement |
|--------|--------|-------|-------------|
| Cells fully processed | 618,840 | ~200,000-250,000 | 60-70% reduction |
| `ResolveColors` calls | 618,840 | ~200,000-250,000 | 60-70% reduction |
| `ApplyAttributes` calls | 618,840 | ~200,000-250,000 | 60-70% reduction |
| `ApplyOpacity` calls | 618,840 | ~200,000-250,000 | 60-70% reduction |

---

## Task 2: Row-Level Selection Optimization

### Goal
Avoid calling `currentSelection.Contains(row, col)` for every cell. Instead, compute once per row whether the row could possibly intersect the selection, and skip the `Contains()` call entirely for rows outside the selection range.

### Current Behavior

**File:** [caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs](caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs)

Lines 221-233 check selection for every cell:

```csharp
bool isSelected;
if (currentSelection.IsEmpty)
{
    isSelected = false;
}
else
{
    isSelected = currentSelection.Contains(row, col);
}
```

**File:** [caTTY.Display/Types/TextSelection.cs](caTTY.Display/Types/TextSelection.cs)

The `TextSelection` struct likely has `StartRow`, `EndRow`, `StartCol`, `EndCol` properties and a `Contains(int row, int col)` method.

### Problem

Even with the `IsEmpty` fast path, every non-empty-selection frame calls `Contains()` for all 10,000+ cells. Selections typically cover a small region (a few lines of text being copied), but we check every cell.

### Proposed Solution

Pre-compute row-level selection bounds once per row, eliminating `Contains()` calls for rows entirely outside the selection.

### Implementation Steps

#### Step 1: Add Row-Overlap Check to TextSelection

**File:** [caTTY.Display/Types/TextSelection.cs](caTTY.Display/Types/TextSelection.cs)

Add a method to check if a row could possibly contain selected cells:

```csharp
/// <summary>
/// Returns true if the given row might contain selected cells.
/// This is a fast check that avoids per-cell Contains() calls for rows
/// entirely outside the selection range.
/// </summary>
[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
public bool RowMightBeSelected(int row)
{
    if (IsEmpty) return false;
    return row >= Math.Min(StartRow, EndRow) && row <= Math.Max(StartRow, EndRow);
}
```

#### Step 2: Cache Row Selection State in Render Loop

**File:** [caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs](caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs)

Add a per-row variable before the column loop (around line 133, inside the row loop):

```csharp
for (int row = 0; row < Math.Min(viewportRows.Count, activeSession.Terminal.Height); row++)
{
    var rowMemory = viewportRows[row];
    var rowSpan = rowMemory.Span;
    int colsToRender = Math.Min(rowSpan.Length, terminalWidthCells);

    // Pre-compute selection state for this row
    bool rowMightHaveSelection = currentSelection.RowMightBeSelected(row);

    int runStartCol = 0;
    int runLength = 0;
    // ... rest of row setup
```

#### Step 3: Use Cached State in Cell Loop

Replace the selection check (lines 221-233) with:

```csharp
// Check if this cell is selected
bool isSelected;
if (!rowMightHaveSelection)
{
    isSelected = false;
}
else
{
    isSelected = currentSelection.Contains(row, col);
}
```

This eliminates the `IsEmpty` check per cell (moved to once per row) and skips `Contains()` entirely for rows outside selection.

### Testing

1. **Selection test:** Create selection across multiple rows, verify highlighting works correctly.
2. **Edge cases:**
   - Selection from bottom to top (EndRow < StartRow)
   - Single-cell selection
   - Selection at first/last row of viewport
   - No selection (should never call `Contains()`)

### Expected Impact

| Scenario | Before `Contains()` calls | After | Improvement |
|----------|---------------------------|-------|-------------|
| No selection | 0 | 0 | Same |
| 3-row selection | 618,840 (all cells) | ~300 (3 rows × ~100 cols) | 99.95% reduction |
| Full-screen selection | 618,840 | 618,840 | Same (correct) |

---

## Task 3: Skip Processing for Empty Rows

### Goal
Skip entire rows that contain only default empty cells and have no selection overlap. This provides row-level early exit similar to Task 1's cell-level early exit.

### Current Behavior

**File:** [caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs](caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs)

The row loop (lines 128-331) always iterates through all columns for every row:

```csharp
for (int row = 0; row < Math.Min(viewportRows.Count, activeSession.Terminal.Height); row++)
{
    var rowMemory = viewportRows[row];
    var rowSpan = rowMemory.Span;
    int colsToRender = Math.Min(rowSpan.Length, terminalWidthCells);
    
    // ... setup run tracking variables ...
    
    for (int col = 0; col < colsToRender; col++)
    {
        // Process every cell
    }
}
```

### Problem

Terminal output is often clustered - command output appears in some rows while many rows remain empty (especially after `clear`, or at the bottom of a partially-filled screen). Empty rows still iterate through all columns.

### Proposed Solution

Add a row-level scan to detect if a row has any content worth rendering, and skip the entire column loop if not.

### Implementation Steps

#### Step 1: Add Row Content Detection

**File:** [caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs](caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs)

Add a helper method to the class:

```csharp
/// <summary>
/// Checks if a row has any content that requires rendering.
/// Returns true if any cell has a non-space character or non-default attributes.
/// </summary>
[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
private static bool RowHasContent(ReadOnlySpan<Cell> rowSpan)
{
    for (int i = 0; i < rowSpan.Length; i++)
    {
        ref readonly var cell = ref rowSpan[i];
        
        // Non-empty character?
        if (cell.Character != ' ' && cell.Character != '\0')
            return true;
        
        // Has explicit background color? (needs to draw background)
        if (cell.Attributes.BackgroundColor.HasValue)
            return true;
        
        // Has attributes that affect empty cells? (inverse makes spaces visible)
        if (cell.Attributes.Inverse)
            return true;
    }
    return false;
}
```

#### Step 2: Add Early Exit in Row Loop

Modify the row loop to check for empty rows:

```csharp
for (int row = 0; row < Math.Min(viewportRows.Count, activeSession.Terminal.Height); row++)
{
    var rowMemory = viewportRows[row];
    var rowSpan = rowMemory.Span;
    int colsToRender = Math.Min(rowSpan.Length, terminalWidthCells);

    // Pre-compute selection state for this row
    bool rowMightHaveSelection = currentSelection.RowMightBeSelected(row);
    
    // EARLY EXIT: Skip rows with no content and no selection
    if (!rowMightHaveSelection && !RowHasContent(rowSpan))
    {
        continue;
    }

    int runStartCol = 0;
    int runLength = 0;
    // ... rest of existing code
```

### Consideration: Row Scan Cost vs. Benefit

The `RowHasContent()` scan iterates through the row once. This is worthwhile because:
1. It's a simple loop with no function calls (just field reads)
2. If the row is empty, we skip the entire column loop with its many function calls
3. If the row has content, we've only added ~80-200 iterations of simple checks

For an 80-column row:
- Row scan cost: ~80 iterations of field reads
- Column loop cost (if not skipped): ~80 iterations × 6+ function calls each

The scan pays for itself if >10% of rows are empty.

### Testing

1. **Visual test:** Clear terminal, run partial output, verify rendering correct.
2. **Performance test:** Measure with mostly-empty screen vs. full screen.
3. **Edge cases:**
   - All rows empty (should skip all)
   - All rows full (should process all, scan overhead acceptable)
   - Selection on empty row (should still highlight)
   - Single character on otherwise empty row (should render)

### Expected Impact

| Screen State | Rows Processed Before | Rows Processed After | Improvement |
|--------------|----------------------|---------------------|-------------|
| Mostly empty (30% content) | 129 | ~40 | 70% reduction |
| Half full | 129 | ~65 | 50% reduction |
| Completely full | 129 | 129 | 0% (correct) |

---

## Task 4: Batch Background Rectangle Drawing

### Goal
Reduce `drawList.AddRectFilled()` calls by combining contiguous same-color background regions into single rectangles.

### Current Behavior

**File:** [caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs](caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs)

Background drawing (lines 258-281) draws one rectangle per cell with explicit background:

```csharp
if (isSelected)
{
    // Selection highlighting
    var selectionBg = new float4(0.3f, 0.5f, 0.8f, 0.7f);
    // ...
    var bgRect = new float2(x + currentCharacterWidth, y + currentLineHeight);
    drawList.AddRectFilled(pos, bgRect, ImGui.ColorConvertFloat4ToU32(bgColor));
}
else if (cell.Attributes.BackgroundColor.HasValue)
{
    var bgRect = new float2(x + currentCharacterWidth, y + currentLineHeight);
    drawList.AddRectFilled(pos, bgRect, ImGui.ColorConvertFloat4ToU32(bgColor));
}
```

### Problem

From timing: **33,120 `DrawBackground` calls** over 60 frames. Terminal content often has contiguous regions of same background color (e.g., a colored status bar, highlighted search results, selection).

Each `AddRectFilled()` call adds overhead:
- Function call to ImGui
- ImGui internally processes the rectangle

### Proposed Solution

Similar to how text is batched into runs, batch background rectangles into horizontal runs of same color.

### Implementation Steps

#### Step 1: Add Background Run Tracking Variables

Add these variables alongside the existing text run variables (around line 136):

```csharp
int runStartCol = 0;
int runLength = 0;
uint runColorU32 = 0;
ImFontPtr runFont = default;

// Background run tracking
int bgRunStartCol = -1;
int bgRunLength = 0;
uint bgRunColorU32 = 0;
```

#### Step 2: Add Background Run Flush Method

Add a local function inside `RenderTerminalContent()` similar to `FlushRun()`:

```csharp
void FlushBackgroundRun()
{
    if (bgRunLength <= 0)
        return;
    
    float bgX = terminalDrawPos.X + (bgRunStartCol * currentCharacterWidth);
    float bgY = terminalDrawPos.Y + (row * currentLineHeight);
    float bgWidth = bgRunLength * currentCharacterWidth;
    float bgHeight = currentLineHeight;
    
    drawList.AddRectFilled(
        new float2(bgX, bgY),
        new float2(bgX + bgWidth, bgY + bgHeight),
        bgRunColorU32
    );
    
    bgRunLength = 0;
    bgRunStartCol = -1;
}
```

#### Step 3: Replace Individual Background Draws with Batching

Replace the background drawing code (lines 258-281):

**Before:**
```csharp
if (isSelected)
{
    var selectionBg = new float4(0.3f, 0.5f, 0.8f, 0.7f);
    var selectionFg = new float4(1.0f, 1.0f, 1.0f, 1.0f);
    bgColor = OpacityManager.ApplyCellBackgroundOpacity(selectionBg);
    fgColor = OpacityManager.ApplyForegroundOpacity(selectionFg);
    foregroundColors[col] = fgColor;
    
    var bgRect = new float2(x + currentCharacterWidth, y + currentLineHeight);
    drawList.AddRectFilled(pos, bgRect, ImGui.ColorConvertFloat4ToU32(bgColor));
}
else if (cell.Attributes.BackgroundColor.HasValue)
{
    var bgRect = new float2(x + currentCharacterWidth, y + currentLineHeight);
    drawList.AddRectFilled(pos, bgRect, ImGui.ColorConvertFloat4ToU32(bgColor));
}
```

**After:**
```csharp
// Determine if this cell needs a background
uint cellBgColorU32 = 0;
bool needsBackground = false;

if (isSelected)
{
    var selectionBg = new float4(0.3f, 0.5f, 0.8f, 0.7f);
    var selectionFg = new float4(1.0f, 1.0f, 1.0f, 1.0f);
    bgColor = OpacityManager.ApplyCellBackgroundOpacity(selectionBg);
    fgColor = OpacityManager.ApplyForegroundOpacity(selectionFg);
    foregroundColors[col] = fgColor;
    
    cellBgColorU32 = ImGui.ColorConvertFloat4ToU32(bgColor);
    needsBackground = true;
}
else if (cell.Attributes.BackgroundColor.HasValue)
{
    cellBgColorU32 = ImGui.ColorConvertFloat4ToU32(bgColor);
    needsBackground = true;
}

// Batch background drawing
if (needsBackground)
{
    bool canExtendRun = bgRunLength > 0 
        && col == bgRunStartCol + bgRunLength 
        && cellBgColorU32 == bgRunColorU32;
    
    if (canExtendRun)
    {
        bgRunLength++;
    }
    else
    {
        FlushBackgroundRun();
        bgRunStartCol = col;
        bgRunLength = 1;
        bgRunColorU32 = cellBgColorU32;
    }
}
else
{
    FlushBackgroundRun();
}
```

#### Step 4: Flush Background Run at Row End

Add `FlushBackgroundRun()` call at the end of each row, before moving to the next row:

```csharp
        FlushRun();  // Existing text run flush
        FlushBackgroundRun();  // Add this
    }  // End of column loop
}  // End of row loop
```

### Testing

1. **Visual test:** Verify colored backgrounds render correctly (colored output, selections, inverse text).
2. **Selection test:** Select text across multiple cells, verify continuous highlight.
3. **Edge cases:**
   - Alternating background colors (should not batch)
   - Single-cell background (should render as single rect)
   - Full-row same-color background (should render as one rect)
   - Selection with varying original backgrounds (selection color should win)

### Expected Impact

| Scenario | Before `AddRectFilled` calls | After | Improvement |
|----------|------------------------------|-------|-------------|
| No backgrounds | 0 | 0 | Same |
| Scattered single cells | 1000 | 1000 | Same |
| Contiguous 10-cell regions | 1000 | 100 | 90% reduction |
| Full-row selection | 80 per row | 1 per row | 98.75% reduction |

---

## Task 5: Fused Color and Style Processing

### Goal
Replace multiple sequential function calls for color/style processing with a single fused call that computes the final `uint` color values directly.

### Current Behavior

**File:** [caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs](caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs)

Lines 240-252 perform 5 separate operations per cell:

```csharp
// 1. Resolve foreground color
float4 baseForeground = _colorResolver.Resolve(cell.Attributes.ForegroundColor, false);

// 2. Resolve background color
float4 baseBackground = _colorResolver.Resolve(cell.Attributes.BackgroundColor, true);

// 3. Apply SGR attributes (handles inverse, faint, etc.)
var (fgColor, bgColor) = _styleManager.ApplyAttributes(cell.Attributes, baseForeground, baseBackground);

// 4. Apply foreground opacity
fgColor = OpacityManager.ApplyForegroundOpacity(fgColor);

// 5. Apply background opacity
bgColor = OpacityManager.ApplyCellBackgroundOpacity(bgColor);
```

### Problem

Each function call has overhead (stack frame, parameter passing, potential cache miss). For 618,840 cells, this is 3,094,200 function calls just for color processing.

Additionally, the intermediate `float4` values are computed even when:
- Background is default (no drawing needed) - could skip bg resolution
- Cell is hidden attribute - could return transparent immediately
- Final result will be converted to `uint` anyway - could skip intermediate float4

### Proposed Solution

Create a fused method that handles all color processing in one call, with early exits for common cases.

### Implementation Steps

#### Step 1: Add Fused Resolution Method

**File:** [caTTY.Display/Rendering/CachedColorResolver.cs](caTTY.Display/Rendering/CachedColorResolver.cs)

Add a new method:

```csharp
/// <summary>
/// Resolves and processes cell colors in a single fused operation.
/// Returns final uint colors ready for ImGui draw calls.
/// 
/// This method combines:
/// - Color resolution (fg/bg)
/// - SGR attribute application (inverse, faint, bold color changes)
/// - Opacity application
/// - Float4 to uint32 conversion
/// 
/// Optimizations:
/// - Skips background resolution if not needed
/// - Early exit for hidden cells
/// - Direct uint output (no intermediate float4 storage needed by caller)
/// </summary>
/// <param name="attributes">Cell SGR attributes</param>
/// <param name="styleManager">Style manager for attribute application</param>
/// <param name="fgColorU32">Output: final foreground color as uint32</param>
/// <param name="bgColorU32">Output: final background color as uint32</param>
/// <param name="needsBackground">Output: true if background needs to be drawn</param>
[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
public void ResolveCellColors(
    in SgrAttributes attributes,
    StyleManager styleManager,
    out uint fgColorU32,
    out uint bgColorU32,
    out bool needsBackground)
{
    // Early exit: hidden cells are invisible
    if (attributes.Hidden)
    {
        fgColorU32 = 0; // Transparent
        bgColorU32 = 0;
        needsBackground = false;
        return;
    }
    
    // Resolve base colors
    float4 baseFg = Resolve(attributes.ForegroundColor, false);
    
    // Only resolve background if needed
    needsBackground = attributes.BackgroundColor.HasValue || attributes.Inverse;
    float4 baseBg = needsBackground 
        ? Resolve(attributes.BackgroundColor, true)
        : GetCachedDefaultBackground();
    
    // Apply SGR attributes
    var (fgColor, bgColor) = styleManager.ApplyAttributes(attributes, baseFg, baseBg);
    
    // Apply opacity
    fgColor = OpacityManager.ApplyForegroundOpacity(fgColor);
    
    if (needsBackground)
    {
        bgColor = OpacityManager.ApplyCellBackgroundOpacity(bgColor);
        bgColorU32 = ImGui.ColorConvertFloat4ToU32(bgColor);
    }
    else
    {
        bgColorU32 = 0;
    }
    
    fgColorU32 = ImGui.ColorConvertFloat4ToU32(fgColor);
}
```

#### Step 2: Update Render Loop to Use Fused Method

**File:** [caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs](caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs)

Replace lines 240-252:

**Before:**
```csharp
// Resolve colors using the new color resolution system
float4 baseForeground = _colorResolver.Resolve(cell.Attributes.ForegroundColor, false);
float4 baseBackground = _colorResolver.Resolve(cell.Attributes.BackgroundColor, true);

// Apply SGR attributes to colors
var (fgColor, bgColor) = _styleManager.ApplyAttributes(cell.Attributes, baseForeground, baseBackground);

// Apply foreground opacity to foreground colors and cell background opacity to background colors
fgColor = OpacityManager.ApplyForegroundOpacity(fgColor);
bgColor = OpacityManager.ApplyCellBackgroundOpacity(bgColor);

foregroundColors[col] = fgColor;
```

**After:**
```csharp
// Resolve and process all colors in one fused call
_colorResolver.ResolveCellColors(
    in cell.Attributes,
    _styleManager,
    out uint fgColorU32,
    out uint bgColorU32,
    out bool cellNeedsBackground);

// Store for later use in text rendering
// Note: We now store uint directly instead of float4
foregroundColorsU32[col] = fgColorU32;
```

#### Step 3: Update Data Structures

Change the pooled array type:

**Before:**
```csharp
float4[] foregroundColors = ArrayPool<float4>.Shared.Rent(Math.Max(terminalWidthCells, 1));
```

**After:**
```csharp
uint[] foregroundColorsU32 = ArrayPool<uint>.Shared.Rent(Math.Max(terminalWidthCells, 1));
```

Update all references to `foregroundColors[col]` to use `foregroundColorsU32[col]` and remove the `ImGui.ColorConvertFloat4ToU32()` calls that now happen inside the fused method.

### Additional Required Changes

The `float4 foregroundColors` array is also used for decorations. Either:
1. Keep both arrays (uint for text, float4 for decorations), or
2. Store uint and convert back to float4 only for decoration code

Option 1 is simpler and maintains existing decoration code.

### Testing

1. **Color accuracy test:** Compare rendered colors before/after, should be identical.
2. **Performance test:** Measure call reduction and timing improvement.
3. **Edge cases:**
   - Hidden text (should be invisible)
   - Inverse video (bg/fg swap)
   - Faint text (dimmed colors)
   - Bold with bright colors
   - 256-color and RGB colors

### Expected Impact

| Metric | Before | After | Improvement |
|--------|--------|-------|-------------|
| Function calls per cell | 5-6 | 1 | 80-85% reduction |
| Float4 conversions | 2 per cell | 0-1 per cell | 50-100% reduction |
| Background resolution | Always | Only when needed | ~70% reduction |

---

## Task 6: Incremental Dirty Row Tracking

### Goal
Track which rows have changed since the last frame and only re-process those rows. This is a more advanced optimization that builds on the previous tasks.

### Current Behavior

Every frame re-processes all rows regardless of whether content changed.

### Proposed Solution

1. Terminal emulator marks rows as dirty when modified
2. Render loop checks dirty flags and skips unchanged rows
3. Dirty flags are cleared after rendering

### Prerequisites

- Task 1 (cell early exit) should be completed first
- Task 3 (row skip) should be completed first
- Requires integration with `TerminalEmulator` change tracking

### Implementation Complexity

**High** - Requires changes across multiple layers:
- `ScreenBuffer` needs dirty flag storage
- `TerminalEmulator` needs to set flags on mutations
- Render loop needs to respect and clear flags
- Scroll operations need to handle flag shifting

### Deferral Recommendation

This task should be deferred until Tasks 1-5 are complete and measured. The simpler row-skip optimization (Task 3) provides similar benefits without the complexity of change tracking.

---

## Implementation Order

Recommended order based on impact and dependencies:

| Order | Task | Estimated Impact | Dependencies | Risk |
|-------|------|------------------|--------------|------|
| 1 | Task 2: Row-level selection | High for selection frames | None | Low |
| 2 | Task 1: Early exit for default cells | Very High | Task 2 (uses row selection) | Low |
| 3 | Task 3: Skip empty rows | Medium-High | Task 2 | Low |
| 4 | Task 4: Batch backgrounds | Medium | None | Low |
| 5 | Task 5: Fused color processing | Medium | None | Medium |
| 6 | Task 6: Dirty row tracking | Medium | Tasks 1-5 | High |

Tasks 1-4 can be implemented incrementally with low risk. Each provides measurable improvement and can be validated independently.

---

## Validation Approach

After each task:

1. **Functional validation:** Run existing test suite, verify no regressions
2. **Visual validation:** Manual inspection of terminal rendering with various content
3. **Performance validation:** Use `_perfWatch` infrastructure to measure:
   - Total render time
   - Call counts for key operations
   - Compare against baseline in `IMGUI_TIMING_RESULTS.md`

Target: Reduce `TerminalController.Render` from ~1.75ms to <1.0ms average.
