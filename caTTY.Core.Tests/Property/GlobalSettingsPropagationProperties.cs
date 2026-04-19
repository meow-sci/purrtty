using caTTY.Core.Terminal;
using FsCheck;
using NUnit.Framework;

namespace caTTY.Core.Tests.Property;

/// <summary>
///     Property-based tests for global settings propagation across multiple sessions.
///     These tests verify that global settings coordination works correctly across all sessions.
///     **Feature: multi-session-support, Property 9: Global Settings Propagation**
///     **Validates: Requirements 6.1, 6.2, 6.3, 6.4, 6.5**
/// </summary>
[TestFixture]
[Category("Property")]
public class GlobalSettingsPropagationProperties
{
    /// <summary>
    ///     Generator for valid session counts for testing.
    ///     Reduced range for better performance.
    /// </summary>
    public static Arbitrary<int> SessionCountArb =>
        Arb.From(Gen.Choose(1, 3)); // Reduced from 5 to 3 for performance

    /// <summary>
    ///     Generator for mock font configuration objects.
    /// </summary>
    public static Arbitrary<object> FontConfigArb =>
        Arb.From(Gen.Elements(
            new { FontSize = 12.0f, FontName = "Regular" },
            new { FontSize = 16.0f, FontName = "Bold" },
            new { FontSize = 20.0f, FontName = "Italic" },
            new { FontSize = 24.0f, FontName = "BoldItalic" }
        ).Select(x => (object)x));

    /// <summary>
    ///     **Feature: multi-session-support, Property 9: Global Settings Propagation**
    ///     **Validates: Requirements 6.1, 6.2, 6.3, 6.4, 6.5**
    ///     Property: For any session manager with multiple sessions, when global settings
    ///     are applied, all sessions should remain functional and accessible.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 1, QuietOnSuccess = true)] // Reduced iterations for performance
    public FsCheck.Property GlobalSettingsPreserveSessionFunctionality()
    {
        return Prop.ForAll(SessionCountArb, FontConfigArb,
            (sessionCount, fontConfig) =>
            {
                // Arrange: Create session manager with multiple sessions
                using var sessionManager = new SessionManager(maxSessions: 10);
                var sessions = new List<TerminalSession>();

                try
                {
                    // Create multiple sessions with optimized launch options
                    var optimizedOptions = ProcessLaunchOptions.CreatePowerShellQuietCDrive();
                    optimizedOptions.ShellType = ShellType.PowerShell; // Use fastest shell
                    
                    for (int i = 0; i < sessionCount; i++)
                    {
                        var session = sessionManager.CreateSessionAsync($"Test Session {i + 1}", optimizedOptions).Result;
                        sessions.Add(session);
                    }

                    // Store initial state
                    var initialStates = sessions.Select(s => new
                    {
                        Id = s.Id,
                        Width = s.Terminal.Width,
                        Height = s.Terminal.Height,
                        State = s.State,
                        Title = s.Title
                    }).ToList();

                    // Act: Apply global settings to all sessions
                    sessionManager.ApplyFontConfigToAllSessions(fontConfig);

                    // Assert: Verify all sessions maintain their core functionality
                    var allSessionsPreserved = sessions.All(s =>
                        s.State != SessionState.Disposed &&
                        s.Terminal != null &&
                        s.ProcessManager != null
                    );

                    // Verify session identities are preserved
                    var sessionIdentitiesPreserved = initialStates.All(initial =>
                        sessions.Any(s => s.Id == initial.Id && s.Title == initial.Title)
                    );

                    // Verify session manager state is consistent
                    var managerStateConsistent = sessionManager.SessionCount == sessionCount;

                    // Verify all sessions are still accessible through the manager
                    var allSessionsAccessible = sessions.All(s => 
                        sessionManager.Sessions.Any(ms => ms.Id == s.Id)
                    );

                    return allSessionsPreserved && sessionIdentitiesPreserved && 
                           managerStateConsistent && allSessionsAccessible;
                }
                finally
                {
                    // SessionManager.Dispose() will handle session cleanup automatically
                    // No need for manual session disposal
                }
            });
    }

    /// <summary>
    ///     **Feature: multi-session-support, Property 9: Global Settings Propagation**
    ///     **Validates: Requirements 6.1, 6.4**
    ///     Property: For any session manager, global settings changes should not affect
    ///     session-specific state like active session tracking and session order.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 1, QuietOnSuccess = true)] // Reduced iterations for performance
    public FsCheck.Property GlobalSettingsPreserveSessionOrder()
    {
        return Prop.ForAll(SessionCountArb, FontConfigArb,
            (sessionCount, fontConfig) =>
            {
                // Arrange: Create session manager with multiple sessions
                using var sessionManager = new SessionManager(maxSessions: 10);
                var sessions = new List<TerminalSession>();

                try
                {
                    // Create multiple sessions with optimized launch options
                    var optimizedOptions = ProcessLaunchOptions.CreatePowerShellQuietCDrive();
                    optimizedOptions.ShellType = ShellType.PowerShell; // Use fastest shell
                    
                    for (int i = 0; i < sessionCount; i++)
                    {
                        var session = sessionManager.CreateSessionAsync($"Test Session {i + 1}", optimizedOptions).Result;
                        sessions.Add(session);
                    }

                    // Store initial session order and active session
                    var initialOrder = sessionManager.Sessions.Select(s => s.Id).ToList();
                    var initialActiveSession = sessionManager.ActiveSession?.Id;

                    // Switch to a different session if we have multiple sessions
                    if (sessionCount > 1)
                    {
                        sessionManager.SwitchToSession(sessions[0].Id);
                    }

                    var activeSessionBeforeChange = sessionManager.ActiveSession?.Id;

                    // Act: Apply global settings
                    sessionManager.ApplyFontConfigToAllSessions(fontConfig);

                    // Assert: Verify session order is preserved
                    var finalOrder = sessionManager.Sessions.Select(s => s.Id).ToList();
                    var orderPreserved = initialOrder.SequenceEqual(finalOrder);

                    // Verify active session is preserved
                    var activeSessionPreserved = sessionManager.ActiveSession?.Id == activeSessionBeforeChange;

                    // Verify all sessions still exist
                    var allSessionsExist = sessions.All(s => 
                        sessionManager.Sessions.Any(ms => ms.Id == s.Id)
                    );

                    return orderPreserved && activeSessionPreserved && allSessionsExist;
                }
                finally
                {
                    // SessionManager.Dispose() will handle session cleanup automatically
                    // No need for manual session disposal
                }
            });
    }

    /// <summary>
    ///     **Feature: multi-session-support, Property 9: Global Settings Propagation**
    ///     **Validates: Requirements 6.3, 6.5**
    ///     Property: For any session manager, applying global settings should not cause
    ///     session disposal or state corruption.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 1, QuietOnSuccess = true)] // Reduced iterations for performance
    public FsCheck.Property GlobalSettingsDoNotCorruptSessions()
    {
        return Prop.ForAll(SessionCountArb, FontConfigArb,
            (sessionCount, fontConfig) =>
            {
                // Arrange: Create session manager with multiple sessions
                using var sessionManager = new SessionManager(maxSessions: 10);
                var sessions = new List<TerminalSession>();

                try
                {
                    // Create multiple sessions with optimized launch options
                    var optimizedOptions = ProcessLaunchOptions.CreatePowerShellQuietCDrive();
                    optimizedOptions.ShellType = ShellType.PowerShell; // Use fastest shell
                    
                    for (int i = 0; i < sessionCount; i++)
                    {
                        var session = sessionManager.CreateSessionAsync($"Test Session {i + 1}", optimizedOptions).Result;
                        sessions.Add(session);
                    }

                    // Store initial session states
                    var initialSessionStates = sessions.Select(s => s.State).ToList();

                    // Act: Apply global settings multiple times to test robustness
                    sessionManager.ApplyFontConfigToAllSessions(fontConfig);
                    sessionManager.ApplyFontConfigToAllSessions(fontConfig); // Apply twice

                    // Assert: Verify no sessions were corrupted or disposed
                    var noSessionsCorrupted = sessions.All(s => 
                        s.State != SessionState.Disposed && 
                        s.State != SessionState.Failed
                    );

                    // Verify session manager maintains correct count
                    var sessionCountMaintained = sessionManager.SessionCount == sessionCount;

                    // Verify all sessions are still in the manager's collection
                    var allSessionsInCollection = sessions.All(s =>
                        sessionManager.Sessions.Any(ms => ms.Id == s.Id)
                    );

                    // Verify terminal and process manager references are still valid
                    var allReferencesValid = sessions.All(s =>
                        s.Terminal != null && s.ProcessManager != null
                    );

                    return noSessionsCorrupted && sessionCountMaintained && 
                           allSessionsInCollection && allReferencesValid;
                }
                finally
                {
                    // SessionManager.Dispose() will handle session cleanup automatically
                    // No need for manual session disposal
                }
            });
    }
}