namespace purrTTY.Kfs.Testing;

/// <summary>Severity of a <see cref="BuildDiagnostic"/>.</summary>
public enum BuildSeverity
{
    /// <summary>The build failed.</summary>
    Error,
    /// <summary>The build succeeded with a warning.</summary>
    Warning,
}

/// <summary>A compiler or assembler diagnostic in a shape with no game types.</summary>
/// <param name="File">The file (a system header appears as <c>&lt;kfs.h&gt;</c>).</param>
/// <param name="Line">1-based line; 0 for no position.</param>
/// <param name="Column">1-based column in UTF-16 code units; 0 for no position.</param>
/// <param name="Code">The diagnostic code (<c>KC-E016</c>, <c>KA-E003</c>, …).</param>
/// <param name="Severity">Error or warning.</param>
/// <param name="Message">The message.</param>
/// <param name="Hint">The hint, if any.</param>
public sealed record BuildDiagnostic(string File, int Line, int Column, string Code, BuildSeverity Severity, string Message, string? Hint)
{
    /// <summary><c>file:line:col: error CODE: message</c> (and the hint on its own line), as the game prints it.</summary>
    public override string ToString() =>
        $"{File}:{Line}:{Column}: {(Severity == BuildSeverity.Error ? "error" : "warning")} {Code}: {Message}" +
        (Hint is null ? "" : "\n  hint: " + Hint);
}

/// <summary>The outcome of a kC compile or a kASM assembly.</summary>
/// <param name="Ok">True when an image was produced.</param>
/// <param name="Image">The image, or null.</param>
/// <param name="Diagnostics">Every diagnostic, in the compiler's order.</param>
public sealed record BuildResult(bool Ok, byte[]? Image, IReadOnlyList<BuildDiagnostic> Diagnostics)
{
    /// <summary>True when a diagnostic is an error.</summary>
    public bool HasErrors => Diagnostics.Any(d => d.Severity == BuildSeverity.Error);

    /// <summary>Every diagnostic, one per line (for failure messages).</summary>
    public string Text => string.Join("\n", Diagnostics);
}
