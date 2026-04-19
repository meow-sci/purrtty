namespace caTTY.Core.Terminal;

/// <summary>
///     Manages terminal dimension tracking and launch options for sessions.
/// </summary>
internal class SessionDimensionTracker
{
    private readonly object _lock = new();
    private (int cols, int rows) _lastKnownTerminalDimensions;
    private ProcessLaunchOptions _defaultLaunchOptions;

    /// <summary>
    ///     Creates a new dimension tracker with the specified default launch options.
    /// </summary>
    /// <param name="defaultLaunchOptions">Default options for launching new sessions</param>
    public SessionDimensionTracker(ProcessLaunchOptions defaultLaunchOptions)
    {
        ArgumentNullException.ThrowIfNull(defaultLaunchOptions);

        _defaultLaunchOptions = defaultLaunchOptions;
        _lastKnownTerminalDimensions = (defaultLaunchOptions.InitialWidth, defaultLaunchOptions.InitialHeight);
    }

    /// <summary>
    ///     Gets the most recently known terminal dimensions (cols, rows).
    ///     Used to seed new sessions so they start at the current UI size instead of a fixed default.
    /// </summary>
    public (int cols, int rows) LastKnownTerminalDimensions
    {
        get
        {
            lock (_lock)
            {
                return _lastKnownTerminalDimensions;
            }
        }
    }

    /// <summary>
    ///     Updates the manager's notion of the current terminal dimensions.
    ///     This also updates the default launch options so newly created processes start at the latest size.
    /// </summary>
    /// <param name="cols">Terminal width in columns</param>
    /// <param name="rows">Terminal height in rows</param>
    public void UpdateLastKnownTerminalDimensions(int cols, int rows)
    {
        if (cols < 1 || cols > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(cols), "Width must be between 1 and 1000");
        }

        if (rows < 1 || rows > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(rows), "Height must be between 1 and 1000");
        }

        lock (_lock)
        {
            _lastKnownTerminalDimensions = (cols, rows);
            _defaultLaunchOptions.InitialWidth = cols;
            _defaultLaunchOptions.InitialHeight = rows;
        }
    }

    /// <summary>
    ///     Gets a snapshot of the current default launch options.
    /// </summary>
    /// <returns>A clone of the current default launch options</returns>
    public ProcessLaunchOptions GetDefaultLaunchOptionsSnapshot()
    {
        lock (_lock)
        {
            return CloneLaunchOptions(_defaultLaunchOptions);
        }
    }

    /// <summary>
    ///     Updates the default launch options for new sessions.
    /// </summary>
    /// <param name="launchOptions">New default launch options</param>
    public void UpdateDefaultLaunchOptions(ProcessLaunchOptions launchOptions)
    {
        ArgumentNullException.ThrowIfNull(launchOptions);

        lock (_lock)
        {
            // Preserve the last-known terminal dimensions when switching shell configuration.
            // Shell switching should not reset the terminal size back to the options' defaults.
            var lastKnown = _lastKnownTerminalDimensions;

            _defaultLaunchOptions = launchOptions;
            _defaultLaunchOptions.InitialWidth = lastKnown.cols;
            _defaultLaunchOptions.InitialHeight = lastKnown.rows;
        }
    }

    /// <summary>
    ///     Gets the current default launch options.
    /// </summary>
    public ProcessLaunchOptions DefaultLaunchOptions
    {
        get
        {
            lock (_lock)
            {
                return _defaultLaunchOptions;
            }
        }
    }

    /// <summary>
    ///     Creates a deep clone of the specified launch options.
    /// </summary>
    /// <param name="options">The launch options to clone</param>
    /// <returns>A new instance with copied values</returns>
    public static ProcessLaunchOptions CloneLaunchOptions(ProcessLaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new ProcessLaunchOptions
        {
            ShellType = options.ShellType,
            CustomShellPath = options.CustomShellPath,
            CustomShellId = options.CustomShellId,
            Arguments = new List<string>(options.Arguments),
            WorkingDirectory = options.WorkingDirectory,
            EnvironmentVariables = new Dictionary<string, string>(options.EnvironmentVariables),
            InitialWidth = options.InitialWidth,
            InitialHeight = options.InitialHeight,
            CreateWindow = options.CreateWindow,
            UseShellExecute = options.UseShellExecute
        };
    }
}
