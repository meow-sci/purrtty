// No game types in this file (see Program.cs).

namespace purrTTY.Kfs.Cli;

/// <summary>The tool's help texts (<c>\n</c> line ends; written line by line).</summary>
internal static class HelpText
{
    private const string Ksa =
        "  --ksa <dir>          the folder that holds KSA.dll (default: the KSA_DLL_DIR environment variable)";

    private const string Vehicle =
        """
          --lights <n>         simulated lights (default 0)
          --deployables <n>    simulated deployables, 2 s each (default 0)
          --actuators <n>      simulated actuators, 1 s each (default 0)
          --thrusters <n>      thrusters in the vehicle layout (default 1, at least 1)
          --engines <n>        engines in the vehicle layout (default 0)
          --gimbals <n>        gimbals in the vehicle layout (default 0)
        """;

    private const string Project =
        """
        The project is chosen as the game's workbench chooses it: a file in a folder builds every
        .c, .h, .kasm and .kinc file directly in that folder (except vehicle.h and vehicle.kinc); a
        root-level .c builds with the files its #include "..." lines reach; a root-level .kasm builds
        alone (its .include "..." files are read from the workspace); a root-level header is no
        project. <file> is relative to the workspace (default: the current directory). Nothing is
        written to the workspace.
        """;

    public const string General =
        $"""
        purr-kfs - check and run kFS programs (the flight computer of Kitten Space Agency) outside the
        game, with the game's own compiler, assembler and VM plus purrTTY's x_ services.

        Usage: purr-kfs <command> [options]

        Commands:
          check <file>         build the file's project as the game's workbench would; print diagnostics
          run <file>           build, load onto a simulated vehicle and run; print log, requests, state
          lsp                  serve the kC/kASM language server (--stdio, or --listen 127.0.0.1:<port>)
          header               print <purrtty.h>
          kinc                 print purrtty.kinc
          services             print the service catalogue as a table

        Options:
        {Ksa}
          --help, -h           show help (purr-kfs <command> --help for one command)
          --version            show the version

        check, run and lsp need the game's own assemblies: pass --ksa <dir> or set KSA_DLL_DIR.
        Exit codes: 0 success, 1 build errors or a trapped program, 2 usage or environment problem.
        """;

    public const string Check =
        $"""
        Usage: purr-kfs check <file> [options]

        Builds <file>'s project exactly as the game's workbench would and prints each diagnostic as
          file:line:col: severity code: message
            hint: ...
        followed by a one-line summary. Exit code 0 when there is no error (warnings allowed), 1 on
        errors, 2 on a usage or environment problem.

        {Project}

        Options:
          --workspace <dir>    the kFS workspace folder (default: the current directory)
          --json               print one JSON document instead (diagnostics, sizes, per-task WCET)
        {Vehicle}
        {Ksa}
        """;

    public const string Run =
        $"""
        Usage: purr-kfs run <file> [options]

        Builds <file>'s project (as purr-kfs check), loads it onto a simulated vehicle, runs it for a
        simulated duration and prints what is asked for. Time is simulated: a run never waits. The
        stock flight computer's requests (stage, throttle, holds, burns) are recorded, not flown.
        Exit code 1 when the build fails or the program traps (or the host faults it).

        {Project}

        Options:
          --workspace <dir>    the kFS workspace folder (default: the current directory)
          --for <duration>     simulated time to run: 10s, 500ms, 2m (default 10s)
          --frame <hz>         frames per simulated second (default 60)
          --log                print the program log (kfs_logf lines and host events)
          --requests           print the decoded stock requests
          --state              print the final state of the machine, every light and animation
                               (with none of --log, --requests, --state: all three)
        {Vehicle}
        {Ksa}
        """;

    public const string Lsp =
        $"""
        Usage: purr-kfs lsp (--stdio | --listen <host>:<port>) [options]

        Serves the kFS language server (kC .c/.h, kASM .kasm/.kinc) over the game's own compiler and
        assembler with purrTTY's x_ services: diagnostics for every file of each open project, the
        unsaved buffers overlaid. Projects are chosen as for purr-kfs check.

        The build target is, in this order: the layout flags below; the layout recorded in the
        workspace's vehicle.h (written by the game's kfs build; its fingerprint is verified, and the
        file is watched); else the default layout of purr-kfs check (1 thruster, bench processor).

        Options:
          --stdio              talk LSP on stdin/stdout (nothing else is written to stdout)
          --listen <host>:<port>
                               accept clients on a loopback TCP port (0 picks one; prints
                               "LISTENING <port>" on stdout); exit and Ctrl+C close a client / the server.
                               Always token-protected: without a token below, one is made and printed
                               as a second line "TOKEN <t>"
          --token <t>          clients must send initializationOptions.token = <t> (visible in ps: prefer
                               --token-file or PURR_KFS_LSP_TOKEN)
          --token-file <file>  the token is the first line of <file>
                               (or set PURR_KFS_LSP_TOKEN in the environment)
          --workspace <dir>    the kFS workspace folder (default: the current directory)
          --thrusters <n>, --engines <n>, --gimbals <n>
                               an explicit vehicle layout (default: from vehicle.h)
          --verbose            log every message and build to stderr
        {Ksa}

        Logs go to stderr.
        """;

    public const string Header =
        """
        Usage: purr-kfs header

        Prints <purrtty.h>, the kC header of purrTTY's x_ services (CRLF line ends, as the game's own headers).
        """;

    public const string Kinc =
        """
        Usage: purr-kfs kinc

        Prints purrtty.kinc, the kASM constants of purrTTY's x_ services (.include "purrtty.kinc").
        """;

    public const string Services =
        """
        Usage: purr-kfs services [--markdown]

        Prints the service catalogue: id, prototype, kind, cost class and summary of each service.
          --markdown           the full markdown reference instead (every service and constant)
        """;
}
