# Performance Analysis Documentation

## Overview

The caTTY terminal emulator includes a built-in high-precision performance measurement system for analyzing ImGui rendering performance. This system uses QueryPerformanceCounter (via `Stopwatch.GetTimestamp()`) to provide microsecond-level precision timing data for all rendering operations.

The performance tracing system is designed with minimal overhead:
- **When disabled** (default): ~2-4 nanoseconds per Start/Stop pair (single bool check)
- **When enabled**: ~100-200 nanoseconds per Start/Stop pair (timestamp capture + storage)

## Quick Start

### Enabling Performance Tracing

**Method 1: Via UI (Recommended)**

1. Launch the terminal (F12 in-game)
2. Open the **Performance** menu in the menu bar
3. Check **"Enable Tracing"**
4. Tracing will begin immediately

**Method 2: Via Code**

```csharp
// Access via TerminalController
controller.PerfWatch.Enabled = true;
```

### Viewing Performance Data

Performance data is automatically dumped to the console at regular intervals:

**Default behavior:**
- Auto-dump every 60 frames (~1 second at 60 FPS)
- Output goes to stdout/console window

**Manual dump:**
1. Open the **Performance** menu
2. Click **"Dump Now"** to immediately output current data
3. Data is dumped without resetting counters

**Resetting counters:**
1. Open the **Performance** menu
2. Click **"Reset Counters"** to clear all data and start fresh

### Configuring Auto-dump Interval

**Via UI:**
1. Open the **Performance** menu
2. Modify the **"Auto-dump Interval"** field
3. Valid range: 1-600 frames (0.016s to 10s at 60 FPS)
4. Changes take effect immediately

**Via Code:**
```csharp
controller.PerfWatch.DumpIntervalFrames = 120; // Dump every 120 frames (~2 seconds)
```

## Output Format

### Example Output

```
================================================================================
[2026-01-05 14:32:15] Performance Summary (60 frames, 16.67ms average frame time)
┌─────────────────────────────────┬──────────────┬───────┬──────────────┐
│ Task Name                       │ Total (ms)   │ Count │ Avg (µs)     │
├─────────────────────────────────┼──────────────┼───────┼──────────────┤
│ CellRenderingLoop               │      450.23  │    60 │   7503.83    │
│ RenderCell                      │      425.67  │  9600 │     44.34    │
│ Font.SelectAndRender            │      280.12  │  9600 │     29.18    │
│ ColorResolver.Resolve           │       85.45  │ 19200 │      4.45    │
│ StyleManager.ApplyAttributes    │       35.23  │  9600 │      3.67    │
│ RenderDecorations               │       15.67  │  1200 │     13.06    │
│ RenderCursor                    │        8.23  │    60 │    137.17    │
│ GetViewportRows                 │        5.12  │    60 │     85.33    │
│ HandleMouseInput                │        2.34  │    60 │     39.00    │
│ Font.Push                       │        1.45  │    60 │     24.17    │
│ RenderUnderline                 │        0.89  │   800 │      1.11    │
│ RenderStrikethrough             │        0.45  │   400 │      1.13    │
└─────────────────────────────────┴──────────────┴───────┴──────────────┘
================================================================================
```

### Understanding the Columns

| Column | Description |
|--------|-------------|
| **Task Name** | Name of the measured operation (max 31 chars, truncated with "..." if longer) |
| **Total (ms)** | Total time spent in this operation across all frames (milliseconds) |
| **Count** | Number of times this operation was called |
| **Avg (µs)** | Average time per call (microseconds) |

**Header Information:**
- **Frame count**: Number of frames measured (e.g., "60 frames")
- **Average frame time**: Total render time divided by frame count (e.g., "16.67ms")
- **Timestamp**: When the dump was generated (e.g., "2026-01-05 14:32:15")

**Sorting:**
- Tasks are sorted by **Total (ms)** in descending order
- This highlights operations that consume the most cumulative time

## Interpreting Results

### Key Metrics to Monitor

1. **Average Frame Time**
   - Target: <16.67ms (60 FPS) or <8.33ms (120 FPS)
   - If higher: Frame drops are occurring, investigate top tasks

2. **Total Time per Task**
   - Shows which operations consume the most rendering budget
   - Focus optimization efforts on tasks with highest total time

3. **Average Time per Call**
   - Helps identify inefficient individual operations
   - High average time may indicate algorithmic issues

4. **Call Count**
   - Unusually high counts may indicate redundant work
   - Example: ColorResolver should be called 2x per cell (foreground + background)

### Typical Task Hierarchy

Expected performance hierarchy (fastest to slowest by total time):

```
CellRenderingLoop (outer loop, includes all cell rendering)
  └─ RenderCell (per-cell rendering, called once per visible cell)
      ├─ Font.SelectAndRender (text rendering, ~30-50% of RenderCell time)
      ├─ ColorResolver.Resolve (color calculation, called 2x per cell)
      ├─ StyleManager.ApplyAttributes (style application)
      └─ RenderDecorations (underlines, strikethrough, if present)

RenderCursor (cursor rendering, once per frame)
GetViewportRows (viewport calculation, once per frame)
HandleMouseInput (mouse handling, once per frame if hovered)
Font.Push (font stack management, once per frame)
```

### Normal vs. Problematic Patterns

**Normal Performance (80x24 terminal at 60 FPS):**
```
│ CellRenderingLoop               │      450.00  │    60 │   7500.00    │ ✓
│ RenderCell                      │      400.00  │  1920 │    208.33    │ ✓
│ Font.SelectAndRender            │      280.00  │  1920 │    145.83    │ ✓
│ ColorResolver.Resolve           │       80.00  │  3840 │     20.83    │ ✓
│ RenderCursor                    │        8.00  │    60 │    133.33    │ ✓
```

**Problematic Performance (same workload):**
```
│ CellRenderingLoop               │      950.00  │    60 │  15833.33    │ ✗ 2x slower
│ RenderCell                      │      800.00  │  1920 │    416.67    │ ✗ 2x slower
│ Font.SelectAndRender            │      600.00  │  1920 │    312.50    │ ✗ Font issue
│ ColorResolver.Resolve           │      150.00  │  3840 │     39.06    │ ✗ Color issue
│ RenderCursor                    │       25.00  │    60 │    416.67    │ ✗ Cursor issue
```

## Common Bottlenecks

### 1. Font Rendering (`Font.SelectAndRender`)

**Typical Issue:** High average time per call (>200µs)

**Possible Causes:**
- Font atlas thrashing (too many font switches)
- Large font sizes requiring more pixel processing
- Complex Unicode characters (wide chars, combining marks)

**Investigation Steps:**
1. Check font size settings (smaller = faster)
2. Verify font fallback chain isn't too long
3. Profile specific character types causing slowdowns

**Expected Performance:**
- 80x24 terminal: ~30-50% of total `RenderCell` time
- Average per call: 100-200µs at typical font sizes (12-16pt)

### 2. Color Resolution (`ColorResolver.Resolve`)

**Typical Issue:** High call count or excessive time per call

**Expected Behavior:**
- Called 2x per cell (foreground + background)
- 80x24 terminal = 3,840 calls per frame (1,920 cells × 2)
- Average per call: <10µs for simple colors, <50µs for palette lookups

**Possible Causes:**
- Unnecessary color conversions
- Palette lookup overhead
- Default color resolution happening too frequently

**Investigation Steps:**
1. Verify call count = 2 × visible cells
2. Check if palette colors are cached properly
3. Profile true color vs. indexed color performance

### 3. Cell Rendering Loop (`CellRenderingLoop`, `RenderCell`)

**Typical Issue:** Overall rendering too slow

**Expected Performance:**
- 80x24 terminal: ~7-10ms total per frame at 60 FPS
- 120x40 terminal: ~15-20ms total per frame
- Average per cell: 150-300µs depending on complexity

**Possible Causes:**
- Too many cells being rendered (check viewport calculation)
- Inefficient decoration rendering
- Redundant state changes in ImGui

**Investigation Steps:**
1. Verify visible cell count matches terminal size
2. Check decoration rendering count (should be subset of cells)
3. Profile empty cells vs. cells with decorations

### 4. Cursor Rendering (`RenderCursor`)

**Typical Issue:** Excessive time for single cursor

**Expected Performance:**
- ~100-200µs per frame (single cursor, simple shape)
- ~300-500µs for complex cursor shapes (with glow effects)

**Possible Causes:**
- Complex cursor shape requiring many draw calls
- Blink state calculation overhead
- Cursor position calculation inefficiency

**Investigation Steps:**
1. Test different cursor shapes (block, beam, underline)
2. Disable cursor blinking to isolate blink overhead
3. Check if cursor is being rendered multiple times

### 5. Mouse Input (`HandleMouseInput`)

**Typical Issue:** High time when not expected

**Expected Performance:**
- ~20-50µs per frame when terminal is hovered
- Not called when terminal is not hovered

**Possible Causes:**
- Coordinate transformation overhead
- Unnecessary selection recalculation
- Mouse position tracking inefficiency

**Investigation Steps:**
1. Verify only called when terminal is hovered
2. Check selection update logic
3. Profile coordinate conversion separately

## Advanced Analysis Techniques

### Identifying Redundant Work

**Look for suspicious call counts:**

```
│ ColorResolver.Resolve           │      150.00  │  7680 │     19.53    │
```

In an 80x24 terminal (1,920 cells), this shows 7,680 calls instead of expected 3,840 (2× per cell). This indicates colors are being resolved 4× per cell, suggesting redundant work.

### Frame Time Budget Analysis

Calculate percentage of frame budget per task:

```
Target: 16.67ms per frame (60 FPS)

CellRenderingLoop:     450ms / 60 frames = 7.5ms  → 45% of frame budget
Font.SelectAndRender:  280ms / 60 frames = 4.7ms  → 28% of frame budget
ColorResolver.Resolve:  80ms / 60 frames = 1.3ms  →  8% of frame budget
```

### Comparing Workloads

**Baseline measurement (empty terminal):**
```bash
1. Reset Counters
2. Let run for 60 frames with no shell output
3. Dump Now - save as baseline
```

**Heavy workload measurement (scrolling text):**
```bash
1. Reset Counters
2. Run: Get-ChildItem C:\ -Recurse
3. Let scroll for 60 frames
4. Dump Now - compare to baseline
```

**Difference analysis:**
- Compare Total (ms) columns
- Look for tasks that increased disproportionately
- Identify new bottlenecks under load

### Statistical Analysis

**Per-frame variance:**

If you see inconsistent frame times:
1. Collect multiple dumps (5-10)
2. Compare average frame time across dumps
3. High variance suggests non-deterministic bottlenecks (GC, I/O, etc.)

**Micro-benchmarking specific operations:**
1. Enable tracing
2. Set DumpIntervalFrames = 1 (per-frame dump)
3. Perform specific action (e.g., scroll exactly one line)
4. Analyze single-frame data for that operation

## Performance Optimization Workflow

### 1. Establish Baseline
```
1. Clean terminal (no active sessions or minimal shell output)
2. Enable Performance Tracing
3. Set DumpIntervalFrames = 120 (2 seconds for stable average)
4. Dump Now and save output
```

### 2. Identify Bottlenecks
```
1. Sort output by Total (ms) column (already done)
2. Identify tasks consuming >10% of frame budget
3. Check if call counts are reasonable
4. Look for tasks with high average time per call
```

### 3. Reproduce and Isolate
```
1. Create minimal test case that exhibits bottleneck
2. Reset Counters
3. Run test case
4. Dump Now to verify bottleneck reproduced
```

### 4. Implement Fix
```
1. Make targeted code change
2. Reset Counters
3. Run same test case
4. Dump Now and compare before/after
```

### 5. Validate Improvement
```
1. Calculate performance improvement:
   - Before: Total time for bottleneck task
   - After: Total time for bottleneck task
   - Improvement = (Before - After) / Before * 100%

2. Verify no regressions in other tasks
3. Run full test suite to ensure correctness
```

## Technical Details

### Precision and Accuracy

**Timer Source:**
- Windows: QueryPerformanceCounter (QPC)
- Typical resolution: ~100 nanoseconds
- Frequency: ~10 MHz on modern systems

**Measurement Accuracy:**
- Sub-microsecond precision for timestamp capture
- Aggregation uses double-precision floating point
- Rounding: milliseconds to 2 decimal places, microseconds to 2 decimal places

**Expected Measurement Overhead:**
- `Start()`: ~40-80ns (timestamp capture + dictionary insert)
- `Stop()`: ~60-100ns (timestamp capture + list append)
- Total per operation: ~100-200ns when enabled

### Thread Safety

The `PerformanceStopwatch` class is thread-safe using lock-based synchronization:

```csharp
private readonly object _lock = new();
```

**Lock Scope:**
- `Start()`: Lock during active timing dictionary insert
- `Stop()`: Lock during timing record list append
- `GetSummary()`: Lock during data copy, then process unlocked
- `Reset()`: Lock during collection clear

**ImGui Rendering Context:**
- ImGui is single-threaded per window
- No lock contention expected in typical usage
- Thread safety is defensive for potential future scenarios

### Data Structure Details

**Storage:**
```csharp
private struct TimingRecord
{
    public string TaskName;
    public long StartTicks;
    public long EndTicks;
}

private readonly List<TimingRecord> _timings = new();
private readonly Dictionary<string, long> _activeTimings = new();
```

**Memory Usage:**
- Each timing record: ~24 bytes (string reference + 2 longs)
- 60 frames × 100 operations = ~144 KB per dump cycle
- Memory is cleared on Reset()

### Performance Impact Measurement

**Measuring overhead with tracing disabled:**
```csharp
// No-op, just bool check
if (!Enabled) return;  // ~2-4 CPU cycles
```

**Measuring overhead with tracing enabled:**
```csharp
// Full measurement path
var ticks = Stopwatch.GetTimestamp();  // ~20-50ns (QPC call)
lock (_lock)                           // ~20-40ns (uncontended)
{
    _activeTimings[taskName] = ticks;  // ~30-50ns (dict insert)
}
// Total: ~100-200ns per Start/Stop pair
```

**Frame budget impact:**
- 100 operations × 2 calls (Start+Stop) × 150ns = 30µs per frame
- 30µs / 16,667µs (60 FPS) = 0.18% overhead
- Negligible impact on rendering performance

## Troubleshooting

### No Output When Tracing Enabled

**Symptoms:**
- Performance tracing is enabled but no console output appears

**Checks:**
1. Verify console window is visible (not redirected)
2. Check DumpIntervalFrames is reasonable (>= 1)
3. Ensure enough frames have elapsed (frameCount >= DumpIntervalFrames)
4. Try manual "Dump Now" to force output

### Incomplete or Missing Tasks

**Symptoms:**
- Expected tasks don't appear in output

**Possible Causes:**
1. Task was never called during measurement period
2. Start/Stop calls don't match (early return, exception, etc.)
3. Task name typo (case-sensitive)

**Resolution:**
1. Verify task is actually executed (add debug logging)
2. Check try/finally blocks around Stop() calls
3. Grep codebase for task name to verify spelling

### Suspiciously Low/High Numbers

**Symptoms:**
- Numbers don't match expectations

**Validation Steps:**
1. Check timer frequency: `Stopwatch.Frequency` (~10MHz expected)
2. Verify unit conversions (ticks → ms/µs)
3. Compare manual stopwatch measurement to verify accuracy
4. Check for integer overflow (unlikely but possible on long runs)

### Performance Regression After Enabling

**Symptoms:**
- Frame drops or stuttering when tracing is enabled

**Possible Causes:**
1. Too many instrumentation points (>1000 per frame)
2. Lock contention (multi-threaded scenarios)
3. String allocations in task names

**Resolution:**
1. Reduce instrumentation granularity (remove fine-grained timings)
2. Use string constants for task names (avoid allocations)
3. Increase DumpIntervalFrames to reduce aggregation frequency

## See Also

- `PerformanceStopwatch.cs`: Implementation source code
- `PerformanceMenuRenderer.cs`: UI controls for performance tracing
- `TerminalUiRender.cs`: Example instrumentation in rendering code
- `TIME_IMGUI_PLAN.md`: Full implementation plan and task tracking

## Contributing

When adding new performance instrumentation:

1. **Use descriptive task names**: "ComponentName.MethodName" format
2. **Wrap with try/finally**: Ensure Stop() is always called
3. **Minimize overhead**: Check `Enabled` before expensive setup
4. **Document expected ranges**: Add comments with typical timings

Example:
```csharp
_perfWatch.Start("MyComponent.ExpensiveOperation");
try
{
    // Your code here
    // Expected: ~100-200µs per call
}
finally
{
    _perfWatch.Stop("MyComponent.ExpensiveOperation");
}
```
