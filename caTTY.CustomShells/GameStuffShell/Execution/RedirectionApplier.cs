using caTTY.CustomShells.GameStuffShell.Parsing;

namespace caTTY.CustomShells.GameStuffShell.Execution;

/// <summary>
/// Applies redirections to file descriptors, implementing bash-like semantics.
/// </summary>
public static class RedirectionApplier
{
    /// <summary>
    /// Applies a list of redirections to the base stream set.
    /// Redirections are applied left-to-right.
    /// </summary>
    /// <param name="redirections">The redirections to apply.</param>
    /// <param name="baseStreams">The base stream set (stdin, stdout, stderr).</param>
    /// <param name="context">The execution context.</param>
    /// <param name="errorWriter">Optional error writer for redirection errors (if null, uses baseStreams.Stderr).</param>
    /// <returns>The modified stream set, or null if a redirection error occurred.</returns>
    public static async Task<StreamSet?> ApplyRedirectionsAsync(
        IReadOnlyList<RedirectionNode> redirections,
        StreamSet baseStreams,
        ExecContext context,
        IStreamWriter? errorWriter = null)
    {
        if (redirections.Count == 0)
        {
            return baseStreams;
        }

        errorWriter ??= baseStreams.Stderr;

        // Build FD table: 0=stdin, 1=stdout, 2=stderr
        var fds = new Dictionary<int, object>
        {
            [0] = baseStreams.Stdin,
            [1] = baseStreams.Stdout,
            [2] = baseStreams.Stderr
        };

        foreach (var redir in redirections)
        {
            var result = await ApplySingleRedirectionAsync(redir, fds, context, errorWriter);
            if (!result)
            {
                return null; // Error already written to errorWriter
            }
        }

        return new StreamSet(
            stdin: (IStreamReader)fds[0],
            stdout: (IStreamWriter)fds[1],
            stderr: (IStreamWriter)fds[2]
        );
    }

    private static async Task<bool> ApplySingleRedirectionAsync(
        RedirectionNode redir,
        Dictionary<int, object> fds,
        ExecContext context,
        IStreamWriter errorWriter)
    {
        // Determine the FD number for this redirection
        int fd;
        if (redir.IoNumber.HasValue)
        {
            fd = redir.IoNumber.Value;
        }
        else
        {
            // Default FD numbers
            fd = redir.Kind switch
            {
                RedirectionKind.Out => 1,
                RedirectionKind.OutAppend => 1,
                RedirectionKind.In => 0,
                RedirectionKind.DupOut => 1,
                RedirectionKind.DupIn => 0,
                _ => throw new InvalidOperationException($"Unknown redirection kind: {redir.Kind}")
            };
        }

        // Validate FD number (only 0, 1, 2 are supported)
        if (fd < 0 || fd > 2)
        {
            await errorWriter.WriteAsync($"bash: {fd}: Bad file descriptor\n", CancellationToken.None);
            return false;
        }

        switch (redir.Kind)
        {
            case RedirectionKind.Out:
            case RedirectionKind.OutAppend:
                return await ApplyFileRedirectionAsync(redir, fd, fds, errorWriter, isInput: false);

            case RedirectionKind.In:
                return await ApplyFileRedirectionAsync(redir, fd, fds, errorWriter, isInput: true);

            case RedirectionKind.DupOut:
            case RedirectionKind.DupIn:
                return await ApplyDuplicationAsync(redir, fd, fds, errorWriter);

            default:
                throw new InvalidOperationException($"Unknown redirection kind: {redir.Kind}");
        }
    }

    private static async Task<bool> ApplyFileRedirectionAsync(
        RedirectionNode redir,
        int fd,
        Dictionary<int, object> fds,
        IStreamWriter errorWriter,
        bool isInput)
    {
        // We only support /dev/null as a file target
        if (redir.Target != "/dev/null")
        {
            await errorWriter.WriteAsync($"bash: {redir.Target}: only /dev/null is supported\n", CancellationToken.None);
            return false;
        }

        if (isInput)
        {
            // Input from /dev/null: empty reader
            fds[fd] = EmptyStreamReader.Instance;
        }
        else
        {
            // Output to /dev/null: null sink
            fds[fd] = NullStreamWriter.Instance;
        }

        return true;
    }

    private static async Task<bool> ApplyDuplicationAsync(
        RedirectionNode redir,
        int sourceFd,
        Dictionary<int, object> fds,
        IStreamWriter errorWriter)
    {
        // Parse target FD from the target string
        if (!int.TryParse(redir.Target, out var targetFd))
        {
            await errorWriter.WriteAsync($"bash: {redir.Target}: ambiguous redirect\n", CancellationToken.None);
            return false;
        }

        // Validate target FD
        if (targetFd < 0 || targetFd > 2)
        {
            await errorWriter.WriteAsync($"bash: {targetFd}: Bad file descriptor\n", CancellationToken.None);
            return false;
        }

        // Duplicate: make sourceFd point to the *current* targetFd object
        // This is critical for bash semantics like `2>&1 1>/dev/null`
        fds[sourceFd] = fds[targetFd];

        return true;
    }
}
