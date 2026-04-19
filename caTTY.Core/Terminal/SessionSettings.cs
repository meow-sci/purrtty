namespace caTTY.Core.Terminal;

/// <summary>
///     Settings specific to a terminal session.
///     This is separate from display-related terminal settings.
/// </summary>
public class SessionSettings
{
    /// <summary>Terminal title for session identification</summary>
    public string Title { get; set; } = "Terminal 1";

    /// <summary>Whether this session is currently active</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Working directory for this session</summary>
    public string WorkingDirectory { get; set; } = Environment.CurrentDirectory;

    /// <summary>Terminal dimensions specific to this session (columns)</summary>
    public int Columns { get; set; } = 80;

    /// <summary>Terminal dimensions specific to this session (rows)</summary>
    public int Rows { get; set; } = 25;

    /// <summary>Process ID of the shell running in this session (if any)</summary>
    public int? ProcessId { get; set; }

    /// <summary>Exit code of the process if it has exited</summary>
    public int? ExitCode { get; set; }

    /// <summary>Whether the process in this session is currently running</summary>
    public bool IsProcessRunning { get; set; } = false;

    /// <summary>Shell command or executable used for this session</summary>
    public string ShellCommand { get; set; } = "cmd.exe";

    /// <summary>Last time this session was active</summary>
    public DateTime? LastActiveTime { get; set; }

    /// <summary>Time when this session was created</summary>
    public DateTime CreatedTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Validates the session settings for consistency and reasonable values.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when settings contain invalid values</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            throw new ArgumentException("Title cannot be null or empty");
        }

        if (Columns <= 0)
        {
            throw new ArgumentException("Columns must be greater than zero");
        }

        if (Rows <= 0)
        {
            throw new ArgumentException("Rows must be greater than zero");
        }

        if (string.IsNullOrWhiteSpace(WorkingDirectory))
        {
            WorkingDirectory = Environment.CurrentDirectory;
        }

        if (string.IsNullOrWhiteSpace(ShellCommand))
        {
            ShellCommand = "cmd.exe";
        }
    }

    /// <summary>
    /// Creates a copy of the current settings.
    /// </summary>
    /// <returns>A new SessionSettings instance with the same values</returns>
    public SessionSettings Clone()
    {
        return new SessionSettings
        {
            Title = Title,
            IsActive = IsActive,
            WorkingDirectory = WorkingDirectory,
            Columns = Columns,
            Rows = Rows,
            ProcessId = ProcessId,
            ExitCode = ExitCode,
            IsProcessRunning = IsProcessRunning,
            ShellCommand = ShellCommand,
            LastActiveTime = LastActiveTime,
            CreatedTime = CreatedTime
        };
    }

    /// <summary>
    /// Updates the session settings when the process state changes.
    /// </summary>
    /// <param name="processId">Process ID of the running process</param>
    /// <param name="isRunning">Whether the process is currently running</param>
    /// <param name="exitCode">Exit code if the process has exited</param>
    public void UpdateProcessState(int? processId, bool isRunning, int? exitCode = null)
    {
        ProcessId = processId;
        IsProcessRunning = isRunning;
        ExitCode = exitCode;
    }

    /// <summary>
    /// Updates the terminal dimensions for this session.
    /// </summary>
    /// <param name="columns">Number of columns</param>
    /// <param name="rows">Number of rows</param>
    public void UpdateDimensions(int columns, int rows)
    {
        if (columns <= 0 || rows <= 0)
        {
            throw new ArgumentException("Dimensions must be greater than zero");
        }

        Columns = columns;
        Rows = rows;
    }

    /// <summary>
    /// Marks this session as active and updates the last active time.
    /// </summary>
    public void MarkAsActive()
    {
        IsActive = true;
        LastActiveTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Marks this session as inactive.
    /// </summary>
    public void MarkAsInactive()
    {
        IsActive = false;
    }
}