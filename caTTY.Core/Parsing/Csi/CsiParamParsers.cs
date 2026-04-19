namespace caTTY.Core.Parsing.Csi;

/// <summary>
///     Provides parameter parsing and validation helpers for CSI sequences.
///     Handles parameter extraction, default value application, range validation,
///     and special-case parameter processing.
/// </summary>
public class CsiParamParsers
{
    /// <summary>
    ///     Gets a parameter value with a fallback default.
    /// </summary>
    /// <param name="parameters">The parameter array</param>
    /// <param name="index">The parameter index</param>
    /// <param name="fallback">The default value if parameter is missing</param>
    /// <returns>The parameter value or fallback</returns>
    public static int GetParameter(int[] parameters, int index, int fallback)
    {
        if (index < 0 || index >= parameters.Length)
        {
            return fallback;
        }

        return parameters[index];
    }

    /// <summary>
    ///     Gets a parameter value with zero-to-default conversion.
    ///     Used for cursor position commands where 0 means 1.
    /// </summary>
    /// <param name="parameters">The parameter array</param>
    /// <param name="index">The parameter index</param>
    /// <param name="fallback">The default value if parameter is missing or zero</param>
    /// <returns>The parameter value or fallback if missing/zero</returns>
    public static int GetParameterZeroAsDefault(int[] parameters, int index, int fallback)
    {
        if (index < 0 || index >= parameters.Length)
        {
            return fallback;
        }

        int value = parameters[index];
        return value > 0 ? value : fallback;
    }

    /// <summary>
    ///     Gets a parameter value and validates it against a range.
    ///     Returns the value if valid, otherwise returns the fallback.
    /// </summary>
    /// <param name="parameters">The parameter array</param>
    /// <param name="index">The parameter index</param>
    /// <param name="fallback">The default value if parameter is missing or out of range</param>
    /// <param name="minValue">The minimum valid value (inclusive)</param>
    /// <param name="maxValue">The maximum valid value (inclusive)</param>
    /// <returns>The parameter value if in range, otherwise fallback</returns>
    public static int GetParameterInRange(int[] parameters, int index, int fallback, int minValue, int maxValue)
    {
        int value = GetParameter(parameters, index, fallback);
        return value >= minValue && value <= maxValue ? value : fallback;
    }

    /// <summary>
    ///     Gets a parameter value and maps it to a specific set of valid values.
    ///     Returns the value if it matches one of the valid values, otherwise returns the fallback.
    /// </summary>
    /// <param name="parameters">The parameter array</param>
    /// <param name="index">The parameter index</param>
    /// <param name="fallback">The default value if parameter is missing or not in valid set</param>
    /// <param name="validValues">The set of valid values</param>
    /// <returns>The parameter value if valid, otherwise fallback</returns>
    public static int GetParameterFromSet(int[] parameters, int index, int fallback, params int[] validValues)
    {
        int value = GetParameter(parameters, index, fallback);
        return validValues.Contains(value) ? value : fallback;
    }

    /// <summary>
    ///     Validates DEC private mode numbers and filters out invalid ones.
    /// </summary>
    /// <param name="parameters">The parameter array containing mode numbers</param>
    /// <returns>Array of valid DEC mode numbers</returns>
    public static int[] ValidateDecModes(int[] parameters)
    {
        var validModes = new List<int>();

        foreach (int mode in parameters)
        {
            if (IsValidDecModeNumber(mode))
            {
                validModes.Add(mode);
            }
        }

        return validModes.ToArray();
    }

    /// <summary>
    ///     Checks if a DEC private mode number is valid.
    /// </summary>
    /// <param name="mode">The mode number to validate</param>
    /// <returns>True if the mode number is valid</returns>
    public static bool IsValidDecModeNumber(int mode)
    {
        // Validate mode is a positive integer
        if (mode < 0)
        {
            return false;
        }

        // DEC private modes can range from 1 to 65535 (16-bit unsigned integer range)
        return mode <= 65535;
    }

    /// <summary>
    ///     Parses cursor position parameters (row, column) with special handling for zero/missing values.
    ///     Both row and column default to 1 if missing or zero.
    /// </summary>
    /// <param name="parameters">The parameter array</param>
    /// <returns>A tuple of (row, column) with defaults applied</returns>
    public static (int row, int column) ParseCursorPosition(int[] parameters)
    {
        // Default missing or zero parameters to 1 (following TypeScript behavior)
        int row = GetParameterZeroAsDefault(parameters, 0, 1);
        int column = GetParameterZeroAsDefault(parameters, 1, 1);

        return (row, column);
    }
}
