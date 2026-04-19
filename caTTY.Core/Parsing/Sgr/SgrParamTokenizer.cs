using System.Text;

namespace caTTY.Core.Parsing.Sgr;

/// <summary>
///     Tokenizes SGR parameter strings into individual parameters and separators.
///     Handles both semicolon and colon separators, prefixes, and intermediate characters.
/// </summary>
public class SgrParamTokenizer
{
    /// <summary>
    ///     Parses SGR parameters and separators from raw sequence.
    /// </summary>
    /// <param name="raw">The raw escape sequence string (e.g., "ESC[1;32m")</param>
    /// <returns>A result containing parsed parameters, separators, prefix, and intermediate characters</returns>
    public SgrParseResult ParseSgrParamsAndSeparators(string raw)
    {
        // Extract parameter text (remove ESC[ and final m)
        string paramsText = raw.Length >= 3 ? raw[2..^1] : "";

        var parameters = new List<int>();
        var separators = new List<string>();
        string? prefix = null;
        string? intermediate = null;

        // Check for special prefixes
        int startIndex = 0;
        if (paramsText.StartsWith(">"))
        {
            prefix = ">";
            startIndex = 1;
        }
        else if (paramsText.StartsWith("?"))
        {
            prefix = "?";
            startIndex = 1;
        }

        var current = new StringBuilder();
        for (int i = startIndex; i < paramsText.Length; i++)
        {
            char ch = paramsText[i];

            if (char.IsDigit(ch))
            {
                current.Append(ch);
                continue;
            }

            if (ch == ';' || ch == ':')
            {
                parameters.Add(current.Length > 0 ? int.Parse(current.ToString()) : 0);
                separators.Add(ch.ToString());
                current.Clear();
                continue;
            }

            // Check for intermediate characters (0x20-0x2F range)
            if (ch >= 0x20 && ch <= 0x2F)
            {
                if (current.Length > 0)
                {
                    parameters.Add(int.Parse(current.ToString()));
                    current.Clear();
                }
                intermediate = ch.ToString();
                continue;
            }
        }

        // Add final parameter
        if (current.Length > 0)
        {
            parameters.Add(int.Parse(current.ToString()));
        }
        else if (paramsText.EndsWith(";") || paramsText.EndsWith(":"))
        {
            parameters.Add(0);
        }

        if (parameters.Count == 0)
        {
            parameters.Add(0);
        }

        return new SgrParseResult
        {
            Params = parameters.ToArray(),
            Separators = separators.ToArray(),
            Prefix = prefix,
            Intermediate = intermediate
        };
    }

    /// <summary>
    ///     Parses SGR parameters with support for both semicolon and colon separators.
    ///     This is a simplified tokenization method for general parameter parsing.
    /// </summary>
    /// <param name="parameterString">The parameter string to parse</param>
    /// <param name="parameters">The parsed parameters</param>
    /// <returns>True if parsing was successful</returns>
    public bool TryParseParameters(ReadOnlySpan<char> parameterString, out int[] parameters)
    {
        var result = new List<int>();
        var current = new StringBuilder();

        foreach (char ch in parameterString)
        {
            if (char.IsDigit(ch))
            {
                current.Append(ch);
            }
            else if (ch == ';' || ch == ':')
            {
                if (current.Length > 0)
                {
                    if (int.TryParse(current.ToString(), out int value))
                    {
                        result.Add(value);
                    }
                    else
                    {
                        parameters = Array.Empty<int>();
                        return false;
                    }
                }
                else
                {
                    result.Add(0); // Empty parameter defaults to 0
                }
                current.Clear();
            }
        }

        // Add final parameter
        if (current.Length > 0)
        {
            if (int.TryParse(current.ToString(), out int value))
            {
                result.Add(value);
            }
            else
            {
                parameters = Array.Empty<int>();
                return false;
            }
        }
        else if (parameterString.Length > 0 && (parameterString[^1] == ';' || parameterString[^1] == ':'))
        {
            result.Add(0); // Trailing separator means empty parameter
        }

        if (result.Count == 0)
        {
            result.Add(0); // Default to reset if no parameters
        }

        parameters = result.ToArray();
        return true;
    }
}

/// <summary>
///     Result of parsing SGR parameters and separators.
/// </summary>
public class SgrParseResult
{
    public int[] Params { get; set; } = Array.Empty<int>();
    public string[] Separators { get; set; } = Array.Empty<string>();
    public string? Prefix { get; set; }
    public string? Intermediate { get; set; }
}
