using System.Runtime.InteropServices;
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
    private const byte Can = 0x18;
    private const byte Sub = 0x1A;

    private enum State
    {
        Ground,
        Esc,
        Osc,
        OscEsc,
    }

    private State _state = State.Ground;
    private readonly List<byte> _payload = new(256);

    // Set when payload bytes were dropped at MaxPayload. A truncated sequence
    // is never dispatched: a clipped OSC 52 whose cut lands on a base64
    // 4-char boundary would otherwise decode cleanly and silently replace the
    // user's clipboard with a prefix of what the app sent.
    private bool _overflowed;

    public event Action<string>? IconNameChanged;
    public event Action<ClipboardRequest>? ClipboardRequested;

    public void Feed(ReadOnlySpan<byte> data)
    {
        int i = 0;
        while (i < data.Length)
        {
            // Ground is the overwhelmingly common state for bulk output;
            // vectorized IndexOf skips straight to the next ESC instead of
            // walking every byte through the state machine.
            if (_state == State.Ground)
            {
                int esc = data[i..].IndexOf(Esc);
                if (esc < 0)
                {
                    return;
                }

                i += esc;
                _state = State.Esc;
                i++;
                continue;
            }

            FeedByte(data[i]);
            i++;
        }
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
                    _overflowed = false;
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
                else if (b is Can or Sub)
                {
                    // CAN/SUB abort the control string (DEC/xterm behavior).
                    // The inbox-overflow heal sequence (CAN + ST, gotcha 18)
                    // relies on this: a partial OSC interrupted by a drop must
                    // be abandoned, never dispatched with stray bytes inside.
                    AbandonSequence();
                }
                else if (_payload.Count < MaxPayload)
                {
                    _payload.Add(b);
                }
                else
                {
                    _overflowed = true;
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
                    AbandonSequence();
                    _state = b == Esc ? State.Esc : State.Ground;
                }
                break;
        }
    }

    private void AbandonSequence()
    {
        _payload.Clear();
        _overflowed = false;
        _state = State.Ground;
    }

    private void Dispatch()
    {
        if (_overflowed)
        {
            // Truncated payload — drop the whole sequence (see _overflowed).
            _payload.Clear();
            _overflowed = false;
            return;
        }

        var payload = CollectionsMarshal.AsSpan(_payload);

        // Decide interest in raw bytes before materializing any string: shell
        // prompts emit ignored OSCs (0/2 title, 7 pwd, 133 prompt marks)
        // continuously, and this runs on the tick thread.
        bool isIcon = payload.Length >= 2 && payload[0] == (byte)'1' && payload[1] == (byte)';';
        bool isClipboard = payload.Length >= 3
            && payload[0] == (byte)'5' && payload[1] == (byte)'2' && payload[2] == (byte)';';
        if (!isIcon && !isClipboard)
        {
            _payload.Clear();
            return;
        }

        var text = Encoding.UTF8.GetString(payload);
        _payload.Clear();

        if (isIcon)
        {
            // OSC 1 ; <icon name>
            IconNameChanged?.Invoke(text[2..]);
        }
        else
        {
            DispatchClipboard(text, firstSep: 2);
        }
    }

    private void DispatchClipboard(string text, int firstSep)
    {
        // OSC 52 ; <target(s)> ; <base64 | ?>
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
