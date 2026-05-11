using System;
using purrTTY.GameMod.InWorld.Settings;
using purrTTY.Logging;

namespace purrTTY.GameMod.InWorld;

/// <summary>
///     Top-level coordinator for the in-world (render-to-texture) terminal feature.
///     Phase 1: pure scaffolding — constructed, hot-keyed, and gated behind
///     <see cref="InWorldSettings.Enabled"/>, but performs no rendering.
/// </summary>
public sealed class InWorldTerminalManager : IDisposable
{
    private readonly InWorldSettings _settings;
    private bool _initialized;
    private bool _disposed;

    public InWorldTerminalManager(InWorldSettings settings)
    {
        _settings = settings;
    }

    /// <summary>
    ///     Called once when the game is fully loaded and the renderer is live.
    /// </summary>
    public void Initialize()
    {
        // phase 2+ populates this
        _initialized = true;
    }

    /// <summary>
    ///     Called every frame from <c>[StarMapAfterGui]</c>.
    ///     <paramref name="dt"/> is the same dt the game passes to its own AfterGui callbacks.
    /// </summary>
    public void OnAfterGui(double dt)
    {
        if (!_settings.Enabled || !_initialized || _disposed)
        {
            return;
        }
        // phase 5 populates this
    }

    /// <summary>
    ///     Toggles the master enable flag and logs the new state.
    /// </summary>
    public void Toggle()
    {
        _settings.Enabled = !_settings.Enabled;
        ModLog.Log.Debug($"purrTTY in-world terminal {(_settings.Enabled ? "enabled" : "disabled")}");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        // phase 2+ disposes resources
    }
}
