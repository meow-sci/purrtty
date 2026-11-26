// RULE FOR THIS FILE: no game type. The feeds and the tests' fakes see only these shapes.

namespace purrTTY.Kfs.Lsp.Live;

/// <summary>
/// A trap of the running program, as the feeds publish it (plan §6.7).
/// </summary>
/// <param name="Message">What the game's <c>kfs state</c> says about it (<c>trap DIV_ZERO in task 0, function step</c>).</param>
/// <param name="File">The compiler's file name from the image's debug lines (<c>blink.c</c>), or null.</param>
/// <param name="Path">The workspace path of that file (<c>x_blink/blink.c</c>) when the loaded program's project is known, else null.</param>
/// <param name="Line">The 1-based source line, or 0 when the image has no line for the trapping instruction.</param>
public sealed record LiveTrap(string Message, string? File, string? Path, int Line);

/// <summary>
/// The state of the running program at one moment (the <c>purrtty/kfsState</c> payload before it
/// becomes JSON). Value equality: the feed sends a state only when it differs from the last one sent.
/// </summary>
/// <param name="Program">The loaded program's name (its project), or null when nothing is loaded.</param>
/// <param name="State">The computer state as the game names it (<c>NoSoftware</c>, <c>Standby</c>, <c>Boot</c>, <c>Running</c>, <c>Idle</c>, <c>Halted</c>).</param>
/// <param name="Tick">The last tick run (<c>k</c>).</param>
/// <param name="Overruns">Ticks that ran over their budget.</param>
/// <param name="Trap">The trap that halted the program, or null.</param>
public sealed record LiveState(string? Program, string State, ulong Tick, uint Overruns, LiveTrap? Trap);

/// <summary>
/// The workspace project of the program loaded through the workbench: its name, folder and file
/// names, so a trap's compiler file name can be turned into a workspace path. Immutable.
/// </summary>
/// <param name="Name">The project name (<c>x_blink</c>, or a root file's name without extension).</param>
/// <param name="Folder">The project folder (workspace-relative, <c>""</c> for a root file).</param>
/// <param name="Files">The project's file names (bare, as the compiler names them).</param>
/// <param name="Image">The image the workbench loaded (the very array): it describes a computer only while that computer runs this array.</param>
public sealed record LoadedProgram(string Name, string Folder, IReadOnlyCollection<string> Files, byte[]? Image)
{
    /// <summary>The workspace path of a compiler file name of this project, or null when it is not one of its files.</summary>
    public string? PathOf(string? file)
    {
        if (string.IsNullOrEmpty(file)) return null;
        foreach (string f in Files)
        {
            if (!string.Equals(f, file, StringComparison.Ordinal)) continue;
            return Folder.Length == 0 ? f : Folder + "/" + f;
        }
        return null;
    }
}

/// <summary>
/// What the live feeds read (plan §6.7): the program log by version and the program's state. Every
/// member is called from the feed's timer thread and must be thread-safe; <see cref="ReadState"/>
/// may cost something on the game's side (it asks the computer to publish a snapshot), so the feed
/// calls it only while a client subscribes to <c>state</c>. The in-game implementation is
/// <see cref="HostComputerLive"/>; tests use fakes.
/// </summary>
public interface ILiveSource
{
    /// <summary>The program log's version: it grows with every line added (and when the log is cleared).</summary>
    long LogVersion { get; }

    /// <summary>Copies the last <paramref name="count"/> log lines, formatted as the game shows them, into <paramref name="into"/> (cleared first); returns the version they belong to.</summary>
    long CopyLog(int count, List<string> into);

    /// <summary>The program's state now.</summary>
    LiveState ReadState();
}
