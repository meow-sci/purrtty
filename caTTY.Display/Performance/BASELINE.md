# Performance Baseline Collection Guide

## Overview

This document provides instructions for collecting baseline performance metrics for the caTTY terminal emulator in the KSA game environment. These baselines establish expected performance characteristics and help identify regressions or optimization opportunities.

## Prerequisites

1. **Game Build**: Ensure you have the latest build of caTTY.GameMod
   ```bash
   dotnet build caTTY.GameMod
   ```

2. **Mod Installation**: Copy the mod to your KSA mods folder
   ```bash
   # The build outputs to: caTTY.GameMod/bin/Debug/net10.0/caTTY.dll
   # Copy contents to: C:\Program Files\Kitten Space Agency\Mods\caTTY\
   ```

3. **Console Access**: Ensure you have access to the game's console output window to view performance dumps

## Quick Start: Collecting Your First Baseline

1. Launch KSA game
2. Press **F12** to open the terminal
3. Open **Performance** menu → Check **"Enable Tracing"**
4. Set **"Auto-dump Interval"** to **120 frames** (for stable 2-second averages)
5. Let the terminal idle for 2-3 dump cycles
6. Copy the console output and save it to a file
7. Press **Performance** → **"Reset Counters"** before the next test

## Standard Test Workloads

### Workload 1: Idle Terminal (Baseline)

**Purpose:** Establish minimum rendering cost with no active shell output

**Procedure:**
1. Open terminal (F12)
2. Performance → "Enable Tracing"
3. Performance → "Set Auto-dump Interval" → 120 frames
4. **Do not type or interact with terminal**
5. Wait for 3 auto-dumps to complete
6. Save the middle dump as your idle baseline

**Expected Characteristics:**
- Minimal CellRenderingLoop time
- RenderCell called once per visible cell (e.g., 80×24 = 1,920 cells/frame)
- ColorResolver.Resolve called 2× per visible cell (3,840 calls/frame)
- Low decoration rendering (few styled cells)
- Cursor rendering present (~100-200µs/frame)

**Baseline Template:**
```
Test Date: [YYYY-MM-DD]
Terminal Size: [cols]x[rows] (e.g., 80x24)
Font: [font name and size]
Theme: [theme name]

Average Frame Time: [XX.XX]ms
FPS: [60.00 or actual]

Top 5 Operations by Total Time:
1. CellRenderingLoop: [XXX]ms total, [XX] count, [XXXX]µs avg
2. RenderCell: [XXX]ms total, [XXXX] count, [XX]µs avg
3. Font.SelectAndRender: [XXX]ms total, [XXXX] count, [XX]µs avg
4. ColorResolver.Resolve: [XX]ms total, [XXXX] count, [XX]µs avg
5. RenderCursor: [X]ms total, [XX] count, [XXX]µs avg
```

### Workload 2: Active Shell Prompt

**Purpose:** Measure overhead of active shell with no scrolling output

**Procedure:**
1. Open terminal (F12)
2. Start PowerShell session (should start automatically)
3. Performance → "Enable Tracing"
4. Performance → "Reset Counters"
5. Wait at PowerShell prompt (do not run commands)
6. Wait for 2-3 auto-dumps
7. Save output

**Expected Characteristics:**
- Similar to idle, but may show prompt updates
- Cursor blinking active
- Minimal changes from Workload 1

### Workload 3: Moderate Scrolling Output

**Purpose:** Measure performance under typical scrolling workload

**Procedure:**
1. Open terminal (F12)
2. Performance → "Enable Tracing"
3. Performance → "Reset Counters"
4. Run command: `Get-ChildItem C:\Windows\System32 | Format-Table`
5. Let output scroll for 3-4 seconds
6. Performance → "Dump Now"
7. Save output

**Expected Characteristics:**
- Increased CellRenderingLoop time (more cells changing)
- Possibly increased ColorResolver calls (varied colors in output)
- Higher decoration rendering if output contains styled text

**Commands for Testing:**
```powershell
# PowerShell
Get-ChildItem C:\Windows\System32 | Format-Table
Get-Process | Format-Table
1..100 | ForEach-Object { "Line $_" }

# Cmd.exe
dir C:\Windows\System32 /s
```

### Workload 4: Heavy Scrolling Output

**Purpose:** Stress test rendering pipeline with continuous high-speed output

**Procedure:**
1. Open terminal (F12)
2. Performance → "Enable Tracing"
3. Performance → "Reset Counters"
4. Run command: `Get-ChildItem C:\ -Recurse -ErrorAction SilentlyContinue`
5. Let scroll for 5-10 seconds
6. Performance → "Dump Now"
7. Stop command (Ctrl+C)
8. Save output

**Expected Characteristics:**
- Maximum CellRenderingLoop time
- All visible cells updating every frame
- May drop below 60 FPS if rendering is bottleneck
- Good test for identifying rendering bottlenecks

**Commands for Testing:**
```powershell
# PowerShell - continuous output
Get-ChildItem C:\ -Recurse -ErrorAction SilentlyContinue
while ($true) { Get-Date; Start-Sleep -Milliseconds 100 }
1..10000 | ForEach-Object { "Line $_" }

# Cmd.exe - continuous output
dir C:\ /s
```

### Workload 5: Heavy Styling and Colors

**Purpose:** Test color resolution and style application performance

**Procedure:**
1. Open terminal (F12)
2. Performance → "Enable Tracing"
3. Performance → "Reset Counters"
4. Run command with colored output:
   ```powershell
   Get-ChildItem C:\Windows\System32 -Recurse | Select-Object -First 100 | Format-Table -AutoSize
   ```
5. Wait for output to complete
6. Performance → "Dump Now"
7. Save output

**Expected Characteristics:**
- Higher ColorResolver.Resolve time (many colors)
- Higher StyleManager.ApplyAttributes time (bold, underline, etc.)
- Increased RenderDecorations time (underlines, strikethrough)

### Workload 6: Interactive Editing (Vim/Nano)

**Purpose:** Measure performance with full-screen applications

**Procedure:**
1. Open terminal (F12)
2. Start a text editor (if available): `vim` or `nano`
3. Performance → "Enable Tracing"
4. Open a file
5. Scroll through the file
6. Performance → "Dump Now"
7. Save output

**Expected Characteristics:**
- Frequent full-screen redraws
- May show different patterns than scrolling output
- Tests alternate screen buffer performance

## Data Recording Template

Use this template to record baseline measurements:

```markdown
# Performance Baseline - [Test Name]

**Date:** [YYYY-MM-DD HH:MM]
**Build:** [Git commit hash or build number]
**KSA Version:** [Game version]

## Environment

- **OS:** Windows [version]
- **CPU:** [CPU model]
- **RAM:** [RAM amount]
- **GPU:** [GPU model]
- **Display Resolution:** [resolution]
- **Game FPS Cap:** [60/120/uncapped]

## Terminal Configuration

- **Size:** [cols]×[rows] (e.g., 80×24)
- **Font:** [font name], [size]pt
- **Theme:** [theme name]
- **Shell:** [PowerShell 7.x / cmd.exe]

## Workload Details

**Workload Type:** [Idle / Active Shell / Moderate Scrolling / Heavy Scrolling / etc.]

**Procedure:**
[Describe what you did]

**Commands Run:**
```
[Commands executed]
```

## Performance Results

**Raw Output:**
```
[Paste complete performance dump output here]
```

**Summary:**
- **Average Frame Time:** [XX.XX]ms
- **Target Frame Time:** [16.67ms for 60 FPS]
- **Performance Headroom:** [XX.XX]ms ([XX]%)

## Analysis

**Top 5 Time Consumers:**
1. [Task Name]: [Total]ms ([XX]% of frame)
2. [Task Name]: [Total]ms ([XX]% of frame)
3. [Task Name]: [Total]ms ([XX]% of frame)
4. [Task Name]: [Total]ms ([XX]% of frame)
5. [Task Name]: [Total]ms ([XX]% of frame)

**Key Observations:**
- [Note any unexpected results]
- [Note any bottlenecks]
- [Note any optimization opportunities]

## Comparison to Expected

**Expected vs. Actual:**
| Metric | Expected | Actual | Delta |
|--------|----------|--------|-------|
| Frame Time | [XX]ms | [XX]ms | [±XX]% |
| RenderCell Avg | [XX]µs | [XX]µs | [±XX]% |
| ColorResolver Avg | [XX]µs | [XX]µs | [±XX]% |
| Font.SelectAndRender Avg | [XX]µs | [XX]µs | [±XX]% |

**Status:** [✓ Within expected range / ⚠ Slower than expected / ✗ Critical bottleneck detected]

## Notes

[Any additional observations, issues, or context]
```

## Interpreting Baseline Results

### Frame Time Budget

**Target:** 16.67ms per frame (60 FPS)

**Budget Allocation:**
- **Good:** Rendering uses <10ms/frame (60% budget), leaving headroom for game overhead
- **Acceptable:** Rendering uses 10-14ms/frame (60-84% budget), tight but functional
- **Poor:** Rendering uses >14ms/frame (>84% budget), likely causing frame drops

### Per-Cell Rendering Cost

**80×24 terminal (1,920 visible cells):**

**Expected ranges:**
- **RenderCell average:** 100-300µs per cell
- **Font.SelectAndRender average:** 80-200µs per cell
- **ColorResolver.Resolve average:** 5-20µs per call (called 2× per cell)
- **StyleManager.ApplyAttributes average:** 3-10µs per cell

**Total per-cell cost:** 200-500µs depending on complexity

### Call Count Validation

**For an 80×24 terminal in a 60-frame measurement:**

| Operation | Expected Call Count | Calculation |
|-----------|---------------------|-------------|
| RenderCell | 115,200 | 1,920 cells × 60 frames |
| ColorResolver.Resolve | 230,400 | 1,920 cells × 2 colors × 60 frames |
| RenderCursor | 60 | 1 per frame × 60 frames |
| GetViewportRows | 60 | 1 per frame × 60 frames |

If call counts are significantly higher, investigate redundant work.

### Performance Flags

**Green Flags (Good Performance):**
- Average frame time <12ms
- RenderCell average <250µs
- ColorResolver average <15µs
- No single operation consuming >50% of frame budget

**Yellow Flags (Monitor Closely):**
- Average frame time 12-15ms
- RenderCell average 250-400µs
- ColorResolver average 15-30µs
- One operation consuming >50% of frame budget

**Red Flags (Optimization Needed):**
- Average frame time >15ms
- RenderCell average >400µs
- ColorResolver average >30µs
- Frequent frame drops below 60 FPS
- Call counts 2× or higher than expected

## Common Issues and Investigation

### Issue 1: High Font.SelectAndRender Time

**Symptoms:**
- Font.SelectAndRender consuming >40% of total frame time
- Average per-call time >250µs

**Investigation Steps:**
1. Check font size (larger fonts = more pixels to render)
2. Try different fonts to isolate font-specific issues
3. Check if font fallback is triggering frequently
4. Profile Unicode character rendering vs. ASCII

**Potential Causes:**
- Font atlas thrashing
- Complex Unicode characters
- Font size too large for GPU fill rate

### Issue 2: High ColorResolver.Resolve Time

**Symptoms:**
- ColorResolver.Resolve consuming >15% of total frame time
- Average per-call time >25µs
- Call count higher than expected (>2× visible cells)

**Investigation Steps:**
1. Verify call count = 2 × visible cells × frames
2. Check palette vs. true color performance
3. Profile default color resolution
4. Check for unnecessary color conversions

**Potential Causes:**
- Palette lookup overhead
- Redundant color resolution
- Inefficient RGBA conversion

### Issue 3: Frame Time Spikes

**Symptoms:**
- Average frame time acceptable, but periodic spikes
- Inconsistent performance dumps

**Investigation Steps:**
1. Collect multiple baselines (10+ dumps)
2. Look for patterns in spikes (every Nth frame?)
3. Check if spikes correlate with shell output
4. Monitor GC activity (may be external to tracing)

**Potential Causes:**
- Garbage collection pauses
- Background shell processing
- ImGui state updates
- Window focus changes

### Issue 4: Unexpectedly Low Performance

**Symptoms:**
- All operations showing high time
- Overall frame time >20ms for simple workloads

**Investigation Steps:**
1. Check game FPS cap (is it 30 FPS instead of 60?)
2. Verify no background processes consuming CPU/GPU
3. Check terminal window size (larger = more cells)
4. Test with minimal theme/font
5. Profile empty terminal vs. shell prompt

**Potential Causes:**
- System resource contention
- GPU driver issues
- VSync or FPS limiting
- Debug build instead of release build

## Advanced Baseline Scenarios

### Variable Terminal Sizes

Test different terminal dimensions to understand scaling behavior:

**Small (40×12):**
- Baseline for minimal rendering cost
- Should be <5ms per frame

**Medium (80×24):**
- Standard baseline
- Should be 7-12ms per frame

**Large (120×40):**
- Stress test for large terminals
- Acceptable if <16ms per frame

**Very Large (160×60):**
- Maximum stress test
- May exceed 60 FPS budget, useful for identifying bottlenecks

### Font Size Scaling

Test different font sizes to understand rendering cost scaling:

**Small (8-10pt):**
- Minimal pixel fill rate
- Fastest rendering

**Medium (12-14pt):**
- Standard size
- Balanced performance

**Large (16-20pt):**
- Increased pixel fill rate
- Higher font rendering cost

**Very Large (24pt+):**
- Stress test for font rendering
- May show GPU fill rate limitations

### Theme Complexity

Test different themes to measure color/style overhead:

**Minimal (Default theme, 16 colors):**
- Baseline for color resolution
- Minimal palette lookups

**Rich (256-color theme):**
- Increased palette usage
- More color resolution work

**True Color (24-bit RGB theme):**
- Maximum color depth
- Tests RGBA conversion performance

## Baseline Storage and Tracking

### Recommended File Organization

```
caTTY.Display/Performance/Baselines/
├── YYYY-MM-DD_idle_80x24.md
├── YYYY-MM-DD_scrolling_moderate_80x24.md
├── YYYY-MM-DD_scrolling_heavy_80x24.md
├── YYYY-MM-DD_vim_editing_80x24.md
└── historical/
    ├── 2026-01-05_baseline_summary.md
    └── 2026-02-01_post_optimization_summary.md
```

### Version Control

Consider committing baseline data to git:
- Track performance changes over time
- Identify regressions in pull requests
- Document optimization improvements

### Baseline Refresh Cadence

**Recommended frequency:**
- **After major refactors:** Collect new baselines
- **After performance optimizations:** Compare before/after
- **Monthly:** Collect fresh baselines to detect drift
- **Before releases:** Validate performance meets targets

## Automation Opportunities

While this guide focuses on manual testing, consider automating baseline collection:

### Future Automation Ideas

1. **Scripted test harness:**
   - Launch game
   - Enable tracing
   - Run test commands
   - Capture output
   - Parse and compare to expected ranges

2. **CI/CD integration:**
   - Run performance tests on each commit
   - Flag regressions in pull requests
   - Track performance trends over time

3. **Performance benchmarking suite:**
   - Standardized workloads
   - Automated result comparison
   - Regression detection

## Reporting Performance Issues

When reporting performance issues, include:

1. **Full baseline data** (using template above)
2. **Comparison to expected** (if available)
3. **Steps to reproduce**
4. **System information**
5. **Raw performance dump output**
6. **Screenshots or video** (if visual issues present)

## See Also

- [README.md](README.md) - Performance tracing system documentation
- [PerformanceStopwatch.cs](../Performance/PerformanceStopwatch.cs) - Implementation details
- [TIME_IMGUI_PLAN.md](../../TIME_IMGUI_PLAN.md) - Implementation plan and task tracking

## Contributing

When collecting baselines for contribution to the project:

1. **Use the standard template** provided above
2. **Include environment details** (hardware, software versions)
3. **Test multiple workloads** (not just idle)
4. **Document any anomalies** observed
5. **Provide raw data** (complete performance dumps)

Your baseline data helps establish performance expectations and identify optimization opportunities for the entire community. Thank you for contributing!
