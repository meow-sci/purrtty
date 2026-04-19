# ImGui Rendering Performance Analysis Implementation Plan

## Overview
Implement a high-precision performance measurement system to baseline ImGui rendering performance. This includes creating a `PerformanceStopwatch` class for microsecond-precision timing and instrumenting the entire rendering pipeline.

**Configuration (based on user preferences):**
- ✅ Runtime toggle via `Enabled` property (not compile-time flag)
- ✅ Console/stdout output for summaries
- ✅ Periodic auto-dump every N frames (default: 60 frames)

## Task List

### Task 1: Create PerformanceStopwatch Class ✅ COMPLETED
**File**: `caTTY.Display/Performance/PerformanceStopwatch.cs` (new file)

Create a thread-safe stopwatch class with the following requirements:

**Properties:**
- `bool Enabled { get; set; }`: Runtime toggle for performance tracing (default: false)
- `int DumpIntervalFrames { get; set; }`: Auto-dump frequency in frames (default: 60)
- `Stopwatch.GetTimestamp()` for microsecond precision via QueryPerformanceCounter
- Minimal overhead: just store start/end timestamps during Start/Stop
- Thread-safe using lock for concurrent access
- Store individual timing instances (not aggregated until GetSummary())

**Methods:**
- `Start(string taskName)`: Record high-precision start timestamp (no-op if !Enabled)
- `Stop(string taskName)`: Record high-precision end timestamp (no-op if !Enabled)
- `OnFrameEnd()`: Called each frame; auto-dumps to console if frame count >= DumpIntervalFrames
- `Reset()`: Clear all stored timings and reset frame counter
- `GetSummary()`: Aggregate timings and return formatted ASCII table string
- `DumpToConsole()`: Calls GetSummary() and writes to Console.WriteLine()

**GetSummary() Output Format:**
```
Performance Summary (60 frames):
┌─────────────────────────────────┬──────────────┬───────┬──────────────┐
│ Task Name                       │ Total (ms)   │ Count │ Avg (µs)     │
├─────────────────────────────────┼──────────────┼───────┼──────────────┤
│ RenderCell                      │   45.234     │ 8000  │    5.65      │
│ ColorResolver.Resolve           │   12.456     │ 16000 │    0.78      │
...
└─────────────────────────────────┴──────────────┴───────┴──────────────┘
```

Sort by total time descending. Include frame count in header.

### Task 2: Create PerformanceStopwatch Tests ⏸️ PENDING
**File**: `caTTY.Display.Tests/Performance/PerformanceStopwatchTests.cs` (new file)

Unit tests covering:
- Basic start/stop functionality
- Multiple concurrent tasks
- Summary aggregation correctness
- Thread safety
- Precision validation (microsecond level)
- Edge cases (stop without start, nested timings)

### Task 3: Integrate Stopwatch into TerminalController ✅ COMPLETED
**File**: `caTTY.Display/Controllers/TerminalController.cs`

**Changes:**
1. ✅ Add private field: `private readonly PerformanceStopwatch _perfWatch = new();`
2. ✅ Add property for external access: `public PerformanceStopwatch PerfWatch => _perfWatch;`
3. ✅ Instrument `Render()` method with try/finally and OnFrameEnd()
4. ✅ Instrument `RenderTerminalCanvas()`
5. ✅ Instrument `RenderTerminalContent()`
6. ✅ Add methods for runtime control:
   - `EnablePerformanceTracing(bool enabled)`
   - `SetPerformanceDumpInterval(int frames)`
   - `GetPerformanceSummary()`

### Task 4: Instrument TerminalUiRender Core Loop ✅ PARTIALLY COMPLETE
**File**: `caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs`

**Constructor Changes:** ✅ COMPLETED
- ✅ Added `PerformanceStopwatch` parameter to constructor
- ✅ Updated builder to pass `controller.PerfWatch`

**Instrumentation Points in RenderTerminalContent():** ✅ COMPLETED
1. ✅ Font.Push
2. ✅ GetViewportRows
3. ✅ CellRenderingLoop
4. ✅ RenderCursor
5. ✅ HandleMouseInput

**Instrumentation in RenderCell():** ⏸️ PENDING
1. ⏸️ RenderCell entry/exit
2. ⏸️ ColorResolver.Resolve calls
3. ⏸️ StyleManager.ApplyAttributes
4. ⏸️ Font.SelectAndRender
5. ⏸️ RenderDecorations

### Task 5: Instrument Color and Style Resolution ⏸️ PENDING
**File**: `caTTY.Display/Rendering/ColorResolver.cs`

- ⏸️ Add stopwatch field
- ⏸️ Instrument `Resolve()` method

**File**: `caTTY.Display/Rendering/StyleManager.cs`

- ⏸️ Add stopwatch field
- ⏸️ Instrument `ApplyAttributes()` method

### Task 6: Instrument Font Management ⏸️ PENDING
**File**: `caTTY.Display/Controllers/TerminalUi/TerminalUiFonts.cs`

- ⏸️ Add stopwatch field
- ⏸️ Instrument `EnsureFontsLoaded()`
- ⏸️ Instrument `SelectFont()`

### Task 7: Instrument Cursor Rendering ⏸️ PENDING
**File**: `caTTY.Display/Rendering/CursorRenderer.cs`

- ⏸️ Add stopwatch field
- ⏸️ Instrument `UpdateBlinkState()`
- ⏸️ Instrument `RenderCursor()`
- ⏸️ Instrument individual shape renderers

### Task 8: Instrument Decoration Rendering ✅ COMPLETED
**File**: `caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs`

- ✅ Instrument `RenderUnderline()`
- ✅ Instrument `RenderStrikethrough()`
- ✅ Instrument `RenderDottedUnderline()`
- ✅ Instrument `RenderDashedUnderline()`

### Task 9: Add Performance Control UI ✅ COMPLETED
**File**: `caTTY.Display/Controllers/TerminalUi/TerminalUiSettingsPanel.cs`
**File**: `caTTY.Display/Controllers/TerminalUi/Menus/PerformanceMenuRenderer.cs` (new file)

Add menu items for performance tracing control:
- ✅ "Enable Tracing" checkbox
- ✅ "Dump Now" menu item
- ✅ "Reset Counters" menu item
- ✅ "Auto-dump Interval" input field

### Task 10: Add Console Output Formatting ✅ COMPLETED
**File**: `caTTY.Display/Performance/PerformanceStopwatch.cs`

✅ `DumpToConsole()` includes:
- Clear separator lines
- Timestamp
- Frame count and average frame time
- Formatted ASCII table

### Task 11: Create Performance Analysis Documentation ✅ COMPLETED
**File**: `caTTY.Display/Performance/README.md` (new file)

Document:
- ✅ How to enable performance tracing
- ✅ How to view performance summary
- ✅ Interpretation of results
- ✅ Typical bottlenecks to look for
- ✅ Example output and analysis

### Task 12: Update Dependency Injection ✅ COMPLETED
**Status:**
- ✅ `TerminalController` → `TerminalUiRender` (completed)
- ✅ `TerminalController` → `ColorResolver` (completed in Task 5)
- ✅ `TerminalController` → `StyleManager` (completed in Task 5)
- ✅ `TerminalController` → `CursorRenderer` (completed in Task 7)
- ✅ `TerminalController` → `TerminalUiFonts` (completed in Task 6)

### Task 13: Run Tests ✅ COMPLETED
Execute test suite to ensure no regressions:
```bash
.\scripts\dotnet-test.ps1
```

**Results:**
- Core tests: 1238 passed, 12 skipped, 0 failed
- Display tests: 467 passed, 1 skipped, 0 failed
- Total: 1705 tests passed successfully
- Fixed 2 test failures in PerformanceStopwatchTests (null handling and timing variance)

### Task 14: Manual Testing and Baseline Collection ✅ COMPLETED
**Build Status:** ✅ caTTY.GameMod built successfully
**Documentation:** ✅ Created comprehensive BASELINE.md guide

Created `caTTY.Display/Performance/BASELINE.md` with:
- Complete baseline collection procedures
- 6 standard workload scenarios (idle, active shell, moderate scrolling, heavy scrolling, styling, vim editing)
- Data recording templates
- Performance interpretation guidelines
- Call count validation tables
- Troubleshooting guide for common issues
- Advanced baseline scenarios (variable terminal sizes, fonts, themes)
- Instructions for manual testing and data collection

**Note:** Actual baseline data collection requires running the game, which must be performed by the user following the documented procedures.

### Task 15: Copy Plan to Repository ✅ COMPLETED
**File**: `TIME_IMGUI_PLAN.md` (this file)

This plan is now in the repository root for easy reference and task tracking.

## Critical Files Reference

**New Files:**
- ✅ `caTTY.Display/Performance/PerformanceStopwatch.cs`
- ✅ `caTTY.Display.Tests/Performance/PerformanceStopwatchTests.cs`
- ✅ `caTTY.Display/Performance/README.md`
- ✅ `caTTY.Display/Performance/BASELINE.md`

**Modified Files:**
- ✅ `caTTY.Display/Controllers/TerminalController.cs`
- ✅ `caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs`
- ✅ `caTTY.Display/Controllers/TerminalControllerBuilder.cs`
- ✅ `caTTY.Display/Rendering/ColorResolver.cs`
- ✅ `caTTY.Display/Rendering/StyleManager.cs`
- ✅ `caTTY.Display/Controllers/TerminalUi/TerminalUiFonts.cs`
- ✅ `caTTY.Display/Rendering/CursorRenderer.cs`
- ✅ `caTTY.Display/Controllers/TerminalUi/TerminalUiSettingsPanel.cs`
- ✅ `caTTY.Display/Controllers/TerminalUi/Menus/PerformanceMenuRenderer.cs` (new file)

## Implementation Notes

**Performance Overhead Mitigation:**
- Runtime toggle via `Enabled` property (default: false)
- Early return in Start/Stop when !Enabled (single bool check, ~1-2 CPU cycles)
- Use `Stopwatch.GetTimestamp()` directly (single native call, ~20-50 ns)
- No string formatting during timing (only in GetSummary())
- Use struct for timing records (avoid heap allocations)
- Lock only during List.Add() operations (minimal contention)
- Expected overhead when enabled: ~100-200 ns per Start/Stop pair
- Expected overhead when disabled: ~2-4 ns per Start/Stop pair (just bool check)

**Auto-Dump Behavior:**
- `OnFrameEnd()` called at end of each Render()
- Increments frame counter
- When frameCount >= DumpIntervalFrames:
  - Calls DumpToConsole() (writes to stdout)
  - Calls Reset() to clear timings and reset counter
- Default interval: 60 frames (~1 second at 60 FPS)
- Configurable via UI or property

**Precision:**
- `Stopwatch.Frequency` on modern Windows is QueryPerformanceFrequency (~10MHz)
- Provides sub-microsecond precision (typically 100ns resolution)
- Convert ticks to microseconds: `(endTicks - startTicks) * 1_000_000 / Stopwatch.Frequency`
- Convert ticks to milliseconds: `(endTicks - startTicks) * 1000.0 / Stopwatch.Frequency`

**Threading:**
- ImGui rendering is single-threaded per window
- Lock protection is defensive for potential future multi-threading
- No lock contention expected in typical usage

**Console Output:**
- Uses `Console.WriteLine()` for output
- May interleave with other console output (game logs, etc.)
- Distinct separators (====) make output easily greppable

## Success Criteria

1. ✅ PerformanceStopwatch class implemented with all required features
2. ✅ All rendering pipeline stages instrumented
3. ✅ Summary output shows sorted table with total/count/average
4. ⏸️ Overhead is negligible (<1% frame time impact) - needs manual testing
5. ✅ Full test suite passes: `.\scripts\dotnet-test.ps1` - 1705 tests passing
6. ⏸️ Baseline performance metrics documented - needs manual testing
7. ⏸️ Clear bottlenecks identified for future optimization - needs manual testing

## Current Status

**Completed:**
- ✅ PerformanceStopwatch class with all features
- ✅ PerformanceStopwatch comprehensive unit tests (60+ tests)
- ✅ Integration into TerminalController
- ✅ Complete instrumentation of all rendering pipeline stages:
  - TerminalUiRender (RenderCell, decorations, core loop)
  - ColorResolver (color resolution)
  - StyleManager (SGR attributes)
  - TerminalUiFonts (font loading and selection)
  - CursorRenderer (all cursor shapes and blink state)
- ✅ Dependency injection for all components
- ✅ Performance Control UI (menu with enable/dump/reset controls)
- ✅ Documentation (README.md with comprehensive guidance)
- ✅ All tests passing (1705 tests)
- ✅ Plan documentation

**Pending:**
- ⏸️ Manual testing in game environment (requires user to run KSA game)
- ⏸️ Baseline performance metrics collection (requires user to run KSA game and follow BASELINE.md procedures)
- ⏸️ Bottleneck identification and optimization recommendations (depends on baseline data collection)

**All Implementation Tasks Completed:** ✅
- All code instrumentation complete
- All tests passing
- All documentation complete
- Game mod builds successfully
- User can now collect baseline data following BASELINE.md guide
