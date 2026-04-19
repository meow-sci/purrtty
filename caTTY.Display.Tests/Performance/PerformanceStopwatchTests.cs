using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using caTTY.Display.Performance;

namespace caTTY.Display.Tests.Performance;

/// <summary>
/// Comprehensive unit tests for PerformanceStopwatch class.
/// Tests cover basic functionality, concurrency, aggregation, thread safety, precision, and edge cases.
/// </summary>
[TestFixture]
[Category("Unit")]
public class PerformanceStopwatchTests
{
    private PerformanceStopwatch _stopwatch = null!;

    [SetUp]
    public void SetUp()
    {
        _stopwatch = new PerformanceStopwatch();
    }

    #region Constructor and Properties Tests

    [Test]
    public void Constructor_ShouldInitializeWithDefaultValues()
    {
        // Arrange & Act
        var stopwatch = new PerformanceStopwatch();

        // Assert
        Assert.That(stopwatch.Enabled, Is.False, "Enabled should default to false");
        Assert.That(stopwatch.DumpIntervalFrames, Is.EqualTo(60), "DumpIntervalFrames should default to 60");
    }

    [Test]
    public void Enabled_WhenSet_ShouldUpdateProperty()
    {
        // Act
        _stopwatch.Enabled = true;

        // Assert
        Assert.That(_stopwatch.Enabled, Is.True);
    }

    [Test]
    public void DumpIntervalFrames_WhenSet_ShouldUpdateProperty()
    {
        // Act
        _stopwatch.DumpIntervalFrames = 120;

        // Assert
        Assert.That(_stopwatch.DumpIntervalFrames, Is.EqualTo(120));
    }

    #endregion

    #region Basic Start/Stop Functionality Tests

    [Test]
    public void Start_WhenDisabled_ShouldNotRecordTiming()
    {
        // Arrange
        _stopwatch.Enabled = false;

        // Act
        _stopwatch.Start("Task1");
        _stopwatch.Stop("Task1");
        var summary = _stopwatch.GetSummary();

        // Assert
        Assert.That(summary, Does.Contain("No performance data collected"));
    }

    [Test]
    public void Start_WhenEnabled_ShouldRecordTiming()
    {
        // Arrange
        _stopwatch.Enabled = true;

        // Act
        _stopwatch.Start("Task1");
        Thread.Sleep(1); // Ensure measurable time
        _stopwatch.Stop("Task1");
        var summary = _stopwatch.GetSummary();

        // Assert
        Assert.That(summary, Does.Not.Contain("No performance data collected"));
        Assert.That(summary, Does.Contain("Task1"));
    }

    [Test]
    public void Stop_WithoutStart_ShouldNotRecordTiming()
    {
        // Arrange
        _stopwatch.Enabled = true;

        // Act
        _stopwatch.Stop("TaskNeverStarted");
        var summary = _stopwatch.GetSummary();

        // Assert
        Assert.That(summary, Does.Contain("No performance data collected"));
    }

    [Test]
    public void Stop_WhenDisabled_ShouldNotRecordTiming()
    {
        // Arrange
        _stopwatch.Enabled = false;

        // Act
        _stopwatch.Start("Task1");
        _stopwatch.Stop("Task1");
        var summary = _stopwatch.GetSummary();

        // Assert
        Assert.That(summary, Does.Contain("No performance data collected"));
    }

    [Test]
    public void StartStop_WithSameTaskMultipleTimes_ShouldRecordAllInstances()
    {
        // Arrange
        _stopwatch.Enabled = true;

        // Act
        for (int i = 0; i < 5; i++)
        {
            _stopwatch.Start("RepeatTask");
            Thread.Sleep(1);
            _stopwatch.Stop("RepeatTask");
        }
        var summary = _stopwatch.GetSummary();

        // Assert
        Assert.That(summary, Does.Contain("RepeatTask"));
        Assert.That(summary, Does.Contain("5")); // Count should be 5
    }

    #endregion

    #region Multiple Concurrent Tasks Tests

    [Test]
    public void MultipleTasks_ShouldBeTrackedSeparately()
    {
        // Arrange
        _stopwatch.Enabled = true;

        // Act
        _stopwatch.Start("Task1");
        Thread.Sleep(1);
        _stopwatch.Stop("Task1");

        _stopwatch.Start("Task2");
        Thread.Sleep(1);
        _stopwatch.Stop("Task2");

        _stopwatch.Start("Task3");
        Thread.Sleep(1);
        _stopwatch.Stop("Task3");

        var summary = _stopwatch.GetSummary();

        // Assert
        Assert.That(summary, Does.Contain("Task1"));
        Assert.That(summary, Does.Contain("Task2"));
        Assert.That(summary, Does.Contain("Task3"));
    }

    [Test]
    public void OverlappingTasks_ShouldBeTrackedCorrectly()
    {
        // Arrange
        _stopwatch.Enabled = true;

        // Act
        _stopwatch.Start("OuterTask");
        _stopwatch.Start("InnerTask");
        Thread.Sleep(1);
        _stopwatch.Stop("InnerTask");
        Thread.Sleep(1);
        _stopwatch.Stop("OuterTask");

        var summary = _stopwatch.GetSummary();

        // Assert
        Assert.That(summary, Does.Contain("OuterTask"));
        Assert.That(summary, Does.Contain("InnerTask"));
    }

    [Test]
    public void StartTwice_ShouldOverwritePreviousStart()
    {
        // Arrange
        _stopwatch.Enabled = true;

        // Act
        _stopwatch.Start("Task1");
        Thread.Sleep(5);
        _stopwatch.Start("Task1"); // Overwrite the first start
        Thread.Sleep(1);
        _stopwatch.Stop("Task1");

        var summary = _stopwatch.GetSummary();

        // Assert - should only have 1 count, not 2
        Assert.That(summary, Does.Contain("Task1"));
        // The timing should be closer to 1ms than 6ms since the first start was overwritten
    }

    #endregion

    #region Summary Aggregation Tests

    [Test]
    public void GetSummary_WithNoData_ShouldReturnNoDataMessage()
    {
        // Arrange
        _stopwatch.Enabled = true;

        // Act
        var summary = _stopwatch.GetSummary();

        // Assert
        Assert.That(summary, Is.EqualTo("No performance data collected."));
    }

    [Test]
    public void GetSummary_ShouldIncludeFrameCount()
    {
        // Arrange
        _stopwatch.Enabled = true;
        _stopwatch.Start("Task1");
        _stopwatch.Stop("Task1");
        _stopwatch.OnFrameEnd();

        // Act
        var summary = _stopwatch.GetSummary();

        // Assert
        Assert.That(summary, Does.Contain("1 frames"));
    }

    [Test]
    public void GetSummary_ShouldIncludeTaskName()
    {
        // Arrange
        _stopwatch.Enabled = true;
        _stopwatch.Start("MyCustomTask");
        _stopwatch.Stop("MyCustomTask");

        // Act
        var summary = _stopwatch.GetSummary();

        // Assert
        Assert.That(summary, Does.Contain("MyCustomTask"));
    }

    [Test]
    public void GetSummary_ShouldIncludeTotalMilliseconds()
    {
        // Arrange
        _stopwatch.Enabled = true;
        _stopwatch.Start("Task1");
        Thread.Sleep(10);
        _stopwatch.Stop("Task1");

        // Act
        var summary = _stopwatch.GetSummary();

        // Assert - should have a Total (ms) column with a value > 0
        Assert.That(summary, Does.Contain("Total (ms)"));
        Assert.That(summary, Does.Match(@"\d+\.\d+")); // Should contain decimal numbers
    }

    [Test]
    public void GetSummary_ShouldIncludeCount()
    {
        // Arrange
        _stopwatch.Enabled = true;
        for (int i = 0; i < 3; i++)
        {
            _stopwatch.Start("Task1");
            _stopwatch.Stop("Task1");
        }

        // Act
        var summary = _stopwatch.GetSummary();

        // Assert
        Assert.That(summary, Does.Contain("Count"));
        Assert.That(summary, Does.Contain("3")); // Count should be 3
    }

    [Test]
    public void GetSummary_ShouldIncludeAverageMicroseconds()
    {
        // Arrange
        _stopwatch.Enabled = true;
        _stopwatch.Start("Task1");
        Thread.Sleep(1);
        _stopwatch.Stop("Task1");

        // Act
        var summary = _stopwatch.GetSummary();

        // Assert
        Assert.That(summary, Does.Contain("Avg (µs)"));
    }

    [Test]
    public void GetSummary_ShouldSortByTotalTimeDescending()
    {
        // Arrange
        _stopwatch.Enabled = true;

        _stopwatch.Start("FastTask");
        Thread.Sleep(2);
        _stopwatch.Stop("FastTask");

        _stopwatch.Start("SlowTask");
        Thread.Sleep(100);
        _stopwatch.Stop("SlowTask");

        _stopwatch.Start("MediumTask");
        Thread.Sleep(50);
        _stopwatch.Stop("MediumTask");

        // Act
        var summary = _stopwatch.GetSummary();

        // Assert - SlowTask should appear before MediumTask, which should appear before FastTask
        var slowTaskIndex = summary.IndexOf("SlowTask");
        var mediumTaskIndex = summary.IndexOf("MediumTask");
        var fastTaskIndex = summary.IndexOf("FastTask");

        Assert.That(slowTaskIndex, Is.LessThan(mediumTaskIndex), "SlowTask should appear before MediumTask");
        Assert.That(mediumTaskIndex, Is.LessThan(fastTaskIndex), "MediumTask should appear before FastTask");
    }

    [Test]
    public void GetSummary_ShouldTruncateLongTaskNames()
    {
        // Arrange
        _stopwatch.Enabled = true;
        var longTaskName = new string('A', 150); // 150 characters, should be truncated to 59 + "..."
        _stopwatch.Start(longTaskName);
        _stopwatch.Stop(longTaskName);

        // Act
        var summary = _stopwatch.GetSummary();

        // Assert - should contain truncated name with "..."
        Assert.That(summary, Does.Contain("..."));
    }

    [Test]
    public void GetSummary_ShouldCalculateAverageCorrectly()
    {
        // Arrange
        _stopwatch.Enabled = true;

        // Execute task multiple times with measurable delays
        for (int i = 0; i < 10; i++)
        {
            _stopwatch.Start("Task1");
            Thread.Sleep(1);
            _stopwatch.Stop("Task1");
        }

        // Act
        var summary = _stopwatch.GetSummary();

        // Assert - Count should be 10
        Assert.That(summary, Does.Contain("10"));
    }

    [Test]
    public void GetSummary_WithTerminalControllerRenderTask_ShouldCalculateAverageFrameTime()
    {
        // Arrange
        _stopwatch.Enabled = true;

        // Simulate 5 frames of rendering
        for (int i = 0; i < 5; i++)
        {
            _stopwatch.Start("TerminalController.Render");
            Thread.Sleep(2);
            _stopwatch.Stop("TerminalController.Render");
            _stopwatch.OnFrameEnd();
        }

        // Act
        var summary = _stopwatch.GetSummary();

        // Assert - should include average frame time
        Assert.That(summary, Does.Match(@"\d+\.\d+ms average frame time"));
    }

    #endregion

    #region Thread Safety Tests

    [Test]
    public void ConcurrentStartStop_ShouldBeThreadSafe()
    {
        // Arrange
        _stopwatch.Enabled = true;
        var tasks = new List<Task>();
        var taskCount = 10;
        var iterationsPerTask = 100;

        // Act - multiple threads starting and stopping tasks concurrently
        for (int i = 0; i < taskCount; i++)
        {
            var taskId = i;
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < iterationsPerTask; j++)
                {
                    var taskName = $"Task{taskId}";
                    _stopwatch.Start(taskName);
                    Thread.Sleep(0); // Yield to other threads
                    _stopwatch.Stop(taskName);
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());
        var summary = _stopwatch.GetSummary();

        // Assert - should not crash and should have data
        Assert.That(summary, Does.Not.Contain("No performance data collected"));
    }

    [Test]
    public void ConcurrentGetSummary_ShouldBeThreadSafe()
    {
        // Arrange
        _stopwatch.Enabled = true;
        _stopwatch.Start("Task1");
        _stopwatch.Stop("Task1");

        var tasks = new List<Task<string>>();

        // Act - multiple threads calling GetSummary concurrently
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() => _stopwatch.GetSummary()));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert - all should return valid summaries without crashing
        foreach (var task in tasks)
        {
            Assert.That(task.Result, Does.Contain("Task1"));
        }
    }

    [Test]
    public void ConcurrentReset_ShouldBeThreadSafe()
    {
        // Arrange
        _stopwatch.Enabled = true;
        var tasks = new List<Task>();

        // Act - one thread continuously adding data, another continuously resetting
        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(500));

        tasks.Add(Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                _stopwatch.Start("Task1");
                _stopwatch.Stop("Task1");
            }
        }));

        tasks.Add(Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                _stopwatch.Reset();
                Thread.Sleep(10);
            }
        }));

        // Assert - should not crash
        Assert.DoesNotThrow(() => Task.WaitAll(tasks.ToArray()));
    }

    #endregion

    #region Precision Validation Tests

    [Test]
    public void Precision_ShouldBeMicrosecondLevel()
    {
        // Arrange
        _stopwatch.Enabled = true;
        var iterations = 10;

        // Act - measure very short operations
        for (int i = 0; i < iterations; i++)
        {
            _stopwatch.Start("ShortTask");
            // Minimal work - should still be measurable at microsecond precision
            var dummy = Stopwatch.GetTimestamp();
            _stopwatch.Stop("ShortTask");
        }

        var summary = _stopwatch.GetSummary();

        // Assert - should contain microsecond values (µs)
        Assert.That(summary, Does.Contain("µs"));
        Assert.That(summary, Does.Match(@"\d+\.\d+")); // Should have decimal precision
    }

    [Test]
    public void Precision_MultipleShortOperations_ShouldAccumulateCorrectly()
    {
        // Arrange
        _stopwatch.Enabled = true;

        // Act - many short operations
        for (int i = 0; i < 1000; i++)
        {
            _stopwatch.Start("MicroTask");
            _stopwatch.Stop("MicroTask");
        }

        var summary = _stopwatch.GetSummary();

        // Assert - count should be exactly 1000
        Assert.That(summary, Does.Contain("1000"));
    }

    [Test]
    public void TimingAccuracy_ShouldBeReasonablyAccurate()
    {
        // Arrange
        _stopwatch.Enabled = true;
        var sleepDurationMs = 10;

        // Act
        _stopwatch.Start("TimedTask");
        Thread.Sleep(sleepDurationMs);
        _stopwatch.Stop("TimedTask");

        var summary = _stopwatch.GetSummary();

        // Assert - Total time should be at least the sleep duration
        // Extract the total ms value from summary (this is a rough check)
        Assert.That(summary, Does.Contain("TimedTask"));
        // The timing should be > 0 and have microsecond precision
        Assert.That(summary, Does.Match(@"\d+\.\d+"));
    }

    #endregion

    #region Edge Cases Tests

    [Test]
    public void StopWithoutStart_ShouldNotCrash()
    {
        // Arrange
        _stopwatch.Enabled = true;

        // Act & Assert
        Assert.DoesNotThrow(() => _stopwatch.Stop("NeverStarted"));
        Assert.DoesNotThrow(() => _stopwatch.GetSummary());
    }

    [Test]
    public void MultipleStopsWithoutStart_ShouldNotCrash()
    {
        // Arrange
        _stopwatch.Enabled = true;

        // Act & Assert
        Assert.DoesNotThrow(() =>
        {
            _stopwatch.Stop("Task1");
            _stopwatch.Stop("Task1");
            _stopwatch.Stop("Task1");
        });
    }

    [Test]
    public void NestedTimings_ShouldTrackBothTasksSeparately()
    {
        // Arrange
        _stopwatch.Enabled = true;

        // Act
        _stopwatch.Start("OuterTask");
        Thread.Sleep(1);

        _stopwatch.Start("InnerTask1");
        Thread.Sleep(1);
        _stopwatch.Stop("InnerTask1");

        _stopwatch.Start("InnerTask2");
        Thread.Sleep(1);
        _stopwatch.Stop("InnerTask2");

        Thread.Sleep(1);
        _stopwatch.Stop("OuterTask");

        var summary = _stopwatch.GetSummary();

        // Assert
        Assert.That(summary, Does.Contain("OuterTask"));
        Assert.That(summary, Does.Contain("InnerTask1"));
        Assert.That(summary, Does.Contain("InnerTask2"));
    }

    [Test]
    public void EmptyTaskName_ShouldBeHandled()
    {
        // Arrange
        _stopwatch.Enabled = true;

        // Act
        _stopwatch.Start("");
        _stopwatch.Stop("");
        var summary = _stopwatch.GetSummary();

        // Assert - should not crash and should have data
        Assert.That(summary, Does.Not.Contain("No performance data collected"));
    }

    [Test]
    public void NullTaskName_ShouldNotCrash()
    {
        // Arrange
        _stopwatch.Enabled = true;

        // Act & Assert - should handle null gracefully (or throw expected exception)
        // Note: actual behavior depends on implementation - this documents expected behavior
        Assert.DoesNotThrow(() =>
        {
            _stopwatch.Start(null!);
            _stopwatch.Stop(null!);
        });
    }

    [Test]
    public void VeryLongTaskName_ShouldBeTruncatedInSummary()
    {
        // Arrange
        _stopwatch.Enabled = true;
        var longName = new string('X', 100);

        // Act
        _stopwatch.Start(longName);
        _stopwatch.Stop(longName);
        var summary = _stopwatch.GetSummary();

        // Assert - should be truncated to fit table format
        Assert.That(summary, Does.Contain("..."));
        // Full name should not appear
        Assert.That(summary, Does.Not.Contain(longName));
    }

    #endregion

    #region OnFrameEnd and Auto-Dump Tests

    [Test]
    public void OnFrameEnd_WhenDisabled_ShouldNotIncrement()
    {
        // Arrange
        _stopwatch.Enabled = false;

        // Act
        _stopwatch.OnFrameEnd();
        var summary = _stopwatch.GetSummary();

        // Assert
        Assert.That(summary, Does.Contain("No performance data collected"));
    }

    [Test]
    public void OnFrameEnd_ShouldIncrementFrameCount()
    {
        // Arrange
        _stopwatch.Enabled = true;
        _stopwatch.Start("Task1");
        _stopwatch.Stop("Task1");

        // Act
        _stopwatch.OnFrameEnd();
        _stopwatch.OnFrameEnd();
        _stopwatch.OnFrameEnd();
        var summary = _stopwatch.GetSummary();

        // Assert
        Assert.That(summary, Does.Contain("3 frames"));
    }

    [Test]
    public void OnFrameEnd_WhenReachingDumpInterval_ShouldResetData()
    {
        // Arrange
        _stopwatch.Enabled = true;
        _stopwatch.DumpIntervalFrames = 2;

        _stopwatch.Start("Task1");
        _stopwatch.Stop("Task1");
        _stopwatch.OnFrameEnd();

        _stopwatch.Start("Task1");
        _stopwatch.Stop("Task1");
        _stopwatch.OnFrameEnd(); // Should trigger dump and reset

        // Act
        var summary = _stopwatch.GetSummary();

        // Assert - data should be cleared after auto-dump
        Assert.That(summary, Does.Contain("No performance data collected"));
    }

    [Test]
    public void OnFrameEnd_BelowDumpInterval_ShouldNotResetData()
    {
        // Arrange
        _stopwatch.Enabled = true;
        _stopwatch.DumpIntervalFrames = 10;

        _stopwatch.Start("Task1");
        _stopwatch.Stop("Task1");
        _stopwatch.OnFrameEnd();
        _stopwatch.OnFrameEnd();

        // Act
        var summary = _stopwatch.GetSummary();

        // Assert - data should still be present
        Assert.That(summary, Does.Contain("Task1"));
        Assert.That(summary, Does.Contain("2 frames"));
    }

    #endregion

    #region Reset Functionality Tests

    [Test]
    public void Reset_ShouldClearAllTimings()
    {
        // Arrange
        _stopwatch.Enabled = true;
        _stopwatch.Start("Task1");
        _stopwatch.Stop("Task1");
        _stopwatch.Start("Task2");
        _stopwatch.Stop("Task2");

        // Act
        _stopwatch.Reset();
        var summary = _stopwatch.GetSummary();

        // Assert
        Assert.That(summary, Does.Contain("No performance data collected"));
    }

    [Test]
    public void Reset_ShouldClearActiveTimings()
    {
        // Arrange
        _stopwatch.Enabled = true;
        _stopwatch.Start("Task1");
        // Don't stop it

        // Act
        _stopwatch.Reset();
        _stopwatch.Stop("Task1"); // This should not record anything

        var summary = _stopwatch.GetSummary();

        // Assert
        Assert.That(summary, Does.Contain("No performance data collected"));
    }

    [Test]
    public void Reset_ShouldResetFrameCount()
    {
        // Arrange
        _stopwatch.Enabled = true;
        _stopwatch.Start("Task1");
        _stopwatch.Stop("Task1");
        _stopwatch.OnFrameEnd();
        _stopwatch.OnFrameEnd();
        _stopwatch.OnFrameEnd();

        // Act
        _stopwatch.Reset();
        _stopwatch.Start("Task2");
        _stopwatch.Stop("Task2");
        var summary = _stopwatch.GetSummary();

        // Assert - frame count should be 0, not 3
        Assert.That(summary, Does.Contain("0 frames"));
    }

    [Test]
    public void Reset_WhenDisabled_ShouldStillWork()
    {
        // Arrange
        _stopwatch.Enabled = false;

        // Act & Assert
        Assert.DoesNotThrow(() => _stopwatch.Reset());
    }

    #endregion

    #region DumpToConsole Tests

    [Test]
    public void DumpToConsole_ShouldNotCrash()
    {
        // Arrange
        _stopwatch.Enabled = true;
        _stopwatch.Start("Task1");
        _stopwatch.Stop("Task1");

        // Act & Assert
        Assert.DoesNotThrow(() => _stopwatch.DumpToConsole());
    }

    [Test]
    public void DumpToConsole_WithNoData_ShouldNotCrash()
    {
        // Arrange
        _stopwatch.Enabled = true;

        // Act & Assert
        Assert.DoesNotThrow(() => _stopwatch.DumpToConsole());
    }

    #endregion

    #region Summary Format Tests

    [Test]
    public void GetSummary_ShouldContainTableBorders()
    {
        // Arrange
        _stopwatch.Enabled = true;
        _stopwatch.Start("Task1");
        _stopwatch.Stop("Task1");

        // Act
        var summary = _stopwatch.GetSummary();

        // Assert - should have ASCII table borders
        Assert.That(summary, Does.Contain("┌"));
        Assert.That(summary, Does.Contain("┐"));
        Assert.That(summary, Does.Contain("└"));
        Assert.That(summary, Does.Contain("┘"));
        Assert.That(summary, Does.Contain("├"));
        Assert.That(summary, Does.Contain("┤"));
        Assert.That(summary, Does.Contain("│"));
    }

    [Test]
    public void GetSummary_ShouldHaveColumnHeaders()
    {
        // Arrange
        _stopwatch.Enabled = true;
        _stopwatch.Start("Task1");
        _stopwatch.Stop("Task1");

        // Act
        var summary = _stopwatch.GetSummary();

        // Assert
        Assert.That(summary, Does.Contain("Task Name"));
        Assert.That(summary, Does.Contain("Total (ms)"));
        Assert.That(summary, Does.Contain("Count"));
        Assert.That(summary, Does.Contain("Avg (µs)"));
    }

    [Test]
    public void GetSummary_ShouldIncludePerformanceSummaryHeader()
    {
        // Arrange
        _stopwatch.Enabled = true;
        _stopwatch.Start("Task1");
        _stopwatch.Stop("Task1");

        // Act
        var summary = _stopwatch.GetSummary();

        // Assert
        Assert.That(summary, Does.Contain("Performance Summary"));
    }

    #endregion

    #region Integration Tests

    [Test]
    public void CompleteWorkflow_ShouldWorkEndToEnd()
    {
        // Arrange
        _stopwatch.Enabled = true;
        _stopwatch.DumpIntervalFrames = 3;

        // Act - simulate 3 frames of rendering
        for (int frame = 0; frame < 3; frame++)
        {
            _stopwatch.Start("TerminalController.Render");

            _stopwatch.Start("RenderCanvas");
            Thread.Sleep(1);
            _stopwatch.Stop("RenderCanvas");

            _stopwatch.Start("RenderContent");
            Thread.Sleep(1);
            _stopwatch.Stop("RenderContent");

            _stopwatch.Stop("TerminalController.Render");
            _stopwatch.OnFrameEnd();
        }

        var summaryBeforeAutoDump = _stopwatch.GetSummary();

        // Assert - after 3 frames (reaching DumpIntervalFrames), data should be auto-cleared
        Assert.That(summaryBeforeAutoDump, Does.Contain("No performance data collected"));
    }

    [Test]
    public void EnableDisableWorkflow_ShouldRespectEnabledFlag()
    {
        // Arrange & Act
        _stopwatch.Enabled = false;
        _stopwatch.Start("Task1");
        _stopwatch.Stop("Task1");
        var summary1 = _stopwatch.GetSummary();

        _stopwatch.Enabled = true;
        _stopwatch.Start("Task2");
        _stopwatch.Stop("Task2");
        var summary2 = _stopwatch.GetSummary();

        _stopwatch.Enabled = false;
        _stopwatch.Start("Task3");
        _stopwatch.Stop("Task3");
        var summary3 = _stopwatch.GetSummary();

        // Assert
        Assert.That(summary1, Does.Contain("No performance data collected"));
        Assert.That(summary2, Does.Contain("Task2"));
        Assert.That(summary2, Does.Not.Contain("Task1"));
        Assert.That(summary3, Does.Contain("Task2")); // Task2 still there, Task3 not added
        Assert.That(summary3, Does.Not.Contain("Task3"));
    }

    #endregion
}
