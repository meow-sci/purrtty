using System.Text;

namespace caTTY.Core.Parsing.Csi;

/// <summary>
///     Tokenizes CSI sequence bytes into parameters, intermediate characters, and final byte.
///     Handles parameter byte parsing (0x30-0x3f), intermediate byte extraction (0x20-0x2f),
///     and parameter string parsing including private mode and prefix detection.
/// </summary>
public class CsiTokenizer
{
    /// <summary>
    ///     Result of CSI sequence tokenization.
    /// </summary>
    public class TokenizationResult
    {
        /// <summary>The final byte of the CSI sequence.</summary>
        public byte FinalByte { get; init; }

        /// <summary>The final byte as a string character.</summary>
        public string Final { get; init; } = string.Empty;

        /// <summary>The parsed numeric parameters.</summary>
        public int[] Parameters { get; init; } = Array.Empty<int>();

        /// <summary>True if the sequence has a '?' prefix (private mode).</summary>
        public bool IsPrivate { get; init; }

        /// <summary>The prefix character ('>' or null).</summary>
        public string? Prefix { get; init; }

        /// <summary>The intermediate characters extracted from the sequence.</summary>
        public string Intermediate { get; init; } = string.Empty;

        /// <summary>The parameter text string before parsing.</summary>
        public string ParameterText { get; init; } = string.Empty;
    }

    /// <summary>
    ///     Tokenizes a complete CSI sequence from the provided bytes.
    /// </summary>
    /// <param name="sequence">The complete CSI sequence bytes (including ESC [)</param>
    /// <returns>Tokenization result with parsed parameters and metadata</returns>
    public TokenizationResult Tokenize(ReadOnlySpan<byte> sequence)
    {
        if (sequence.Length < 3) // Minimum: ESC [ final
        {
            return new TokenizationResult
            {
                FinalByte = 0,
                Final = string.Empty,
                Parameters = Array.Empty<int>(),
                IsPrivate = false,
                Prefix = null,
                Intermediate = string.Empty,
                ParameterText = string.Empty
            };
        }

        byte finalByte = sequence[^1];
        string final = ((char)finalByte).ToString();

        // Extract parameters and intermediate characters
        var paramsText = new StringBuilder();
        var intermediate = new StringBuilder();

        // Skip ESC [ (first 2 bytes) and process until final byte
        for (int i = 2; i < sequence.Length - 1; i++)
        {
            byte b = sequence[i];
            if (b >= 0x30 && b <= 0x3f) // Parameter bytes (0-9, :, ;, <, =, >, ?)
            {
                paramsText.Append((char)b);
            }
            else if (b >= 0x20 && b <= 0x2f) // Intermediate bytes (space, !, ", #, etc.)
            {
                intermediate.Append((char)b);
            }
        }

        string parameterString = paramsText.ToString();
        string intermediateStr = intermediate.ToString();

        // Parse parameters
        if (!TryParseParameters(parameterString, out int[] parameters, out bool isPrivate, out string? prefix))
        {
            return new TokenizationResult
            {
                FinalByte = finalByte,
                Final = final,
                Parameters = Array.Empty<int>(),
                IsPrivate = false,
                Prefix = null,
                Intermediate = intermediateStr,
                ParameterText = parameterString
            };
        }

        return new TokenizationResult
        {
            FinalByte = finalByte,
            Final = final,
            Parameters = parameters,
            IsPrivate = isPrivate,
            Prefix = prefix,
            Intermediate = intermediateStr,
            ParameterText = parameterString
        };
    }

    /// <summary>
    ///     Attempts to parse CSI parameters from a parameter string.
    /// </summary>
    /// <param name="parameterString">The parameter portion of the CSI sequence</param>
    /// <param name="parameters">The parsed numeric parameters</param>
    /// <param name="isPrivate">True if the sequence has a '?' prefix</param>
    /// <param name="prefix">The prefix character ('>' or null)</param>
    /// <returns>True if parsing was successful</returns>
    public bool TryParseParameters(ReadOnlySpan<char> parameterString, out int[] parameters, out bool isPrivate,
        out string? prefix)
    {
        parameters = Array.Empty<int>();
        isPrivate = false;
        prefix = null;

        if (parameterString.IsEmpty)
        {
            return true;
        }

        string text = parameterString.ToString();

        // Check for private mode indicator
        if (text.StartsWith("?"))
        {
            isPrivate = true;
            text = text[1..];
        }
        else if (text.StartsWith(">"))
        {
            prefix = ">";
            text = text[1..];
        }

        if (text.Length == 0)
        {
            return true;
        }

        // Parse semicolon-separated parameters
        string[] parts = text.Split(';');
        var paramList = new List<int>();

        foreach (string part in parts)
        {
            if (string.IsNullOrEmpty(part))
            {
                // Empty parameter - treat as 0 (will be defaulted later)
                paramList.Add(0);
                continue;
            }

            if (int.TryParse(part, out int value))
            {
                paramList.Add(value);
            }
            else
            {
                // Invalid numbers are treated as 0 (following TypeScript behavior)
                paramList.Add(0);
            }
        }

        parameters = paramList.ToArray();
        return true;
    }
}
