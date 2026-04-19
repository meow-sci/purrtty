using System;
using System.Text;
using caTTY.Core.Parsing;

namespace caTTY.Core.Tracing;

/// <summary>
/// Example integration showing how to add tracing to terminal parsing operations.
/// This demonstrates the tracing API usage patterns.
/// </summary>
public static class TracingIntegration
{
  /// <summary>
  /// Example: Trace raw input bytes before parsing.
  /// Call this from your parser's PushBytes method.
  /// </summary>
  /// <param name="data">Raw input bytes</param>
  /// <param name="row">Current cursor row position (0-based, nullable)</param>
  /// <param name="col">Current cursor column position (0-based, nullable)</param>
  public static void TraceInputBytes(ReadOnlySpan<byte> data, int? row = null, int? col = null)
  {
    if (!TerminalTracer.Enabled || data.IsEmpty)
      return;

    // Separate printable characters from escape sequences for better tracing
    var printableBuffer = new StringBuilder();
    var escapeBuffer = new StringBuilder();
    bool inEscape = false;

    foreach (var b in data)
    {
      if (b == 0x1B) // ESC
      {
        // Flush any accumulated printable text
        if (printableBuffer.Length > 0)
        {
          TerminalTracer.TracePrintable(printableBuffer.ToString(), TraceDirection.Output, row, col, "printable");
          printableBuffer.Clear();
        }
        inEscape = true;
        escapeBuffer.Append("ESC");
      }
      else if (inEscape)
      {
        if (b >= 32 && b <= 126) // Printable ASCII
        {
          escapeBuffer.Append((char)b);
        }
        else
        {
          escapeBuffer.Append($"\\x{b:X2}");
        }

        // Simple heuristic: end escape on certain characters
        if (IsEscapeTerminator(b))
        {
          TerminalTracer.TraceEscape(escapeBuffer.ToString(), TraceDirection.Output, row, col);
          escapeBuffer.Clear();
          inEscape = false;
        }
      }
      else if (b >= 32 && b <= 126) // Printable ASCII
      {
        printableBuffer.Append((char)b);
      }
      else if (b <= 31 || b == 0x7F) // Control characters
      {
        // Flush printable text before control char
        if (printableBuffer.Length > 0)
        {
          TerminalTracer.TracePrintable(printableBuffer.ToString(), TraceDirection.Output, row, col, "printable");
          printableBuffer.Clear();
        }
        TraceHelper.TraceControlChar(b, TraceDirection.Output, row, col);
      }
    }

    // Flush any remaining buffers
    if (printableBuffer.Length > 0)
    {
      TerminalTracer.TracePrintable(printableBuffer.ToString(), TraceDirection.Output, row, col, "printable");
    }
    if (escapeBuffer.Length > 0)
    {
      TerminalTracer.TraceEscape(escapeBuffer.ToString(), TraceDirection.Output, row, col);
    }
  }

  /// <summary>
  /// Example: Trace parsed CSI sequences.
  /// Call this from your CSI handler methods.
  /// </summary>
  /// <param name="command">CSI command character</param>
  /// <param name="parameters">Parsed parameters</param>
  /// <param name="prefix">Optional prefix character</param>
  /// <param name="row">Current cursor row position (0-based, nullable)</param>
  /// <param name="col">Current cursor column position (0-based, nullable)</param>
  public static void TraceParsedCsi(char command, int[]? parameters = null, char? prefix = null, int? row = null, int? col = null)
  {
    if (!TerminalTracer.Enabled)
      return;

    var paramStr = parameters != null && parameters.Length > 0
        ? string.Join(";", parameters)
        : null;

    TraceHelper.TraceCsiSequence(command, paramStr, prefix, TraceDirection.Output, row, col);
  }

  /// <summary>
  /// Example: Trace parsed OSC sequences.
  /// Call this from your OSC handler methods.
  /// </summary>
  /// <param name="command">OSC command number</param>
  /// <param name="data">OSC data payload</param>
  /// <param name="row">Current cursor row position (0-based, nullable)</param>
  /// <param name="col">Current cursor column position (0-based, nullable)</param>
  public static void TraceParsedOsc(int command, string? data = null, int? row = null, int? col = null)
  {
    if (!TerminalTracer.Enabled)
      return;

    TraceHelper.TraceOscSequence(command, data, TraceDirection.Output, row, col);
  }

  /// <summary>
  /// Example: Trace character output to screen buffer.
  /// Call this when writing characters to the terminal screen.
  /// </summary>
  /// <param name="character">Character being written</param>
  /// <param name="row">Screen row</param>
  /// <param name="col">Screen column</param>
  public static void TraceCharacterOutput(char character, int row, int col)
  {
    if (!TerminalTracer.Enabled)
      return;

    TerminalTracer.TracePrintable($"[{row},{col}] '{character}'", TraceDirection.Output, row, col, "printable");
  }

  /// <summary>
  /// Example: Trace UTF-8 decoding results.
  /// Call this from your UTF-8 decoder.
  /// </summary>
  /// <param name="codePoint">Decoded Unicode code point</param>
  /// <param name="sourceBytes">Original UTF-8 bytes</param>
  /// <param name="row">Current cursor row position (0-based, nullable)</param>
  /// <param name="col">Current cursor column position (0-based, nullable)</param>
  public static void TraceUtf8Decode(int codePoint, ReadOnlySpan<byte> sourceBytes, int? row = null, int? col = null)
  {
    if (!TerminalTracer.Enabled)
      return;

    var character = char.ConvertFromUtf32(codePoint);
    var bytesHex = string.Join(" ", sourceBytes.ToArray().Select(b => $"{b:X2}"));

    TerminalTracer.Trace($"UTF8: {bytesHex}", $"'{character}' (U+{codePoint:X4})", TraceDirection.Output, row, col);
  }

  /// <summary>
  /// Simple heuristic to detect escape sequence terminators.
  /// This is a basic implementation - real parsers use proper state machines.
  /// </summary>
  private static bool IsEscapeTerminator(byte b)
  {
    // Common CSI terminators
    if (b >= 0x40 && b <= 0x7E) // @ A-Z [ \ ] ^ _ ` a-z { | } ~
      return true;

    // BEL (for OSC sequences)
    if (b == 0x07)
      return true;

    return false;
  }
}

/// <summary>
/// Application shutdown hook to ensure tracing resources are cleaned up.
/// Register this with your application's shutdown process.
/// </summary>
public static class TracingShutdown
{
  private static bool _registered = false;

  /// <summary>
  /// Register the tracing shutdown hook with the application domain.
  /// Call this once during application startup.
  /// </summary>
  public static void RegisterShutdownHook()
  {
    if (_registered)
      return;

    AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    AppDomain.CurrentDomain.DomainUnload += OnDomainUnload;

    // For console applications
    Console.CancelKeyPress += OnCancelKeyPress;

    _registered = true;
  }

  private static void OnProcessExit(object? sender, EventArgs e)
  {
    TerminalTracer.Shutdown();
  }

  private static void OnDomainUnload(object? sender, EventArgs e)
  {
    TerminalTracer.Shutdown();
  }

  private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
  {
    TerminalTracer.Shutdown();
  }
}