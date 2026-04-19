using caTTY.Core.Types;

namespace caTTY.Core.Parsing.Csi;

/// <summary>
///     Factory for creating CsiMessage instances from parsed CSI sequence data.
///     Handles message construction, type determination, and final character to message mapping.
/// </summary>
public class CsiMessageFactory
{
    private readonly CsiTokenizer _tokenizer;

    /// <summary>
    ///     Creates a new CSI message factory.
    /// </summary>
    /// <param name="tokenizer">The tokenizer for accessing parameter parsing</param>
    public CsiMessageFactory(CsiTokenizer tokenizer)
    {
        _tokenizer = tokenizer;
    }

    /// <summary>
    ///     Builds a CSI message from tokenized sequence data.
    /// </summary>
    /// <param name="finalByte">The final byte of the CSI sequence</param>
    /// <param name="final">The final character as a string</param>
    /// <param name="parameters">The parsed numeric parameters</param>
    /// <param name="isPrivate">True if the sequence has a '?' prefix</param>
    /// <param name="prefix">The prefix character ('>' or null)</param>
    /// <param name="intermediate">The intermediate characters</param>
    /// <param name="raw">The raw string representation of the sequence</param>
    /// <returns>A constructed CsiMessage</returns>
    public CsiMessage BuildMessage(byte finalByte, string final, int[] parameters, bool isPrivate, string? prefix,
        string intermediate, string raw)
    {
        // DECSCUSR: CSI Ps SP q
        if (final == "q" && intermediate == " ")
        {
            int styleParam = CsiParamParsers.GetParameter(parameters, 0, 0);
            CursorStyle validatedStyle = CursorStyleExtensions.ValidateStyle(styleParam);
            return new CsiMessage
            {
                Type = "csi.setCursorStyle",
                Raw = raw,
                Implemented = true,
                FinalByte = finalByte,
                Parameters = parameters,
                CursorStyle = validatedStyle.ToInt()
            };
        }

        // DECSCA: CSI Ps " q
        if (final == "q" && intermediate == "\"" && !isPrivate && prefix == null)
        {
            int modeValue = CsiParamParsers.GetParameter(parameters, 0, 0);
            bool protectedValue = modeValue == 2;
            return new CsiMessage
            {
                Type = "csi.selectCharacterProtection",
                Raw = raw,
                Implemented = true,
                FinalByte = finalByte,
                Parameters = parameters,
                Protected = protectedValue
            };
        }

        // DEC private modes: CSI ? Pm h / l
        if (isPrivate && (final == "h" || final == "l"))
        {
            int[] modes = CsiParamParsers.ValidateDecModes(parameters);
            return new CsiMessage
            {
                Type = final == "h" ? "csi.decModeSet" : "csi.decModeReset",
                Raw = raw,
                Implemented = true,
                FinalByte = finalByte,
                Parameters = parameters,
                DecModes = modes
            };
        }

        // Standard modes: CSI Pm h / l (non-private)
        if (!isPrivate && prefix == null && (final == "h" || final == "l"))
        {
            // IRM (Insert/Replace Mode): CSI 4 h/l
            if (parameters.Length == 1 && parameters[0] == 4)
            {
                return new CsiMessage
                {
                    Type = "csi.insertMode",
                    Raw = raw,
                    Implemented = true,
                    FinalByte = finalByte,
                    Parameters = parameters,
                    Enable = final == "h"
                };
            }
        }

        // DECSTR: CSI ! p (soft reset)
        if (!isPrivate && prefix == null && final == "p" && intermediate == "!" && parameters.Length == 0)
        {
            return new CsiMessage
            {
                Type = "csi.decSoftReset",
                Raw = raw,
                Implemented = true,
                FinalByte = finalByte,
                Parameters = parameters
            };
        }

        // Cursor movement commands
        return final switch
        {
            "A" => CreateCursorMessage("csi.cursorUp", raw, finalByte, parameters, CsiParamParsers.GetParameter(parameters, 0, 1)),
            "B" => CreateCursorMessage("csi.cursorDown", raw, finalByte, parameters, CsiParamParsers.GetParameter(parameters, 0, 1)),
            "C" => CreateCursorMessage("csi.cursorForward", raw, finalByte, parameters, CsiParamParsers.GetParameter(parameters, 0, 1)),
            "D" => CreateCursorMessage("csi.cursorBackward", raw, finalByte, parameters,
                CsiParamParsers.GetParameter(parameters, 0, 1)),
            "E" => CreateCursorMessage("csi.cursorNextLine", raw, finalByte, parameters,
                CsiParamParsers.GetParameter(parameters, 0, 1)),
            "F" => CreateCursorMessage("csi.cursorPrevLine", raw, finalByte, parameters,
                CsiParamParsers.GetParameter(parameters, 0, 1)),
            "G" => CreateCursorMessage("csi.cursorHorizontalAbsolute", raw, finalByte, parameters,
                CsiParamParsers.GetParameter(parameters, 0, 1)),
            "d" => CreateCursorMessage("csi.verticalPositionAbsolute", raw, finalByte, parameters,
                CsiParamParsers.GetParameter(parameters, 0, 1)),
            "H" or "f" => CreateCursorPositionMessage(raw, finalByte, parameters),
            _ => BuildAdditionalMessages(finalByte, final, parameters, isPrivate, prefix, intermediate, raw)
        };
    }

    /// <summary>
    ///     Builds additional CSI messages not covered by cursor movement.
    /// </summary>
    private CsiMessage BuildAdditionalMessages(byte finalByte, string final, int[] parameters, bool isPrivate,
        string? prefix, string intermediate, string raw)
    {
        return final switch
        {
            // Tab commands
            "I" when !isPrivate && prefix == null && intermediate == "" =>
                CreateTabMessage("csi.cursorForwardTab", raw, finalByte, parameters),
            "Z" when !isPrivate && prefix == null && intermediate == "" =>
                CreateTabMessage("csi.cursorBackwardTab", raw, finalByte, parameters),
            "g" when !isPrivate && prefix == null && intermediate == "" =>
                CreateTabClearMessage(raw, finalByte, parameters),

            // Erase commands
            "J" => CreateEraseDisplayMessage(raw, finalByte, parameters, isPrivate),
            "K" => CreateEraseLineMessage(raw, finalByte, parameters, isPrivate),
            "X" => CreateEraseCharacterMessage(raw, finalByte, parameters),

            // Scroll commands
            "S" => CreateScrollMessage("csi.scrollUp", raw, finalByte, parameters),
            "T" when parameters.Length <= 1 && !isPrivate =>
                CreateScrollMessage("csi.scrollDown", raw, finalByte, parameters),

            // Position save/restore
            "s" => CreateSaveRestoreMessage(raw, finalByte, parameters, isPrivate, true),
            "u" => CreateSaveRestoreMessage(raw, finalByte, parameters, isPrivate, false),
            "r" => CreateScrollRegionMessage(raw, finalByte, parameters, isPrivate),

            // Device queries
            "c" => CreateDeviceAttributesMessage(raw, finalByte, parameters, prefix),
            "n" => CreateDeviceStatusMessage(raw, finalByte, parameters, isPrivate),
            "t" when !isPrivate && prefix == null => CreateWindowManipulationMessage(raw, finalByte, parameters),

            // Line/character operations
            "M" when !isPrivate && prefix == null && intermediate == "" && parameters.Length <= 1 =>
                CreateLineMessage("csi.deleteLines", raw, finalByte, parameters),
            "L" when !isPrivate && prefix == null && intermediate == "" && parameters.Length <= 1 =>
                CreateLineMessage("csi.insertLines", raw, finalByte, parameters),
            "@" when !isPrivate && prefix == null && intermediate == "" && parameters.Length <= 1 =>
                CreateCharMessage("csi.insertChars", raw, finalByte, parameters),
            "P" when !isPrivate && prefix == null && intermediate == "" && parameters.Length <= 1 =>
                CreateCharMessage("csi.deleteChars", raw, finalByte, parameters),

            // SGR variants
            "m" when prefix == ">" => CreateEnhancedSgrMessage(raw, finalByte, parameters),
            "m" when isPrivate => CreatePrivateSgrMessage(raw, finalByte, parameters),
            "m" when intermediate.Length > 0 => CreateSgrWithIntermediateMessage(raw, finalByte, parameters,
                intermediate),
            "m" when !isPrivate && prefix == null && intermediate == "" => CreateStandardSgrMessage(raw, finalByte, parameters),

            _ => CreateUnknownMessage(raw, parameters, isPrivate, prefix, intermediate)
        };
    }

    // Helper methods for creating specific message types
    private static CsiMessage CreateCursorMessage(string type, string raw, byte finalByte, int[] parameters, int count)
    {
        return new CsiMessage
        {
            Type = type,
            Raw = raw,
            Implemented = true,
            FinalByte = finalByte,
            Parameters = parameters,
            Count = count
        };
    }

    private static CsiMessage CreateCursorPositionMessage(string raw, byte finalByte, int[] parameters)
    {
        // Default missing or zero parameters to 1 (following TypeScript behavior)
        var (row, column) = CsiParamParsers.ParseCursorPosition(parameters);

        return new CsiMessage
        {
            Type = "csi.cursorPosition",
            Raw = raw,
            Implemented = true,
            FinalByte = finalByte,
            Parameters = parameters,
            Row = row,
            Column = column
        };
    }

    private static CsiMessage CreateTabMessage(string type, string raw, byte finalByte, int[] parameters)
    {
        return new CsiMessage
        {
            Type = type,
            Raw = raw,
            Implemented = true,
            FinalByte = finalByte,
            Parameters = parameters,
            Count = CsiParamParsers.GetParameter(parameters, 0, 1)
        };
    }

    private static CsiMessage CreateTabClearMessage(string raw, byte finalByte, int[] parameters)
    {
        int mode = CsiParamParsers.GetParameterFromSet(parameters, 0, 0, 0, 3);
        return new CsiMessage
        {
            Type = "csi.tabClear",
            Raw = raw,
            Implemented = true,
            FinalByte = finalByte,
            Parameters = parameters,
            Mode = mode
        };
    }

    private static CsiMessage CreateEraseDisplayMessage(string raw, byte finalByte, int[] parameters, bool isPrivate)
    {
        int mode = CsiParamParsers.GetParameterInRange(parameters, 0, 0, 0, 3);

        return new CsiMessage
        {
            Type = isPrivate ? "csi.selectiveEraseInDisplay" : "csi.eraseInDisplay",
            Raw = raw,
            Implemented = true,
            FinalByte = finalByte,
            Parameters = parameters,
            Mode = mode
        };
    }

    private static CsiMessage CreateEraseLineMessage(string raw, byte finalByte, int[] parameters, bool isPrivate)
    {
        int mode = CsiParamParsers.GetParameterInRange(parameters, 0, 0, 0, 2);

        return new CsiMessage
        {
            Type = isPrivate ? "csi.selectiveEraseInLine" : "csi.eraseInLine",
            Raw = raw,
            Implemented = true,
            FinalByte = finalByte,
            Parameters = parameters,
            Mode = mode
        };
    }

    private static CsiMessage CreateEraseCharacterMessage(string raw, byte finalByte, int[] parameters)
    {
        return new CsiMessage
        {
            Type = "csi.eraseCharacter",
            Raw = raw,
            Implemented = true,
            FinalByte = finalByte,
            Parameters = parameters,
            Count = CsiParamParsers.GetParameter(parameters, 0, 1)
        };
    }

    private static CsiMessage CreateScrollMessage(string type, string raw, byte finalByte, int[] parameters)
    {
        return new CsiMessage
        {
            Type = type,
            Raw = raw,
            Implemented = true,
            FinalByte = finalByte,
            Parameters = parameters,
            Lines = CsiParamParsers.GetParameter(parameters, 0, 1)
        };
    }

    private static CsiMessage CreateSaveRestoreMessage(string raw, byte finalByte, int[] parameters, bool isPrivate,
        bool isSave)
    {
        if (isPrivate)
        {
            int[] modes = CsiParamParsers.ValidateDecModes(parameters);
            return new CsiMessage
            {
                Type = isSave ? "csi.savePrivateMode" : "csi.restorePrivateMode",
                Raw = raw,
                Implemented = true,
                FinalByte = finalByte,
                Parameters = parameters,
                DecModes = modes
            };
        }

        return new CsiMessage
        {
            Type = isSave ? "csi.saveCursorPosition" : "csi.restoreCursorPosition",
            Raw = raw,
            Implemented = true,
            FinalByte = finalByte,
            Parameters = parameters
        };
    }

    private static CsiMessage CreateScrollRegionMessage(string raw, byte finalByte, int[] parameters, bool isPrivate)
    {
        if (isPrivate)
        {
            int[] modes = CsiParamParsers.ValidateDecModes(parameters);
            return new CsiMessage
            {
                Type = "csi.restorePrivateMode",
                Raw = raw,
                Implemented = true,
                FinalByte = finalByte,
                Parameters = parameters,
                DecModes = modes
            };
        }

        return new CsiMessage
        {
            Type = "csi.setScrollRegion",
            Raw = raw,
            Implemented = true,
            FinalByte = finalByte,
            Parameters = parameters,
            Top = parameters.Length >= 1 ? parameters[0] : null,
            Bottom = parameters.Length >= 2 ? parameters[1] : null
        };
    }

    private static CsiMessage CreateDeviceAttributesMessage(string raw, byte finalByte, int[] parameters,
        string? prefix)
    {
        // Secondary DA: CSI > c or CSI > 0 c
        if (prefix == ">" && (parameters.Length == 0 || (parameters.Length == 1 && parameters[0] == 0)))
        {
            return new CsiMessage
            {
                Type = "csi.deviceAttributesSecondary",
                Raw = raw,
                Implemented = true,
                FinalByte = finalByte,
                Parameters = parameters
            };
        }

        // Primary DA: CSI c or CSI 0 c
        if (prefix == null && (parameters.Length == 0 || (parameters.Length == 1 && parameters[0] == 0)))
        {
            return new CsiMessage
            {
                Type = "csi.deviceAttributesPrimary",
                Raw = raw,
                Implemented = true,
                FinalByte = finalByte,
                Parameters = parameters
            };
        }

        return CreateUnknownMessage(raw, parameters, false, prefix, "");
    }

    private static CsiMessage CreateDeviceStatusMessage(string raw, byte finalByte, int[] parameters, bool isPrivate)
    {
        if (isPrivate && parameters.Length == 1 && parameters[0] == 26)
        {
            return new CsiMessage
            {
                Type = "csi.characterSetQuery",
                Raw = raw,
                Implemented = true,
                FinalByte = finalByte,
                Parameters = parameters
            };
        }

        if (!isPrivate && parameters.Length == 1)
        {
            return parameters[0] switch
            {
                6 => new CsiMessage
                {
                    Type = "csi.cursorPositionReport",
                    Raw = raw,
                    Implemented = true,
                    FinalByte = finalByte,
                    Parameters = parameters
                },
                5 => new CsiMessage
                {
                    Type = "csi.deviceStatusReport",
                    Raw = raw,
                    Implemented = true,
                    FinalByte = finalByte,
                    Parameters = parameters
                },
                _ => CreateUnknownMessage(raw, parameters, isPrivate, null, "")
            };
        }

        return CreateUnknownMessage(raw, parameters, isPrivate, null, "");
    }

    private static CsiMessage CreateWindowManipulationMessage(string raw, byte finalByte, int[] parameters)
    {
        if (parameters.Length == 1 && parameters[0] == 18)
        {
            return new CsiMessage
            {
                Type = "csi.terminalSizeQuery",
                Raw = raw,
                Implemented = true,
                FinalByte = finalByte,
                Parameters = parameters
            };
        }

        if (parameters.Length >= 1)
        {
            int operation = parameters[0];
            bool implemented = false;

            // Title stack operations: 22;1t, 22;2t, 23;1t, 23;2t
            if ((operation == 22 || operation == 23) && parameters.Length >= 2)
            {
                int subOperation = parameters[1];
                if (subOperation == 1 || subOperation == 2)
                {
                    implemented = true;
                }
            }

            return new CsiMessage
            {
                Type = "csi.windowManipulation",
                Raw = raw,
                Implemented = implemented,
                FinalByte = finalByte,
                Parameters = parameters,
                Operation = operation,
                WindowParams = parameters.Length > 1 ? parameters[1..] : Array.Empty<int>()
            };
        }

        return CreateUnknownMessage(raw, parameters, false, null, "");
    }

    private static CsiMessage CreateLineMessage(string type, string raw, byte finalByte, int[] parameters)
    {
        return new CsiMessage
        {
            Type = type,
            Raw = raw,
            Implemented = true,
            FinalByte = finalByte,
            Parameters = parameters,
            Count = CsiParamParsers.GetParameter(parameters, 0, 1)
        };
    }

    private static CsiMessage CreateCharMessage(string type, string raw, byte finalByte, int[] parameters)
    {
        return new CsiMessage
        {
            Type = type,
            Raw = raw,
            Implemented = true,
            FinalByte = finalByte,
            Parameters = parameters,
            Count = CsiParamParsers.GetParameter(parameters, 0, 1)
        };
    }

    private static CsiMessage CreateStandardSgrMessage(string raw, byte finalByte, int[] parameters)
    {
        return new CsiMessage
        {
            Type = "csi.sgr",
            Raw = raw,
            Implemented = true,
            FinalByte = finalByte,
            Parameters = parameters
        };
    }

    private static CsiMessage CreateEnhancedSgrMessage(string raw, byte finalByte, int[] parameters)
    {
        bool implemented = parameters.Length >= 2 && parameters[0] == 4 && parameters[1] >= 0 && parameters[1] <= 5;
        return new CsiMessage
        {
            Type = "csi.enhancedSgrMode",
            Raw = raw,
            Implemented = implemented,
            FinalByte = finalByte,
            Parameters = parameters
        };
    }

    private static CsiMessage CreatePrivateSgrMessage(string raw, byte finalByte, int[] parameters)
    {
        bool implemented = parameters.Length == 1 && parameters[0] == 4;
        return new CsiMessage
        {
            Type = "csi.privateSgrMode",
            Raw = raw,
            Implemented = implemented,
            FinalByte = finalByte,
            Parameters = parameters
        };
    }

    private static CsiMessage CreateSgrWithIntermediateMessage(string raw, byte finalByte, int[] parameters,
        string intermediate)
    {
        bool implemented = intermediate == "%" && parameters.Length == 1 && parameters[0] == 0;
        return new CsiMessage
        {
            Type = "csi.sgrWithIntermediate",
            Raw = raw,
            Implemented = implemented,
            FinalByte = finalByte,
            Parameters = parameters,
            Intermediate = intermediate
        };
    }

    private static CsiMessage CreateUnknownMessage(string raw, int[] parameters, bool isPrivate, string? prefix,
        string intermediate)
    {
        return new CsiMessage
        {
            Type = "csi.unknown",
            Raw = raw,
            Implemented = false,
            FinalByte = 0,
            Parameters = parameters,
            IsPrivate = isPrivate,
            Prefix = prefix,
            Intermediate = intermediate
        };
    }
}
