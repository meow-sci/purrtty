# ImGui Render-Loop Optimization Analysis (caTTY) - v3 (Post-Refactor)

> **Last Updated:** January 5, 2026  
> **Status:** Planning / Analysis Complete (Updated for Refactored Codebase)  
> **Supersedes:** IMGUI_IMPROVEMENTS_opus4.5.md v2
> 
> **Note:** This analysis has been updated to reflect the recent codebase refactor where `TerminalController` was split into multiple specialized service classes under `caTTY.Display/Controllers/TerminalUi/`. The refactor was **location-only** (no logical changes), so all performance concerns remain valid.

## Goal / Ideal Architecture

We want the ImGui frame to be **paint-only**:
- ImGui render code should **only read** a stable, already-prepared terminal "frame" (cells, colors, cursor, selection overlays) and emit draw calls.
- Terminal emulation (UTF-8 decode, escape parsing, stateful buffer mutation) should happen **outside** the ImGui render loop.
- Any expensive "derived view" work (viewport composition, scrollback merging, layout/run building) should happen **on change**, not every frame.

In practice, this usually means:
- A **terminal update pump** that applies process output to the emulator on a controlled thread/tick.
- A **render snapshot** (immutable-ish) produced after updates, read by ImGui.
- A **dirty/invalidations** mechanism so we rebuild only what changed.

---

## Current Architecture (What Actually Happens)

### Data path (process output → terminal state)
- `ProcessManager` reads ConPTY output asynchronously and raises `DataReceived`.
- `TerminalSession` subscribes to `ProcessManager.DataReceived` and immediately forwards bytes to the emulator:
  - [TerminalSession.cs#L235-L238](caTTY.Core/Terminal/TerminalSession.cs#L235-L238): `OnProcessDataReceived` → `Terminal.Write(e.Data.Span)`
- `TerminalEmulator.Write(ReadOnlySpan<byte>)` does parsing/state updates immediately:
  - `_parser.PushBytes(data)`
  - [TerminalEmulator.cs#L777-L779](caTTY.Core/Terminal/TerminalEmulator.cs#L777-L779): `OnScreenUpdated()` triggers events

**Implication:** Terminal state mutation happens on the process output thread (not necessarily the UI thread). This creates potential race conditions between emulation and rendering.

### Data path (terminal state → ImGui draw)
- `TerminalTestApp` runs per-frame inside the ImGui callback ([TerminalTestApp.cs#L131-L136](caTTY.TestApp/TerminalTestApp.cs#L131-L136)):
  ```csharp
  StandaloneImGui.Run((deltaTime) => 
  {
      _controller.Update(deltaTime);  // Only handles cursor blinking!
      _controller.Render();           // All heavy work happens here
  });
  ```
- **Refactored rendering pipeline:**
  - [TerminalController.cs#L370](caTTY.Display/Controllers/TerminalController.cs#L370): `Render()` → `RenderTerminalCanvas()` (line 1016)
  - [TerminalController.cs#L830-L841](caTTY.Display/Controllers/TerminalController.cs#L830-L841): `RenderTerminalContent()` delegates to service
  - [TerminalUiRender.cs#L29-L121](caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs#L29-L121): **Actual heavy work happens here**

- `TerminalUiRender.RenderTerminalContent()` performs heavy per-frame work ([TerminalUiRender.cs#L78-L105](caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs#L78-L105)):
  1. **Screen buffer copy** - Copies **every row** from `ScreenBuffer` into new `Cell[]` arrays:
     ```csharp
     var screenBuffer = new ReadOnlyMemory<Cell>[activeSession.Terminal.Height];
     for (int i = 0; i < activeSession.Terminal.Height; i++)
     {
         var rowSpan = activeSession.Terminal.ScreenBuffer.GetRow(i);
         var rowArray = new Cell[rowSpan.Length];  // ALLOCATION per row per frame!
         rowSpan.CopyTo(rowArray);
         screenBuffer[i] = rowArray.AsMemory();
     }
     ```
  2. **GetViewportRows** - Calls `ScrollbackManager.GetViewportRows(...)` which:
     - Allocates a new `List<ReadOnlyMemory<Cell>>` per call
     - For scrollback rows, allocates new `Cell[]` and copies into it
  3. **Cell loop render** - Iterates every cell and calls `RenderCell()`:
     - [TerminalUiRender.cs#L133-L194](caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs#L133-L194): Allocates `cell.Character.ToString()` per drawn character (line 187)
     - Does `ImGui.PushFont/PopFont` per character (lines 185-191)

### Additional Allocation Sources

1. **ScreenBuffer.GetRow() internally allocates** ([ScreenBuffer.cs#L152-L165](caTTY.Core/Types/ScreenBuffer.cs#L152-L165)):
   ```csharp
   public ReadOnlySpan<Cell> GetRow(int row)
   {
       if (row < 0 || row >= Height)
           return ReadOnlySpan<Cell>.Empty;
       
       // Create an array for the row data since we can't create spans from 2D arrays directly
       var rowData = new Cell[Width];  // ALLOCATION per GetRow call!
       for (int col = 0; col < Width; col++)
       {
           rowData[col] = _cells[row, col];
       }
       return new ReadOnlySpan<Cell>(rowData);
   }
   ```
   This means even before `TerminalUiRender.RenderTerminalContent` copies the data, `GetRow()` has already allocated.

2. **GameMod has separate direct data path** ([TerminalMod.cs#L188](caTTY.GameMod/TerminalMod.cs#L188)):
   ```csharp
   _processManager.DataReceived += OnProcessDataReceived;
   ```
   Any changes to `TerminalSession` must also be applied to `TerminalMod`.

3. **Double allocation in viewport retrieval** - `ScrollbackManager.GetViewportRows()` allocates for scrollback lines ([ScrollbackManager.cs#L227-L231](caTTY.Core/Managers/ScrollbackManager.cs#L227-L231)):
   ```csharp
   var line = GetLine(globalRow);
   var lineArray = new Cell[line.Length];  // ALLOCATION
   line.CopyTo(lineArray);
   result.Add(lineArray.AsMemory());
   ```

**Implication:** The ImGui render loop is doing a lot of work that is (a) derived from terminal state and (b) allocation-heavy. For an 80x24 terminal, each frame allocates approximately:
- 24 arrays from `GetRow()` (in ScreenBuffer)
- 24 arrays from the copy in `RenderTerminalContent`
- Variable scrollback allocations
- ~1920 string allocations from `ToString()` (worst case: all cells have characters)

---

## How Close Are We To "Paint-Only"?

### Good News
- [TerminalController.cs#L459-L463](caTTY.Display/Controllers/TerminalController.cs#L459-L463): The controller has an `Update(deltaTime)` method already, which is the right conceptual place to host non-render work.
- `TerminalEmulator` is headless and does not depend on ImGui (good layering).
- [TerminalEventArgs.cs#L15-L30](caTTY.Core/Terminal/TerminalEventArgs.cs#L15-L30): `ScreenUpdatedEventArgs.UpdatedRegion` infrastructure exists for dirty tracking.
- `ScrollbackManager` already uses `ArrayPool<Cell>.Shared` for its internal storage.
- **Refactor win**: Rendering logic is now isolated in dedicated service classes (`TerminalUi/*`), making it easier to introduce a render snapshot without touching other concerns.

### Gaps vs Ideal

| Gap | Impact | Priority | Location |
|-----|--------|----------|----------|
| Terminal emulation runs concurrently with rendering | Race conditions, forces defensive copying | **High** | [TerminalSession.cs#L238](caTTY.Core/Terminal/TerminalSession.cs#L238) |
| `ScreenBuffer.GetRow()` allocates internally | Hidden allocation source | **High** | [ScreenBuffer.cs#L152-L165](caTTY.Core/Types/ScreenBuffer.cs#L152-L165) |
| Viewport composition happens in `Render()` | Per-frame work | **High** | [TerminalUiRender.cs#L78-L92](caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs#L78-L92) |
| Per-character `PushFont/PopFont` and `ToString()` | Major GC pressure | **High** | [TerminalUiRender.cs#L185-L191](caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs#L185-L191) |
| No render snapshot concept | Forced to rebuild view every frame | **Medium** | [TerminalUiRender.cs](caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs) |
| `UpdatedRegion` exists but isn't used | Missed optimization opportunity | **Medium** | [TerminalEventArgs.cs#L30](caTTY.Core/Terminal/TerminalEventArgs.cs#L30) |
| GameMod has separate code path | Duplication of fixes needed | **Low** | [TerminalMod.cs#L188](caTTY.GameMod/TerminalMod.cs#L188) |

**Overall Assessment:** We are **not close** to the paint-only target. The current implementation prioritizes correctness over performance and uses per-frame copying as a safety mechanism against race conditions.

---

## Incremental Improvement Plan (Task List)

Each task is scoped so you can land it independently and keep the app working. Tasks are ordered by dependency and impact.

### Phase 1: Instrumentation & Thread Safety

#### Task 1: Measure Current Per-Frame Costs (Baseline)
- **Goal:** Make the hot paths undeniable and track improvement over time.
- **Files to Edit:**
  - [caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs](caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs)
  - [caTTY.Display/Controllers/TerminalController.cs](caTTY.Display/Controllers/TerminalController.cs) (for metrics collection)
- **Changes:**
  - Add a `RenderMetrics` class to track timing and allocations
  - Add lightweight timing around `TerminalUiRender.RenderTerminalContent()` phases (lines 29-121):
    - Screen buffer copy phase (lines 78-85)
    - `GetViewportRows` phase (lines 88-92)
    - Cell render loop phase (lines 95-105)
  - Add allocation tracking via `GC.GetAllocatedBytesForCurrentThread()` deltas
  - Print summary every N frames (e.g., 120) to avoid console spam
  - Add conditional compilation `#if CATTY_PERF_METRICS` to disable in release
- **Example API:**
  ```csharp
  private class RenderMetrics
  {
      public long ScreenBufferCopyMs;
      public long GetViewportRowsMs;
      public long CellRenderLoopMs;
      public long TotalAllocatedBytes;
      public int FrameCount;
  }
  ```

#### Task 2: Queue Process Output (Thread Safety)
- **Goal:** Make terminal emulation deterministic and keep mutation off the render thread.
- **Files to Edit:**
  - [caTTY.Core/Terminal/TerminalSession.cs](caTTY.Core/Terminal/TerminalSession.cs)
  - [caTTY.GameMod/TerminalMod.cs](caTTY.GameMod/TerminalMod.cs) (apply same pattern)
- **Changes:**
  - Add a `ConcurrentQueue<byte[]>` field and an `ArrayPool<byte>` for efficient buffer reuse
  - Replace `Terminal.Write(e.Data.Span)` in `OnProcessDataReceived` with:
    ```csharp
    private void OnProcessDataReceived(object? sender, DataReceivedEventArgs e)
    {
        // Rent a buffer and copy the data
        var buffer = _bytePool.Rent(e.Data.Length);
        e.Data.Span.CopyTo(buffer);
        _pendingOutput.Enqueue((buffer, e.Data.Length));
    }
    ```
  - Add new public method:
    ```csharp
    /// <summary>
    /// Drains pending process output into the terminal emulator.
    /// Call this from the UI thread before rendering.
    /// </summary>
    /// <param name="maxBytes">Maximum bytes to process (budget)</param>
    /// <returns>Number of bytes actually processed</returns>
    public int DrainPendingProcessOutput(int maxBytes = 256 * 1024)
    ```
  - Ensure proper buffer return to pool after processing

#### Task 3: Pump Terminal Updates in Pre-ImGui Phase
- **Goal:** Move emulation work out of the ImGui "draw UI" callback.
- **Files to Edit:**
  - [caTTY.TestApp/Rendering/StandaloneImGui.cs](caTTY.TestApp/Rendering/StandaloneImGui.cs)
  - [caTTY.Display.Playground/Rendering/StandaloneImGui.cs](caTTY.Display.Playground/Rendering/StandaloneImGui.cs)
  - [caTTY.TestApp/TerminalTestApp.cs](caTTY.TestApp/TerminalTestApp.cs)
- **Changes:**
  - Extend `StandaloneImGui.Run(...)` to accept **two callbacks** (or use a struct):
    ```csharp
    public static void Run(Action<float> onUpdate, Action onDrawUi)
    {
        // ...
        while (!window!.ShouldClose)
        {
            Glfw.PollEvents();
            
            float deltaTime = GetDeltaTime();
            onUpdate(deltaTime);  // Terminal pumping happens here
            
            ImGuiBackend.NewFrame();
            ImGui.NewFrame();
            ImGuiHelper.StartFrame();
            
            onDrawUi();  // Pure rendering
            
            ImGui.Render();
            // ...
        }
    }
    ```
  - Update `TerminalTestApp.RunAsync()`:
    ```csharp
    StandaloneImGui.Run(
        onUpdate: (deltaTime) => {
            _controller.Update(deltaTime);  // Drain output, update cursor, etc.
        },
        onDrawUi: () => {
            _controller.Render();  // Pure painting
        }
    );
    ```

### Phase 2: Eliminate Per-Frame Allocations

#### Task 4: Fix ScreenBuffer.GetRow() Allocation
- **Goal:** Stop `GetRow()` from allocating a new array every call.
- **Files to Edit:**
  - [caTTY.Core/Types/IScreenBuffer.cs](caTTY.Core/Types/IScreenBuffer.cs)
  - [caTTY.Core/Types/ScreenBuffer.cs](caTTY.Core/Types/ScreenBuffer.cs)
  - [caTTY.Core/Types/DualScreenBuffer.cs](caTTY.Core/Types/DualScreenBuffer.cs)
- **Changes:**
  - **Option A (Recommended):** Change internal storage from `Cell[,]` to `Cell[][]` (jagged array):
    ```csharp
    private Cell[][] _rows;  // Instead of Cell[,] _cells
    
    public ReadOnlySpan<Cell> GetRow(int row)
    {
        if (row < 0 || row >= Height) return ReadOnlySpan<Cell>.Empty;
        return _rows[row].AsSpan();  // No allocation!
    }
    ```
  - **Option B (Minimal change):** Add `GetRowMemory(int row)` returning `ReadOnlyMemory<Cell>`:
    ```csharp
    public ReadOnlyMemory<Cell> GetRowMemory(int row);
    ```
    Keep the backing storage but expose stable memory references.
  - Update all callers to use the non-allocating version

#### Task 5: Make Scrollback Viewport Retrieval Non-Allocating
- **Goal:** Eliminate `GetViewportRows()` allocating and copying per call.
- **Files to Edit:**
  - [caTTY.Core/Managers/IScrollbackManager.cs](caTTY.Core/Managers/IScrollbackManager.cs)
  - [caTTY.Core/Managers/ScrollbackManager.cs](caTTY.Core/Managers/ScrollbackManager.cs)
- **Changes:**
  - Add overload that fills a provided list (avoid allocation):
    ```csharp
    void FillViewportRows(
        ReadOnlyMemory<Cell>[] screenBuffer, 
        bool isAlt, 
        int requestedRows, 
        List<ReadOnlyMemory<Cell>> destination);
    ```
  - Scrollback lines are already stored as `Cell[]` arrays - return `ReadOnlyMemory<Cell>` directly without copying:
    ```csharp
    // Instead of:
    var lineArray = new Cell[line.Length];
    line.CopyTo(lineArray);
    result.Add(lineArray.AsMemory());
    
    // Do:
    result.Add(_lines[actualIndex].AsMemory(0, _columns));
    ```
  - Controller reuses a single `List<ReadOnlyMemory<Cell>>` instance across frames

#### Task 6: Move Terminal Work Out of Render()
- **Goal:** Keep `Render()` as paint-only as possible.
- **Files to Edit:**
  - [caTTY.Display/Controllers/TerminalController.cs](caTTY.Display/Controllers/TerminalController.cs) (lines 370-456 for Render, lines 459-463 for Update)
  - [caTTY.Display/Controllers/TerminalUi/TerminalUiResize.cs](caTTY.Display/Controllers/TerminalUi/TerminalUiResize.cs) (resize handling)
  - [caTTY.Display/Controllers/TerminalUi/TerminalUiInput.cs](caTTY.Display/Controllers/TerminalUi/TerminalUiInput.cs) (input processing)
- **Changes:**
  - Move these operations from `TerminalController.Render()` to `Update()`:
    - Drain pending process output via `session.DrainPendingProcessOutput()`
    - Cursor blink update (already in `Update` at line 462)
    - Window resize detection moved to `Update()` (currently in `TerminalUiResize.HandleWindowResize()` at line 424)
    - Input processing already happens conditionally (line 446)
  - Add deferred resize handling in `TerminalUiResize`:
    ```csharp
    private bool _pendingResize;
    private int _pendingWidth, _pendingHeight;
    
    // In Render(): detect size change, set flag
    // In Update(): if flag set, apply resize
    ```
  - `TerminalController.Render()` should only:
    - Read cached viewport data
    - Issue ImGui draw calls via subsystems
    - Detect (but not apply) size changes

### Phase 3: Render Snapshot & Batching

#### Task 7: Introduce Render Snapshot
- **Goal:** `TerminalUiRender.RenderTerminalContent()` reads a snapshot prepared during update, not live terminal state.
- **Files to Create/Edit:**
  - **Create:** [caTTY.Display/Types/TerminalRenderSnapshot.cs](caTTY.Display/Types/TerminalRenderSnapshot.cs)
  - **Edit:** [caTTY.Display/Controllers/TerminalController.cs](caTTY.Display/Controllers/TerminalController.cs) (Update method)
  - **Edit:** [caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs](caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs) (RenderTerminalContent method)
- **TerminalRenderSnapshot Structure:**
  ```csharp
  public class TerminalRenderSnapshot
  {
      // Viewport data (reused list, not reallocated)
      public List<ReadOnlyMemory<Cell>> ViewportRows { get; }
      
      // Cursor state
      public int CursorRow { get; set; }
      public int CursorCol { get; set; }
      public CursorStyle CursorStyle { get; set; }
      public bool CursorVisible { get; set; }
      
      // Terminal state
      public bool IsAlternateScreenActive { get; set; }
      public int ViewportOffset { get; set; }
      public int TerminalWidth { get; set; }
      public int TerminalHeight { get; set; }
      
      // Dirty tracking
      public bool IsDirty { get; set; }
      public HashSet<int> DirtyRows { get; }  // For incremental updates
  }
  ```
- **Changes:**
  - Add snapshot field to `TerminalController` or `TerminalUiRender`
  - Build snapshot in `TerminalController.Update()` when:
    - Process output was drained (screen changed)
    - Viewport offset changed (scrolling)
    - Resize occurred
  - Pass snapshot to `TerminalUiRender.RenderTerminalContent()` instead of live terminal state
  - `TerminalUiRender.RenderTerminalContent()` reads from snapshot only (lines 78-105 will read from snapshot)
  - Add `_needsSnapshotRebuild` flag in controller triggered by screen updates

#### Task 8: Batch Text Rendering
- **Goal:** Reduce ImGui draw-call overhead and GC pressure from per-character operations.
- **Files to Edit:**
  - [caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs](caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs) (RenderCell method at lines 133-202)
- **Changes:**
  - Build per-row "runs" during snapshot build (or lazy in render):
    ```csharp
    public struct TextRun
    {
        public int StartCol;
        public int Length;
        public uint ForegroundColor;  // Pre-converted for ImGui
        public uint BackgroundColor;
        public ImFontPtr Font;
        public bool HasUnderline;
        public bool HasStrikethrough;
    }
    ```
  - Group contiguous cells with same font (bold/italic) and same foreground color
  - Render backgrounds separately (can remain per-cell initially, lines 150-173)
  - Replace per-character rendering (lines 176-202) with run-based rendering
  - Use pooled `StringBuilder` or `Span<char>` for building run text:
    ```csharp
    // Instead of per-character ToString() at line 187:
    Span<char> runBuffer = stackalloc char[run.Length];
    for (int i = 0; i < run.Length; i++)
        runBuffer[i] = cells[run.StartCol + i].Character;
    drawList.AddText(pos, run.ForegroundColor, new string(runBuffer));
    ```
  - `PushFont/PopFont` per run, not per character (replace lines 185-191):
    ```csharp
    ImGui.PushFont(run.Font, fontSize);
    drawList.AddText(pos, color, runText);
    ImGui.PopFont();
    ```

#### Task 9: Dirty Row Tracking
- **Goal:** Avoid rebuilding the entire snapshot/run list when only a portion changed.
- **Files to Edit:**
  - [caTTY.Core/Terminal/TerminalEmulator.cs](caTTY.Core/Terminal/TerminalEmulator.cs) (ensure UpdatedRegion is populated at line 779)
  - [caTTY.Display/Controllers/TerminalController.cs](caTTY.Display/Controllers/TerminalController.cs) (event subscription in Initialize method)
  - [caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs](caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs) (render only dirty rows)
- **Changes:**
  - Ensure `ScreenUpdatedEventArgs.UpdatedRegion` is populated correctly when raising `ScreenUpdated`
  - In controller (or new snapshot management class), track dirty rows via event handler:
    ```csharp
    // Subscribe in TerminalController.Initialize() after line 142
    private void OnScreenUpdated(object? sender, ScreenUpdatedEventArgs e)
    {
        if (e.UpdatedRegion != null)
        {
            for (int row = e.UpdatedRegion.Value.StartRow; row <= e.UpdatedRegion.Value.EndRow; row++)
                _snapshot.DirtyRows.Add(row);
        }
        else
        {
            _snapshot.IsDirty = true;  // Full refresh
        }
    }
    ```
  - During snapshot rebuild, only recompute runs for dirty rows
  - `TerminalUiRender` receives snapshot with dirty row info and skips clean rows

### Phase 4: Testing & Validation

#### Task 10: Add Architecture Tests
- **Goal:** Ensure the separation (queue → pump → snapshot → render) stays intact.
- **Files to Create/Edit:**
  - **Create:** [caTTY.Core.Tests/Unit/Terminal/TerminalSessionOutputQueueTests.cs](caTTY.Core.Tests/Unit/Terminal/TerminalSessionOutputQueueTests.cs)
  - **Create:** [caTTY.Display.Tests/Unit/Controllers/TerminalControllerSnapshotTests.cs](caTTY.Display.Tests/Unit/Controllers/TerminalControllerSnapshotTests.cs)
  - **Create:** [caTTY.Display.Tests/Unit/Controllers/TerminalUi/TerminalUiRenderSnapshotTests.cs](caTTY.Display.Tests/Unit/Controllers/TerminalUi/TerminalUiRenderSnapshotTests.cs)
- **Test Cases for TerminalSession:**
  - Output queue ordering is preserved
  - Drain budget is respected
  - Buffers are returned to pool after drain
  - Concurrent enqueue/drain is safe
- **Test Cases for TerminalUiRender:**
  - Snapshot is rebuilt on screen update (controller responsibility)
  - Snapshot is rebuilt on scroll (controller responsibility)
  - Snapshot is rebuilt on resize (controller responsibility)
  - `TerminalUiRender.RenderTerminalContent()` uses snapshot data, not live terminal state
  - Dirty row tracking works correctly in snapshot
  - Service methods only read, never mutate terminal state directly

---

## Implementation Priority Matrix

| Task | Impact | Effort | Dependencies | Recommended Order |
|------|--------|--------|--------------|-------------------|
| Task 1 (Metrics) | Low | Low | None | 1st (enables measurement) |
| Task 2 (Queue Output) | High | Medium | None | 2nd |
| Task 3 (Pre-ImGui Pump) | High | Low | Task 2 | 3rd |
| Task 4 (Fix GetRow) | High | Medium | None | 4th |
| Task 5 (Scrollback Non-Alloc) | Medium | Medium | Task 4 | 5th |
| Task 6 (Move Work from Render) | High | Medium | Tasks 2, 3 | 6th |
| Task 7 (Render Snapshot) | High | High | Tasks 4, 5, 6 | 7th |
| Task 8 (Batch Text) | High | High | Task 7 | 8th |
| Task 9 (Dirty Rows) | Medium | Medium | Task 7 | 9th |
| Task 10 (Tests) | Low | Medium | All above | Ongoing |

---

## Summary of Key Architectural Decisions

The biggest wins for "paint-only ImGui" are:

1. **Queue process output bytes** as they arrive (Task 2)
   - Eliminates threading concerns
   - Allows controlled batch processing

2. **Fix the double-allocation in row retrieval** (Tasks 4, 5)
   - `ScreenBuffer.GetRow()` → jagged array backing
   - `ScrollbackManager.GetViewportRows()` → return memory directly

3. **Build a render snapshot once per change** (Task 7)
   - Snapshot built in `Update()`, read in `Render()`
   - No live terminal access during rendering

4. **Batch text rendering** (Task 8)
   - Per-run font push instead of per-character
   - Pooled string building instead of `ToString()` per cell

Expected improvement after all tasks: **60-80% reduction in per-frame allocations**, **significant reduction in GC pressure**, and **cleaner separation of concerns**.

---

## Comparison With Original Analysis & Refactor Impact

This v3 analysis updates v2 after the codebase refactor that split `TerminalController` into specialized service classes:

### What Changed in the Refactor (Location-Only, No Logic Changes)

| Before (v2) | After (v3) | Impact on Analysis |
|-------------|------------|-------------------|
| All rendering in `TerminalController.RenderTerminalContent()` | Split into `TerminalUiRender.RenderTerminalContent()` | **Positive**: Easier to isolate snapshot logic |
| All input handling in controller | Moved to `TerminalUiInput` | Cleaner separation, no perf impact |
| Resize logic embedded | Moved to `TerminalUiResize` | Easier to defer resize in Update() |
| Font management inline | Moved to `TerminalUiFonts` | No perf impact |
| Mouse tracking inline | Moved to `TerminalUiMouseTracking` | No perf impact |
| Selection logic inline | Moved to `TerminalUiSelection` | No perf impact |

**Key Insight:** The refactor didn't change the **performance problems**, but it made them **easier to fix** by isolating rendering concerns.

### Updates From v2 Analysis

| Addition | Description |
|----------|-------------|
| **Accurate file paths** | All paths updated to reflect `TerminalUi/*` subsystem structure |
| **Line-level precision** | Added specific line numbers for all allocation hotspots |
| **Refactor benefits** | Noted how service architecture simplifies snapshot introduction |
| **Service boundaries** | Updated tasks to respect new architectural boundaries |
| **Test structure** | Added tests for new `TerminalUiRender` service |

### From Original v1 Analysis (Still Valid)

| Addition | Description |
|----------|-------------|
| **ScreenBuffer.GetRow() allocation** | Identified hidden allocation in the 2D array → span conversion |
| **GameMod code path** | Noted that TerminalMod needs same fixes as TerminalSession |
| **Priority matrix** | Added effort/impact assessment and dependency graph |
| **Phased approach** | Reorganized tasks into logical phases |
| **Code examples** | Added concrete implementation snippets for each task |
| **Metrics class** | Added specific instrumentation guidance |
| **UpdatedRegion usage** | Noted existing infrastructure that's not being used |
| **ArrayPool usage** | Noted ScrollbackManager already uses pooling (partial win) |

---

## Quick Start: First Three Tasks

To begin implementation with the refactored codebase, start with Tasks 1-3 which provide immediate benefits with low risk:

```bash
# Suggested branch names
git checkout -b feature/imgui-perf-metrics      # Task 1 - Add metrics to TerminalUiRender
git checkout -b feature/output-queue            # Task 2 - Queue in TerminalSession
git checkout -b feature/pre-imgui-pump          # Task 3 - Update StandaloneImGui
```

Each can be merged independently and provides incremental improvement.

**Refactor Bonus:** The service architecture makes Task 1 cleaner - you can add metrics just to `TerminalUiRender` without touching the main controller logic.

---

## Real-World Timing Analysis (January 2026)

Based on actual profiling data from `IMGUI_TIMING_RESULTS.md`, the priorities for optimization differ significantly from the theoretical analysis above. Here's what the real data shows:

### Timing Breakdown (60 frames, 80×176 viewport = 10,560 cells/frame)

| Component | Time (ms) | % of Render | Calls | Avg (µs) |
|-----------|-----------|-------------|-------|----------|
| **Total Render** | 753.38 | 100% | 60 | 12,556 |
| CellRenderingLoop | 730.51 | 96.9% | 60 | 12,175 |
| └─ RenderCell | 688.65 | 91.4% | 633,600 | 1.09 |
| &nbsp;&nbsp;&nbsp;└─ **ResolveColors** | **310.07** | **41.2%** | 633,600 | 0.49 |
| &nbsp;&nbsp;&nbsp;└─ Font.SelectAndRender | 136.60 | 18.1% | 284,451 | 0.48 |
| &nbsp;&nbsp;&nbsp;└─ ApplyOpacity | 29.11 | 3.9% | 633,600 | 0.05 |
| &nbsp;&nbsp;&nbsp;└─ ApplyAttributes | 26.63 | 3.5% | 633,600 | 0.04 |
| &nbsp;&nbsp;&nbsp;└─ Setup | 24.39 | 3.2% | 633,600 | 0.04 |
| GetViewportRows | 16.16 | 2.1% | 60 | 269 |

### Key Insight: Color Resolution is the #1 Hotspot

**310ms (41%) of render time is spent in color resolution.** This was not emphasized in the original analysis.

The `ColorResolver.Resolve` breakdown (1,267,200 calls = 2× per cell for fg/bg):

| Sub-component | Time (ms) | Calls | Notes |
|--------------|-----------|-------|-------|
| DefaultColor path | 81.20 | 1,020,549 | 80% of cells use default colors |
| Named color path | 40.86 | 246,651 | Theme lookups for ANSI colors |
| ThemeLookup | 20.23 | 246,651 | Nested inside Named path |

### Revised Priority Order for Real-World Gains

Based on timing data, here's the **actual order of importance**:

#### [DONE] 🔴 Priority 1: Color Resolution Caching (Potential: ~250ms savings, 33% of render)

**Problem:** Every cell resolves fg/bg colors every frame, even though:
- 80% of cells use default colors (same every frame)
- Named colors (ANSI 0-15) are theme-dependent but rarely change
- RGB colors are already final values

**Solution:**
1. **Cache default colors** - Resolve once per theme change, not per cell
2. **Pre-resolve colors in snapshot** - Store `uint` ARGB values, not `TerminalColor` enums
3. **Inline the hot path** - The default color path should be a simple field read

```csharp
// Current (slow): Every cell, every frame
uint fg = _colorResolver.Resolve(cell.ForegroundColor, isForeground: true);

// Optimized: Pre-resolved in snapshot
uint fg = snapshotRow[col].ResolvedFgColor;  // Already computed
```

**Expected gain:** 200-250ms per 60 frames (~3.5ms/frame)

#### 🟠 Priority 2: Font Operation Batching (Potential: ~100ms savings, 13% of render)

**Problem:** 284,451 font operations per 60 frames (one per non-empty cell):
- SelectFont: 32.52ms
- PushFont: 12.94ms  
- AddText: 19.97ms
- PopFont: 11.49ms

**Solution:** Batch consecutive cells with same font into text runs (Task 8 from original analysis):
- Reduce 284,451 push/pop pairs to ~5,000-10,000 runs
- Use `stackalloc` or pooled buffer for run text

**Expected gain:** 80-100ms per 60 frames (~1.5ms/frame)

#### 🟡 Priority 3: Opacity/Attribute Pre-computation (Potential: ~45ms savings, 6% of render)

**Problem:** Every cell applies opacity and style attributes every frame.

**Solution:** Pre-compute in snapshot:
- Store final fg/bg colors with opacity already applied
- Store attribute flags as pre-computed style bits

**Expected gain:** 40-50ms per 60 frames (~0.75ms/frame)

#### 🟢 Priority 4: GetViewportRows Optimization (Potential: ~10ms savings, 2% of render)

**Problem:** 16.16ms per 60 frames in viewport row retrieval (allocations + copies).

**Note:** This was **overemphasized** in the original analysis. While allocations cause GC pressure, the actual CPU time is only 2% of render. Still worth fixing, but lower priority than originally stated.

**Expected gain:** 10-15ms per 60 frames (~0.2ms/frame)

#### ⚪ Priority 5: Other Optimizations (Minimal gains)

- **DrawBackground:** Only 1.59ms - already optimized (only draws non-default backgrounds)
- **Thread safety / queuing:** Important for correctness but minimal perf impact
- **Dirty row tracking:** Useful for partial updates but complex to implement

### Updated Implementation Order

| Order | Task | Expected Savings | Effort | ROI |
|-------|------|------------------|--------|-----|
| **1** | Color caching in snapshot | ~250ms (3.5ms/frame) | Medium | **High** |
| **2** | Font batching (Task 8) | ~100ms (1.5ms/frame) | High | Medium |
| **3** | Pre-compute opacity/attrs | ~45ms (0.75ms/frame) | Low | Medium |
| **4** | Snapshot infrastructure (Task 7) | Enables above | High | Required |
| **5** | GetViewportRows (Task 5) | ~10ms (0.2ms/frame) | Medium | Low |
| **6** | Queue output (Task 2) | Correctness | Medium | Low perf |

### New Task: Color Resolution Caching

**Goal:** Eliminate per-cell color resolution during rendering.

**Files to Edit:**
- [caTTY.Display/Types/TerminalRenderSnapshot.cs](caTTY.Display/Types/TerminalRenderSnapshot.cs) (create)
- [caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs](caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs)
- [caTTY.Display/Rendering/ColorResolver.cs](caTTY.Display/Rendering/ColorResolver.cs) (or equivalent)

**Changes:**
1. Add resolved color fields to snapshot cell structure:
   ```csharp
   public struct ResolvedCell
   {
       public char Character;
       public uint FgColor;      // Pre-resolved ARGB
       public uint BgColor;      // Pre-resolved ARGB  
       public CellAttributes Attributes;
   }
   ```

2. Resolve colors once during snapshot build (in `Update()`):
   ```csharp
   // In snapshot builder
   for (int col = 0; col < width; col++)
   {
       ref var cell = ref row[col];
       resolvedRow[col] = new ResolvedCell
       {
           Character = cell.Character,
           FgColor = ResolveColor(cell.ForegroundColor, true),
           BgColor = ResolveColor(cell.BgColor, false),
           Attributes = cell.Attributes
       };
   }
   ```

3. Cache default colors at theme load:
   ```csharp
   // Once per theme change
   _cachedDefaultFg = ResolveColor(TerminalColor.Default, true);
   _cachedDefaultBg = ResolveColor(TerminalColor.Default, false);
   
   // Hot path becomes:
   uint fg = color.IsDefault ? _cachedDefaultFg : ResolveNonDefault(color);
   ```

### Summary: Theory vs Reality

| Original Priority | Real-World Priority | Why Different |
|-------------------|---------------------|---------------|
| Allocations (high) | Color resolution (highest) | Timing shows CPU time in color ops, not allocation overhead |
| GetViewportRows (high) | GetViewportRows (low) | Only 2% of actual render time |
| Font batching (high) | Font batching (medium-high) | Correctly identified, 18% of time |
| Thread safety (high) | Thread safety (correctness) | Important but not a perf bottleneck |

**Bottom line:** Focus optimization efforts on **color resolution caching first**. This single change could reduce frame time by ~25-30%, from 12.5ms to ~9ms per frame.
