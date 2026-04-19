using caTTY.Core.Tracing;
using caTTY.Core.Types;
using Microsoft.Extensions.Logging;

namespace caTTY.Core.Terminal.ParserHandlers;

/// <summary>
///     Handles SGR (Select Graphic Rendition) sequence processing.
/// </summary>
internal class SgrHandler
{
    private readonly ILogger _logger;
    private readonly TerminalEmulator _terminal;

    public SgrHandler(TerminalEmulator terminal, ILogger logger)
    {
        _terminal = terminal;
        _logger = logger;
    }

    /// <summary>
    ///     Handles SGR sequence processing and applies attributes to the terminal.
    /// </summary>
    /// <param name="sequence">The SGR sequence to process</param>
    public void HandleSgrSequence(SgrSequence sequence)
    {
        // Apply SGR messages to current attributes
        var currentAttributes = _terminal.AttributeManager.CurrentAttributes;
        var newAttributes = _terminal.AttributeManager.ApplyAttributes(currentAttributes, sequence.Messages);
        _terminal.AttributeManager.CurrentAttributes = newAttributes;

        // Sync with terminal state for compatibility
        _terminal.State.CurrentSgrState = newAttributes;

        // Trace SGR sequence with complete attribute change information
        TraceSgrSequence(sequence, currentAttributes, newAttributes);

        _logger.LogDebug("Applied SGR sequence: {Raw} - {MessageCount} messages", sequence.Raw, sequence.Messages.Length);
    }

    /// <summary>
    ///     Traces SGR sequence with complete attribute change information.
    /// </summary>
    /// <param name="sequence">The SGR sequence that was processed</param>
    /// <param name="beforeAttributes">Attributes before applying the sequence</param>
    /// <param name="afterAttributes">Attributes after applying the sequence</param>
    private void TraceSgrSequence(SgrSequence sequence, SgrAttributes beforeAttributes, SgrAttributes afterAttributes)
    {
        if (!TerminalTracer.Enabled)
            return;

        // Get cursor position for tracing context
        int? row = _terminal.Cursor?.Row;
        int? col = _terminal.Cursor?.Col;

        // Create a detailed trace message that includes both raw and formatted sequence with attribute changes
        var formattedSequence = $"\\x1b[{ExtractSgrParameters(sequence.Raw)}m";
        var traceMessage = $"{formattedSequence} - ";

        // Add information about what changed
        var changes = new List<string>();

        if (beforeAttributes.Bold != afterAttributes.Bold)
            changes.Add($"bold:{beforeAttributes.Bold}->{afterAttributes.Bold}");
        if (beforeAttributes.Italic != afterAttributes.Italic)
            changes.Add($"italic:{beforeAttributes.Italic}->{afterAttributes.Italic}");
        if (beforeAttributes.Underline != afterAttributes.Underline)
            changes.Add($"underline:{beforeAttributes.Underline}->{afterAttributes.Underline}");
        if (beforeAttributes.Strikethrough != afterAttributes.Strikethrough)
            changes.Add($"strikethrough:{beforeAttributes.Strikethrough}->{afterAttributes.Strikethrough}");
        if (beforeAttributes.Inverse != afterAttributes.Inverse)
            changes.Add($"inverse:{beforeAttributes.Inverse}->{afterAttributes.Inverse}");
        if (beforeAttributes.Hidden != afterAttributes.Hidden)
            changes.Add($"hidden:{beforeAttributes.Hidden}->{afterAttributes.Hidden}");
        if (beforeAttributes.Blink != afterAttributes.Blink)
            changes.Add($"blink:{beforeAttributes.Blink}->{afterAttributes.Blink}");
        if (beforeAttributes.Faint != afterAttributes.Faint)
            changes.Add($"faint:{beforeAttributes.Faint}->{afterAttributes.Faint}");

        // Check color changes
        if (!Equals(beforeAttributes.ForegroundColor, afterAttributes.ForegroundColor))
            changes.Add($"fg:{FormatColor(beforeAttributes.ForegroundColor)}->{FormatColor(afterAttributes.ForegroundColor)}");
        if (!Equals(beforeAttributes.BackgroundColor, afterAttributes.BackgroundColor))
            changes.Add($"bg:{FormatColor(beforeAttributes.BackgroundColor)}->{FormatColor(afterAttributes.BackgroundColor)}");
        if (!Equals(beforeAttributes.UnderlineColor, afterAttributes.UnderlineColor))
            changes.Add($"ul:{FormatColor(beforeAttributes.UnderlineColor)}->{FormatColor(afterAttributes.UnderlineColor)}");

        // Check underline style changes
        if (beforeAttributes.UnderlineStyle != afterAttributes.UnderlineStyle)
            changes.Add($"ul-style:{beforeAttributes.UnderlineStyle}->{afterAttributes.UnderlineStyle}");

        // Check font changes
        if (beforeAttributes.Font != afterAttributes.Font)
            changes.Add($"font:{beforeAttributes.Font}->{afterAttributes.Font}");

        // TODO: FIXME: tracing cleanup 
        traceMessage += string.Join(", ", changes);
        traceMessage += "";

        // Trace the SGR sequence with Output direction (program output to terminal)
        // Use TerminalTracer.TraceEscape with SGR type to preserve detailed attribute information
        TerminalTracer.TraceEscape(traceMessage, TraceDirection.Output, row, col, "SGR");
    }

    /// <summary>
    ///     Formats a color for tracing display.
    /// </summary>
    /// <param name="color">The color to format</param>
    /// <returns>A string representation of the color</returns>
    private static string FormatColor(Color? color)
    {
        if (!color.HasValue)
            return "null";

        var c = color.Value;
        return c.Type switch
        {
            ColorType.Named => c.NamedColor.ToString(),
            ColorType.Indexed => $"#{c.Index}",
            ColorType.Rgb => $"rgb({c.Red},{c.Green},{c.Blue})",
            _ => "unknown"
        };
    }

    /// <summary>
    ///     Extracts SGR parameters from a raw SGR sequence for tracing.
    /// </summary>
    /// <param name="rawSequence">The raw SGR sequence (e.g., "ESC[1;32m")</param>
    /// <returns>The parameter string (e.g., "1;32")</returns>
    private static string ExtractSgrParameters(string rawSequence)
    {
        // SGR sequences have the format ESC[<parameters>m
        // Extract the parameters between '[' and 'm'
        if (string.IsNullOrEmpty(rawSequence))
            return "0"; // Default to reset

        var startIndex = rawSequence.IndexOf('[');
        var endIndex = rawSequence.LastIndexOf('m');

        if (startIndex >= 0 && endIndex > startIndex)
        {
            var parameters = rawSequence.Substring(startIndex + 1, endIndex - startIndex - 1);
            return string.IsNullOrEmpty(parameters) ? "0" : parameters;
        }

        return "0"; // Default to reset if parsing fails
    }
}
