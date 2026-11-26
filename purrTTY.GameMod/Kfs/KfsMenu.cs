using Brutal.ImGuiApi;
using purrTTY.Kfs;
using purrTTY.Kfs.Commands;
using purrTTY.Logging;

namespace purrTTY.GameMod.Kfs;

/// <summary>
/// The purrTTY menu's <c>kFS</c> submenu (plan §7.5), drawn by <see cref="TerminalMenus"/> inside
/// <c>BeginMenu("kFS")</c> through its <see cref="TerminalMenus.DrawKfsMenu"/> hook, which
/// <see cref="TerminalMod"/> sets once the extension has been installed. A disabled status line,
/// then entries that run <c>purr</c> verbs exactly as typing them in the game console would (their
/// output goes to the console), <b>Reset light overrides</b> among them (<c>purr lights reset</c>).
/// Below the separator, the Neovim entries (task D3): <b>Edit in Neovim...</b> (the workspace's
/// source files) and <b>Open workspace in Neovim</b>, which run <c>purr nvim [file]</c> and work
/// with the extension disabled; greyed out, with the reason, when there is no usable workspace or
/// Neovim cannot be started (no <c>nvim</c>, a wrong <c>nvim_path</c>, a <c>.cmd</c> shim, in-world
/// unavailable); and under them the last launch's error, so a failed click is never silent.
/// Between them, the language server (task C4): its status line, <b>Copy server address</b> and
/// <b>Language server in console</b> (<c>purr lsp</c>).
/// </summary>
/// <remarks>
/// <para>Runs every frame while the menu is open: the status text is
/// <see cref="KfsExtension.StatusSummary"/>, a string cached when the status changes (and made ASCII
/// then, for the font), so drawing allocates nothing. The launch check (<c>kfs.toml</c>, the PATH
/// probe) runs when the kFS menu opens and the file listing (a disk read) when the Edit submenu
/// opens — "opens" meaning it was not drawn in the previous frame (<c>ImGui.GetFrameCount</c>) —
/// and both are cached with their menu labels and commands. The verbs run on the main thread in
/// the UI phase, as console commands do.</para>
/// <para>Never throws into the game's frame: <see cref="TerminalMenus"/> catches around
/// <see cref="Draw"/> and ends the kFS menu in a <c>finally</c>; the Edit submenu does the same
/// for its own <c>BeginMenu</c>.</para>
/// </remarks>
internal static class KfsMenu
{
    private const string StatusPrefix = "Extension: ";
    private const float WrapEms = 32f;

    /// <summary>The Neovim launcher; null until <see cref="TerminalMod"/> has built it (the Neovim entries are hidden then).</summary>
    internal static KfsNeovimLauncher? Neovim;

    // Rebuilt only when the cached summary changes (the menu reads it every frame).
    private static string? s_summary;
    private static string s_statusLine = StatusPrefix;

    // Frame numbers of the last draw of the kFS menu and of the Edit submenu (open detection).
    private static int s_lastMenuFrame = int.MinValue;
    private static int s_lastEditFrame = int.MinValue;

    // Refreshed on open: null when Neovim can be opened, else the greyed-out reason line.
    private static string? s_launchProblemLine;
    private static string[] s_files = [];
    private static string[] s_fileCommands = [];
    private static string? s_filesLine;

    // The launcher's last failure and the line drawn for it (rebuilt when it changes).
    private static string? s_failure;
    private static string? s_failureLine;

    private static bool s_editFailureLogged;

    internal static void Draw()
    {
        int frame = ImGui.GetFrameCount();
        bool menuOpened = frame != s_lastMenuFrame + 1;
        s_lastMenuFrame = frame;

        string summary = KfsExtension.StatusSummary;
        if (!ReferenceEquals(summary, s_summary))
        {
            s_summary = summary;
            s_statusLine = StatusPrefix + ConsoleText.ToAscii(summary);
        }
        ImGui.TextDisabled(s_statusLine);

        bool active = KfsExtension.IsActive;
        if (ImGui.MenuItem("List parts in console", default, false, active))
        {
            PurrConsole.Execute("parts");
        }

        // A program's colour, cone and intensity outlive it (stop, trap, another program).
        if (ImGui.MenuItem("Reset light overrides", default, false, active))
        {
            PurrConsole.Execute("lights reset");
        }

        if (ImGui.MenuItem("Write example programs", default, false, active))
        {
            PurrConsole.Execute("examples");
        }

        if (ImGui.MenuItem("Extension status in console"))
        {
            PurrConsole.Execute("status");
        }

        DrawLanguageServer(frame, menuOpened);
        DrawNeovim(frame, menuOpened);
    }

    // The language server's line, rebuilt when the menu opens and about once a second while it is
    // open (the client count changes), so drawing it allocates nothing in between.
    private static string s_lspLine = "Language server: not running";
    private static string? s_lspAddress;

    private static void DrawLanguageServer(int frame, bool menuOpened)
    {
        ImGui.Separator();
        if (menuOpened || (frame & 63) == 0)
        {
            KfsLspService? service = KfsLspService.Current;
            s_lspLine = service?.MenuLine ?? "Language server: not running";
            s_lspAddress = service?.Address;
        }
        ImGui.TextDisabled(s_lspLine);

        string? address = s_lspAddress;
        if (ImGui.MenuItem("Copy server address", default, false, address is not null) && address is not null)
        {
            ImGui.SetClipboardText(address);
        }

        if (ImGui.MenuItem("Language server in console"))
        {
            PurrConsole.Execute("lsp");
        }
    }

    private static void DrawNeovim(int frame, bool menuOpened)
    {
        KfsNeovimLauncher? neovim = Neovim;
        if (neovim is null)
        {
            return;
        }

        ImGui.Separator();
        if (menuOpened)
        {
            string? problem = neovim.LaunchProblem();
            s_launchProblemLine = problem is null ? null : "Neovim: " + ConsoleText.ToAscii(problem);
        }

        bool usable = s_launchProblemLine is null;
        if (KSA.ImGuiHelper.BeginMenu("Edit in Neovim...", usable))
        {
            try
            {
                DrawEditMenu(neovim, frame);
            }
            catch (Exception ex)
            {
                if (!s_editFailureLogged)
                {
                    s_editFailureLogged = true;
                    ModLog.Log.Warning($"purrTTY kFS: the Edit in Neovim menu failed ({ex.GetType().Name}: {ex.Message}); further failures are not logged");
                }
            }
            finally
            {
                ImGui.EndMenu();
            }
        }

        if (ImGui.MenuItem("Open workspace in Neovim", default, false, usable))
        {
            PurrConsole.Execute("nvim");
        }

        if (s_launchProblemLine is { } line)
        {
            Wrapped(line);
        }

        string? failure = neovim.LastFailure;
        if (!ReferenceEquals(failure, s_failure))
        {
            s_failure = failure;
            s_failureLine = failure is null ? null : "Last launch failed: " + ConsoleText.ToAscii(failure);
        }
        if (s_failureLine is { } failed && !ReferenceEquals(failed, s_launchProblemLine))
        {
            Wrapped(failed);
        }
    }

    private static void DrawEditMenu(KfsNeovimLauncher neovim, int frame)
    {
        if (frame != s_lastEditFrame + 1)
        {
            RefreshFiles(neovim);
        }
        s_lastEditFrame = frame;

        if (s_filesLine is not null)
        {
            ImGui.TextDisabled(s_filesLine);
        }

        for (int i = 0; i < s_files.Length; i++)
        {
            if (ImGui.MenuItem(s_files[i]))
            {
                PurrConsole.Execute(s_fileCommands[i]);
            }
        }
    }

    private static void Wrapped(string line)
    {
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * WrapEms);
        ImGui.TextDisabled(line);
        ImGui.PopTextWrapPos();
    }

    private static void RefreshFiles(KfsNeovimLauncher neovim)
    {
        string[] files = neovim.ListSources(out string? problem);
        var commands = new string[files.Length];
        for (int i = 0; i < files.Length; i++)
        {
            commands[i] = "nvim " + files[i];
        }

        s_files = files;
        s_fileCommands = commands;
        s_filesLine = problem ?? (files.Length == 0 ? "no .c, .h, .kasm or .kinc files yet (purr nvim <name> creates one)" : null);
    }
}
