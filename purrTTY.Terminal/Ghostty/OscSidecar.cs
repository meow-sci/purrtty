using System.Text;

namespace PurrTTY.Terminal.Ghostty;

/// <summary>
/// A managed scanner that tees the raw terminal output stream and surfaces the
/// two OSC effects libghostty does not expose as engine callbacks: OSC 52
/// clipboard and OSC 1 icon-name. Title (OSC 0/2) is handled by the engine's
/// title callback, so it is intentionally ignored here.
///
/// This frames OSC sequences (<c>ESC ]</c> … <c>BEL</c>/<c>ST</c>) itself and
/// parses the payload directly — simpler and sufficient for these two effects
/// than round-tripping through the engine's OSC parser, which does not expose
/// clipboard payload extraction.
/// </summary>
internal sealed class OscSidecar
{
    private const int MaxPayload = 64 * 1024;
    private const byte Esc = 0x1B;
    private const byte Bel = 0x07;

    private enum State
    {
        Ground,
        Esc,
        Osc,
        OscEsc,
    }

    private State _state = State.Ground;
    private readonly List<byte> _payload = new(256);

    public event Action<string>? IconNameChanged;
    public event Action<ClipboardRequest>? ClipboardRequested;

    public void Feed(ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            FeedByte(b);
        }
    }

    public void Reset()
    {
        _state = State.Ground;
        _payload.Clear();
    }

    private void FeedByte(byte b)
    {
        switch (_state)
        {
            case State.Ground:
                if (b == Esc)
                {
                    _state = State.Esc;
                }
                break;

            case State.Esc:
                if (b == (byte)']')
                {
                    _state = State.Osc;
                    _payload.Clear();
                }
                else
                {
                    _state = b == Esc ? State.Esc : State.Ground;
                }
                break;

            case State.Osc:
                if (b == Bel)
                {
                    Dispatch();
                    _state = State.Ground;
                }
                else if (b == Esc)
                {
                    _state = State.OscEsc;
                }
                else if (_payload.Count < MaxPayload)
                {
                    _payload.Add(b);
                }
                break;

            case State.OscEsc:
                if (b == (byte)'\\')
                {
                    // ESC \ = ST terminator
                    Dispatch();
                    _state = State.Ground;
                }
                else
                {
                    // Not a string terminator; abandon this OSC.
                    _payload.Clear();
                    _state = b == Esc ? State.Esc : State.Ground;
                }
                break;
        }
    }

    private void Dispatch()
    {
        if (_payload.Count == 0)
        {
            return;
        }

        var text = Encoding.UTF8.GetString(_payload.ToArray());
        _payload.Clear();

        int firstSep = text.IndexOf(';');
        var command = firstSep < 0 ? text : text[..firstSep];

        switch (command)
        {
            case "1":
                // OSC 1 ; <icon name>
                if (firstSep >= 0)
                {
                    IconNameChanged?.Invoke(text[(firstSep + 1)..]);
                }
                break;

            case "52":
                DispatchClipboard(text, firstSep);
                break;
        }
    }

    private void DispatchClipboard(string text, int firstSep)
    {
        // OSC 52 ; <target(s)> ; <base64 | ?>
        if (firstSep < 0)
        {
            return;
        }

        int secondSep = text.IndexOf(';', firstSep + 1);
        if (secondSep < 0)
        {
            return;
        }

        var target = text[(firstSep + 1)..secondSep];
        var data = text[(secondSep + 1)..];

        if (target.Length == 0)
        {
            target = "c";
        }

        if (data == "?")
        {
            // Clipboard read request (paste-from-clipboard). Text == null signals a query.
            ClipboardRequested?.Invoke(new ClipboardRequest { Target = target, Text = null });
            return;
        }

        string? decoded = TryDecodeBase64(data);
        if (decoded is not null)
        {
            ClipboardRequested?.Invoke(new ClipboardRequest { Target = target, Text = decoded });
        }
    }

    private static string? TryDecodeBase64(string data)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(data));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
