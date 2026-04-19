using System;
using System.Text;

namespace caTTY.Core.Tracing;

/// <summary>
/// Helper methods for terminal tracing with convenient overloads.
/// </summary>
public static class TraceHelper
{
  /// <summary>
  /// Trace raw bytes as an escape sequence (for debugging parser input).
  /// </summary>
  /// <param name="bytes">Raw bytes to trace as escape sequence</param>
  /// <param name="direction">The direction of data flow (default: Output)</param>
  /// <param name="row">The cursor row position (0-based, nullable)</param>
  /// <param name="col">The cursor column position (0-based, nullable)</param>
  public static void TraceEscapeBytes(ReadOnlySpan<byte> bytes, TraceDirection direction = TraceDirection.Output, int? row = null, int? col = null)
  {
    if (bytes.IsEmpty)
      return;

    var sb = new StringBuilder();
    foreach (var b in bytes)
    {
      if (b >= 32 && b <= 126) // Printable ASCII
      {
        sb.Append((char)b);
      }
      else
      {
        sb.Append($"\\x{b:X2}");
      }
    }

    TerminalTracer.TraceEscape(sb.ToString(), direction, row, col);
  }

  /// <summary>
  /// Trace a single character as printable text.
  /// </summary>
  /// <param name="character">Character to trace</param>
  /// <param name="direction">The direction of data flow (default: Output)</param>
  /// <param name="row">The cursor row position (0-based, nullable)</param>
  /// <param name="col">The cursor column position (0-based, nullable)</param>
  public static void TracePrintableChar(char character, TraceDirection direction = TraceDirection.Output, int? row = null, int? col = null)
  {
    TerminalTracer.TracePrintable(character.ToString(), direction, row, col, "printable");
  }

  /// <summary>
  /// Trace a control character with its name for better readability.
  /// </summary>
  /// <param name="controlByte">Control character byte (0-31)</param>
  /// <param name="direction">The direction of data flow (default: Output)</param>
  /// <param name="row">The cursor row position (0-based, nullable)</param>
  /// <param name="col">The cursor column position (0-based, nullable)</param>
  public static void TraceControlChar(byte controlByte, TraceDirection direction = TraceDirection.Output, int? row = null, int? col = null)
  {
    // Format control characters as \x{XX} hexadecimal notation
    var controlSequence = $"\\x{controlByte:X2}";

    TerminalTracer.TraceEscape(controlSequence, direction, row, col, "control");
  }

  /// <summary>
  /// Trace a CSI sequence with parameters for better readability.
  /// </summary>
  /// <param name="command">CSI command character</param>
  /// <param name="parameters">CSI parameters (can be null)</param>
  /// <param name="prefix">CSI prefix character (can be null)</param>
  /// <param name="direction">The direction of data flow (default: Output)</param>
  /// <param name="row">The cursor row position (0-based, nullable)</param>
  /// <param name="col">The cursor column position (0-based, nullable)</param>
  public static void TraceCsiSequence(char command, string? parameters = null, char? prefix = null, TraceDirection direction = TraceDirection.Output, int? row = null, int? col = null)
  {
    // Format as human-readable escape sequence: \x1b[parameters;command
    var sequence = new StringBuilder("\\x1b[");

    if (prefix.HasValue)
      sequence.Append(prefix);

    if (!string.IsNullOrEmpty(parameters))
      sequence.Append(parameters);

    sequence.Append(command);

    TerminalTracer.TraceEscape(sequence.ToString(), direction, row, col, "CSI");
  }

  /// <summary>
  /// Trace an OSC sequence with command and data.
  /// </summary>
  /// <param name="command">OSC command number</param>
  /// <param name="data">OSC data string</param>
  /// <param name="direction">The direction of data flow (default: Output)</param>
  /// <param name="row">The cursor row position (0-based, nullable)</param>
  /// <param name="col">The cursor column position (0-based, nullable)</param>
  public static void TraceOscSequence(int command, string? data = null, TraceDirection direction = TraceDirection.Output, int? row = null, int? col = null)
  {
    // Format as human-readable escape sequence: \x1b]command;data\x07
    var sequence = $"\\x1b]{command}";
    if (!string.IsNullOrEmpty(data))
      sequence += $";{data}";
    sequence += "\\x07"; // Show BEL terminator

    TerminalTracer.TraceEscape(sequence, direction, row, col, "OSC");
  }

  /// <summary>
  /// Trace an ESC sequence (non-CSI).
  /// </summary>
  /// <param name="sequence">The escape sequence characters after ESC</param>
  /// <param name="direction">The direction of data flow (default: Output)</param>
  /// <param name="row">The cursor row position (0-based, nullable)</param>
  /// <param name="col">The cursor column position (0-based, nullable)</param>
  public static void TraceEscSequence(string sequence, TraceDirection direction = TraceDirection.Output, int? row = null, int? col = null)
  {
    // Format as human-readable escape sequence: \x1b + sequence
    TerminalTracer.TraceEscape($"\\x1b{sequence}", direction, row, col, "ESC");
  }

  /// <summary>
  /// Trace a DCS sequence with command, parameters, and data.
  /// </summary>
  /// <param name="command">DCS command string</param>
  /// <param name="parameters">DCS parameters (can be null)</param>
  /// <param name="data">DCS data payload (can be null)</param>
  /// <param name="direction">The direction of data flow (default: Output)</param>
  /// <param name="row">The cursor row position (0-based, nullable)</param>
  /// <param name="col">The cursor column position (0-based, nullable)</param>
  public static void TraceDcsSequence(string command, string? parameters = null, string? data = null, TraceDirection direction = TraceDirection.Output, int? row = null, int? col = null)
  {
    // Format as human-readable escape sequence: \x1bPparameterscommanddata\x1b\\
    var sequence = new StringBuilder("\\x1bP");

    if (!string.IsNullOrEmpty(parameters))
      sequence.Append(parameters);

    sequence.Append(command);

    if (!string.IsNullOrEmpty(data))
      sequence.Append(data);

    sequence.Append("\\x1b\\\\"); // Show ST terminator

    TerminalTracer.TraceEscape(sequence.ToString(), direction, row, col, "DCS");
  }

  /// <summary>
  /// Trace UTF-8 decoded text as printable content.
  /// </summary>
  /// <param name="text">UTF-8 decoded text</param>
  /// <param name="direction">The direction of data flow (default: Output)</param>
  /// <param name="row">The cursor row position (0-based, nullable)</param>
  /// <param name="col">The cursor column position (0-based, nullable)</param>
  public static void TraceUtf8Text(string text, TraceDirection direction = TraceDirection.Output, int? row = null, int? col = null)
  {
    if (string.IsNullOrEmpty(text))
      return;

    TerminalTracer.TracePrintable(text, direction, row, col, "utf8");
  }

  /// <summary>
  /// Trace a wide character with width indication for better debugging.
  /// </summary>
  /// <param name="character">Wide character to trace</param>
  /// <param name="direction">The direction of data flow (default: Output)</param>
  /// <param name="row">The cursor row position (0-based, nullable)</param>
  /// <param name="col">The cursor column position (0-based, nullable)</param>
  public static void TraceWideCharacter(char character, TraceDirection direction = TraceDirection.Output, int? row = null, int? col = null)
  {
    TerminalTracer.TracePrintable($"{character} (wide)", direction, row, col, "wide");
  }

  /// <summary>
  /// Trace an SGR (Select Graphic Rendition) sequence with attribute information.
  /// </summary>
  /// <param name="attributes">SGR attribute parameters</param>
  /// <param name="direction">The direction of data flow (default: Output)</param>
  /// <param name="row">The cursor row position (0-based, nullable)</param>
  /// <param name="col">The cursor column position (0-based, nullable)</param>
  public static void TraceSgrSequence(string attributes, TraceDirection direction = TraceDirection.Output, int? row = null, int? col = null)
  {
    // Format as human-readable SGR sequence: \x1b[attributes;m
    var sequence = $"\\x1b[{attributes}m";
    TerminalTracer.TraceEscape(sequence, direction, row, col, "SGR");
  }

  /// <summary>
  /// Trace parser state transitions for debugging.
  /// </summary>
  /// <param name="fromState">Previous parser state</param>
  /// <param name="toState">New parser state</param>
  /// <param name="trigger">What triggered the transition</param>
  /// <param name="direction">The direction of data flow (default: Output)</param>
  /// <param name="row">The cursor row position (0-based, nullable)</param>
  /// <param name="col">The cursor column position (0-based, nullable)</param>
  public static void TraceStateTransition(string fromState, string toState, string trigger, TraceDirection direction = TraceDirection.Output, int? row = null, int? col = null)
  {
    TerminalTracer.TraceEscape($"STATE: {fromState} -> {toState} ({trigger})", direction, row, col);
  }
}
