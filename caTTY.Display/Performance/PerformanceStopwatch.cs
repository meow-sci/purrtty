using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace caTTY.Display.Performance;

/// <summary>
/// High-precision performance measurement tool for tracking rendering performance.
/// Uses QueryPerformanceCounter via Stopwatch.GetTimestamp() for microsecond precision.
/// </summary>
public class PerformanceStopwatch
{
    private struct TimingRecord
    {
        public long Id;
        public long? ParentId;
        public string TaskName;
        public long StartTicks;
        public long EndTicks;
        public int Depth;
        public string? ParentTaskName;
    }

    private struct AggregatedTiming
    {
        public string TaskName;
        public double TotalMilliseconds;
        public int Count;
        public double AverageMilliseconds;
        public double AverageMicroseconds;
        public int Depth;
    }

    private struct CallStackEntry
    {
        public long Id;
        public long? ParentId;
        public string TaskName;
        public long StartTicks;
    }

    private readonly List<TimingRecord> _timings = new();
    private readonly Stack<CallStackEntry> _callStack = new();
    private readonly object _lock = new();
    private int _frameCount = 0;
    private long _nextId = 0;

    /// <summary>
    /// Runtime toggle for performance tracing. Default: false (no overhead when disabled).
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Auto-dump frequency in frames. Default: 60 frames (~1 second at 60 FPS).
    /// </summary>
    public int DumpIntervalFrames { get; set; } = 60;

    /// <summary>
    /// Record high-precision start timestamp for a task (no-op if !Enabled).
    /// </summary>
    /// <param name="taskName">Name of the task to measure</param>
    public void Start(string? taskName)
    {
        if (!Enabled) return;
        if (taskName == null) return;

        var ticks = Stopwatch.GetTimestamp();
        lock (_lock)
        {
            var parentId = _callStack.Count > 0 ? _callStack.Peek().Id : (long?)null;
            var id = ++_nextId;
            _callStack.Push(new CallStackEntry
            {
                Id = id,
                ParentId = parentId,
                TaskName = taskName,
                StartTicks = ticks
            });
        }
    }

    /// <summary>
    /// Record high-precision end timestamp for a task (no-op if !Enabled).
    /// </summary>
    /// <param name="taskName">Name of the task to measure</param>
    public void Stop(string? taskName)
    {
        if (!Enabled) return;
        if (taskName == null) return;

        var endTicks = Stopwatch.GetTimestamp();
        lock (_lock)
        {
            if (_callStack.Count > 0 && _callStack.Peek().TaskName == taskName)
            {
                var entry = _callStack.Pop();
                var parent = _callStack.Count > 0 ? _callStack.Peek().TaskName : null;
                var depth = _callStack.Count;

                _timings.Add(new TimingRecord
                {
                    Id = entry.Id,
                    ParentId = entry.ParentId,
                    TaskName = taskName,
                    StartTicks = entry.StartTicks,
                    EndTicks = endTicks,
                    Depth = depth,
                    ParentTaskName = parent
                });
            }
        }
    }

    /// <summary>
    /// Called at end of each frame. Auto-dumps to console if frame count >= DumpIntervalFrames.
    /// </summary>
    public void OnFrameEnd()
    {
        if (!Enabled) return;

        _frameCount++;
        if (_frameCount >= DumpIntervalFrames)
        {
            DumpToConsole();
            Reset();
        }
    }

    /// <summary>
    /// Clear all stored timings and reset frame counter.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _timings.Clear();
            _callStack.Clear();
            _frameCount = 0;
            _nextId = 0;
        }
    }

    private sealed class MergedNode
    {
        public required string TaskName { get; init; }
        public long TotalTicks;
        public int Count;
        public long FirstStartTicks = long.MaxValue;
        public readonly Dictionary<string, MergedNode> ChildrenByName = new(StringComparer.Ordinal);

        public MergedNode GetOrAddChild(string taskName)
        {
            if (ChildrenByName.TryGetValue(taskName, out var child))
            {
                return child;
            }

            child = new MergedNode { TaskName = taskName };
            ChildrenByName[taskName] = child;
            return child;
        }

        public IEnumerable<MergedNode> GetChildrenInEncounterOrder()
        {
            return ChildrenByName.Values.OrderBy(c => c.FirstStartTicks);
        }
    }

    private static MergedNode BuildMergedCallTree(IReadOnlyList<TimingRecord> timings)
    {
        var mergedRoot = new MergedNode { TaskName = "<root>" };

        const long RootParentKey = 0;

        // ParentId -> child invocations (kept per exact call instance)
        var childrenByParentId = new Dictionary<long, List<TimingRecord>>();
        foreach (var timing in timings)
        {
            var parentKey = timing.ParentId ?? RootParentKey;
            if (!childrenByParentId.TryGetValue(parentKey, out var list))
            {
                list = new List<TimingRecord>();
                childrenByParentId[parentKey] = list;
            }
            list.Add(timing);
        }

        foreach (var list in childrenByParentId.Values)
        {
            list.Sort(static (a, b) => a.StartTicks.CompareTo(b.StartTicks));
        }

        void MergeInvocation(TimingRecord inv, MergedNode parent)
        {
            var durationTicks = inv.EndTicks - inv.StartTicks;
            if (durationTicks < 0)
            {
                durationTicks = 0;
            }

            var merged = parent.GetOrAddChild(inv.TaskName);
            merged.TotalTicks += durationTicks;
            merged.Count += 1;
            if (inv.StartTicks < merged.FirstStartTicks)
            {
                merged.FirstStartTicks = inv.StartTicks;
            }

            if (childrenByParentId.TryGetValue(inv.Id, out var invChildren))
            {
                foreach (var child in invChildren)
                {
                    MergeInvocation(child, merged);
                }
            }
        }

        if (childrenByParentId.TryGetValue(RootParentKey, out var rootInvocations))
        {
            foreach (var inv in rootInvocations)
            {
                MergeInvocation(inv, mergedRoot);
            }
        }

        return mergedRoot;
    }

    /// <summary>
    /// Aggregate timings and return formatted ASCII table string.
    /// </summary>
    /// <returns>Formatted performance summary</returns>
    public string GetSummary()
    {
        return GetSummaryFlat();
    }

    /// <summary>
    /// Flat aggregation by task name (ignores hierarchy, may include overlapping time).
    /// </summary>
    /// <returns>Formatted performance summary</returns>
    public string GetSummaryFlat()
    {
        List<TimingRecord> timingsCopy;
        int frameCount;

        lock (_lock)
        {
            timingsCopy = new List<TimingRecord>(_timings);
            frameCount = _frameCount;
        }

        if (timingsCopy.Count == 0)
        {
            return "No performance data collected.";
        }

        // Aggregate by task name
        var aggregated = timingsCopy
            .GroupBy(t => t.TaskName)
            .Select(group =>
            {
                var totalTicks = group.Sum(t => t.EndTicks - t.StartTicks);
                var count = group.Count();
                var totalMs = totalTicks * 1000.0 / Stopwatch.Frequency;
                var avgMs = totalMs / count;
                var avgUs = totalTicks * 1_000_000.0 / Stopwatch.Frequency / count;

                return new AggregatedTiming
                {
                    TaskName = group.Key,
                    TotalMilliseconds = totalMs,
                    Count = count,
                    AverageMilliseconds = avgMs,
                    AverageMicroseconds = avgUs,
                    Depth = group.First().Depth
                };
            })
            .OrderByDescending(a => a.TotalMilliseconds)
            .ToList();

        // Calculate total render time
        var totalRenderTime = aggregated
            .Where(a => a.TaskName == "TerminalController.Render")
            .Sum(a => a.TotalMilliseconds);
        var avgFrameTime = frameCount > 0 ? totalRenderTime / frameCount : 0;

        // Build formatted table
        var sb = new StringBuilder();
        sb.AppendLine($"Performance Summary - Flat ({frameCount} frames, {avgFrameTime:F2}ms average frame time)");
        sb.AppendLine("┌────────────────────────────────────────────────────────────────┬──────────────┬─────────┬──────────────┬──────────────┐");
        sb.AppendLine("│ Task Name                                                      │ Total (ms)   │ Count   │ Avg (ms)     │ Avg (µs)     │");
        sb.AppendLine("├────────────────────────────────────────────────────────────────┼──────────────┼─────────┼──────────────┼──────────────┤");

        foreach (var timing in aggregated)
        {
            var taskName = timing.TaskName.Length > 62
                ? timing.TaskName.Substring(0, 59) + "..."
                : timing.TaskName;

            sb.AppendLine(
                $"│ {taskName,-62} │ {timing.TotalMilliseconds,12:F2} │ {timing.Count,7} │ {timing.AverageMilliseconds,12:F3} │ {timing.AverageMicroseconds,12:F2} │");
        }

        sb.AppendLine("└────────────────────────────────────────────────────────────────┴──────────────┴─────────┴──────────────┴──────────────┘");

        return sb.ToString();
    }

    /// <summary>
    /// Hierarchical view with indentation showing parent-child relationships.
    /// </summary>
    /// <returns>Formatted performance summary with hierarchy</returns>
    public string GetSummaryHierarchical()
    {
        List<TimingRecord> timingsCopy;
        int frameCount;

        lock (_lock)
        {
            timingsCopy = new List<TimingRecord>(_timings);
            frameCount = _frameCount;
        }

        if (timingsCopy.Count == 0)
        {
            return "No performance data collected.";
        }

        // Build a merged call tree from real parent/child relationships (per call instance).
        // This ensures children render immediately under their actual parent.
        var mergedRoot = BuildMergedCallTree(timingsCopy);

        // Build formatted table with indentation
        var sb = new StringBuilder();
        sb.AppendLine($"Performance Summary - Hierarchical ({frameCount} frames)");
        sb.AppendLine("┌────────────────────────────────────────────────────────────────┬──────────────┬─────────┬──────────────┬──────────────┐");
        sb.AppendLine("│ Task Name                                                      │ Total (ms)   │ Count   │ Avg (ms)     │ Avg (µs)     │");
        sb.AppendLine("├────────────────────────────────────────────────────────────────┼──────────────┼─────────┼──────────────┼──────────────┤");

        void AppendNode(MergedNode node, int depth)
        {
            var indent = new string(' ', depth * 2);
            var taskName = indent + node.TaskName;

            if (taskName.Length > 62)
            {
                taskName = taskName.Substring(0, 59) + "...";
            }

            var totalMs = node.TotalTicks * 1000.0 / Stopwatch.Frequency;
            var avgMs = node.Count > 0 ? totalMs / node.Count : 0;
            var avgUs = node.Count > 0 ? node.TotalTicks * 1_000_000.0 / Stopwatch.Frequency / node.Count : 0;

            sb.AppendLine(
                $"│ {taskName,-62} │ {totalMs,12:F2} │ {node.Count,7} │ {avgMs,12:F3} │ {avgUs,12:F2} │");

            // Show explicit self-time so parent totals reconcile with children.
            // This helps highlight uninstrumented work and measurement overhead.
            var children = node.GetChildrenInEncounterOrder().ToList();
            if (children.Count > 0)
            {
                long childTicks = 0;
                foreach (var child in children)
                {
                    childTicks += child.TotalTicks;
                }

                var selfTicks = node.TotalTicks - childTicks;
                if (selfTicks > 0)
                {
                    var selfIndent = new string(' ', (depth + 1) * 2);
                    var selfName = selfIndent + "<self>";
                    if (selfName.Length > 62)
                    {
                        selfName = selfName.Substring(0, 59) + "...";
                    }

                    var selfMs = selfTicks * 1000.0 / Stopwatch.Frequency;
                    var selfAvgMs = node.Count > 0 ? selfMs / node.Count : 0;
                    var selfAvgUs = node.Count > 0 ? selfTicks * 1_000_000.0 / Stopwatch.Frequency / node.Count : 0;

                    sb.AppendLine(
                        $"│ {selfName,-62} │ {selfMs,12:F2} │ {node.Count,7} │ {selfAvgMs,12:F3} │ {selfAvgUs,12:F2} │");
                }
            }

            foreach (var child in children)
            {
                AppendNode(child, depth + 1);
            }
        }

        foreach (var rootChild in mergedRoot.GetChildrenInEncounterOrder())
        {
            AppendNode(rootChild, 0);
        }

        sb.AppendLine("└────────────────────────────────────────────────────────────────┴──────────────┴─────────┴──────────────┴──────────────┘");

        return sb.ToString();
    }

    /// <summary>
    /// Shows both exclusive (self-time) and inclusive (total) time for each task.
    /// Exclusive time = time spent in task minus time spent in children.
    /// Displays hierarchically with indentation based on call depth.
    /// </summary>
    /// <returns>Formatted performance summary with exclusive/inclusive breakdown</returns>
    public string GetSummaryExclusive()
    {
        List<TimingRecord> timingsCopy;
        int frameCount;

        lock (_lock)
        {
            timingsCopy = new List<TimingRecord>(_timings);
            frameCount = _frameCount;
        }

        if (timingsCopy.Count == 0)
        {
            return "No performance data collected.";
        }

        // Build a map of parent -> list of children for time calculation
        var childTimeByParent = new Dictionary<string, long>();
        
        foreach (var timing in timingsCopy)
        {
            if (!string.IsNullOrEmpty(timing.ParentTaskName))
            {
                var duration = timing.EndTicks - timing.StartTicks;
                if (!childTimeByParent.ContainsKey(timing.ParentTaskName))
                {
                    childTimeByParent[timing.ParentTaskName] = 0;
                }
                childTimeByParent[timing.ParentTaskName] += duration;
            }
        }

        // Aggregate with exclusive/inclusive time by task name, depth, and parent
        var aggregated = timingsCopy
            .GroupBy(t => new { t.TaskName, t.Depth, t.ParentTaskName })
            .Select(group =>
            {
                var inclusiveTicks = group.Sum(t => t.EndTicks - t.StartTicks);
                var count = group.Count();
                
                // Calculate exclusive time (inclusive minus children)
                var childTicks = childTimeByParent.TryGetValue(group.Key.TaskName, out var ct) ? ct : 0;
                var exclusiveTicks = inclusiveTicks - childTicks;

                var inclusiveMs = inclusiveTicks * 1000.0 / Stopwatch.Frequency;
                var exclusiveMs = exclusiveTicks * 1000.0 / Stopwatch.Frequency;
                var avgExclusiveMs = exclusiveMs / count;
                var avgExclusiveUs = exclusiveTicks * 1_000_000.0 / Stopwatch.Frequency / count;
                var firstStart = group.Min(t => t.StartTicks);

                return new
                {
                    TaskName = group.Key.TaskName,
                    ParentTaskName = group.Key.ParentTaskName,
                    InclusiveMs = inclusiveMs,
                    ExclusiveMs = exclusiveMs,
                    Count = count,
                    AvgExclusiveMs = avgExclusiveMs,
                    AvgExclusiveUs = avgExclusiveUs,
                    Depth = group.Key.Depth,
                    FirstStart = firstStart
                };
            })
            .OrderBy(a => a.Depth)
            .ThenBy(a => a.ParentTaskName ?? "")
            .ThenBy(a => a.FirstStart)
            .ToList();

        // Build formatted table with indentation
        var sb = new StringBuilder();
        sb.AppendLine($"Performance Summary - Exclusive ({frameCount} frames)");
        sb.AppendLine("┌────────────────────────────────────────────────────────────────┬──────────────┬──────────────┬─────────┬──────────────┬──────────────┐");
        sb.AppendLine("│ Task Name                                                      │ Excl. (ms)   │ Incl. (ms)   │ Count   │ Avg Excl(ms) │ Avg Excl(µs) │");
        sb.AppendLine("├────────────────────────────────────────────────────────────────┼──────────────┼──────────────┼─────────┼──────────────┼──────────────┤");

        foreach (var timing in aggregated)
        {
            var indent = new string(' ', timing.Depth * 2);
            var taskName = indent + timing.TaskName;
            
            if (taskName.Length > 62)
            {
                taskName = taskName.Substring(0, 59) + "...";
            }

            sb.AppendLine(
                $"│ {taskName,-62} │ {timing.ExclusiveMs,12:F2} │ {timing.InclusiveMs,12:F2} │ {timing.Count,7} │ {timing.AvgExclusiveMs,12:F3} │ {timing.AvgExclusiveUs,12:F2} │");
        }

        sb.AppendLine("└────────────────────────────────────────────────────────────────┴──────────────┴──────────────┴─────────┴──────────────┴──────────────┘");

        return sb.ToString();
    }

    /// <summary>
    /// Calls GetSummary() and writes to Console.WriteLine() with separators and timestamp.
    /// </summary>
    public void DumpToConsole()
    {
        var summary = GetSummaryHierarchical();
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        Console.WriteLine("================================================================================");
        Console.WriteLine($"[{timestamp}] {summary}");
        Console.WriteLine("================================================================================");
    }
}
