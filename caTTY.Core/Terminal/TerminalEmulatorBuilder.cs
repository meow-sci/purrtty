using caTTY.Core.Parsing;
using caTTY.Core.Types;
using caTTY.Core.Managers;
using caTTY.Core.Tracing;
using caTTY.Core.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace caTTY.Core.Terminal;

/// <summary>
///     Builder class for creating TerminalEmulator instances with proper initialization stages.
///     Separates construction complexity from business logic.
/// </summary>
internal static class TerminalEmulatorBuilder
{
    /// <summary>
    ///     Builds and returns a fully initialized TerminalEmulator.
    ///     This method contains all the initialization logic previously in the constructor.
    /// </summary>
    public static TerminalEmulator Build(int width, int height, int scrollbackLines, ILogger? logger, IRpcHandler? rpcHandler, IOscRpcHandler? oscRpcHandler = null)
    {
        if (width < 1 || width > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be between 1 and 1000");
        }

        if (height < 1 || height > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be between 1 and 1000");
        }

        if (scrollbackLines < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scrollbackLines), "Scrollback lines cannot be negative");
        }

        var effectiveLogger = logger ?? NullLogger.Instance;

        var cursor = new Cursor();
        var state = new TerminalState(width, height);

        // Use a dual buffer so alternate-screen applications (htop/vim/less/etc.)
        // don't corrupt primary screen content and scrollback behavior.
        var screenBuffer = new DualScreenBuffer(width, height, () => state.IsAlternateScreenActive);

        // Initialize scrollback infrastructure
        var scrollbackBuffer = new ScrollbackBuffer(scrollbackLines, width);
        var scrollbackManager = new ScrollbackManager(scrollbackLines, width);

        // Initialize managers
        var screenBufferManager = new ScreenBufferManager(screenBuffer);
        var cursorManager = new CursorManager(cursor);
        var modeManager = new ModeManager();
        var attributeManager = new AttributeManager();
        var characterSetManager = new CharacterSetManager(state);
        var alternateScreenManager = new AlternateScreenManager(state, cursorManager, (DualScreenBuffer)screenBuffer);

        // Set up scrollback integration
        screenBufferManager.SetScrollbackIntegration(
            row => scrollbackManager.AddLine(row),
            () => state.IsAlternateScreenActive
        );

        // Create the emulator first so we can reference its methods
        var emulator = new TerminalEmulator();

        // Initialize operation classes - create screen update ops first as it's used by other ops
        var screenUpdateOps = new EmulatorOps.TerminalScreenUpdateOps(e => emulator.OnScreenUpdatedEvent(e));
        var viewportOps = new EmulatorOps.TerminalViewportOps(scrollbackManager, screenUpdateOps.OnScreenUpdated);
        var resizeOps = new EmulatorOps.TerminalResizeOps(state, screenBufferManager, cursorManager, scrollbackManager, () => emulator.Width, () => emulator.Height, screenUpdateOps.OnScreenUpdated);

        // Build operations by category
        var (cursorMovementOps, cursorSaveRestoreOps, cursorStyleOps) = BuildCursorOps(emulator, cursorManager, state, effectiveLogger);
        var (eraseInDisplayOps, eraseInLineOps, selectiveEraseInDisplayOps, selectiveEraseInLineOps, eraseCharsOps) = BuildEraseOps(emulator, cursorManager, attributeManager, screenBufferManager, scrollbackManager, state, effectiveLogger);
        var (scrollOps, scrollRegionOps, insertLinesOps, deleteLinesOps, insertCharsOps, deleteCharsOps) = BuildScrollOps(emulator, cursorManager, screenBufferManager, attributeManager, state, cursor);
        var (insertModeOps, alternateScreenOps, decModeOps, privateModesOps, bracketedPasteOps) = BuildModeOps(emulator, cursorManager, screenBufferManager, attributeManager, modeManager, alternateScreenManager, characterSetManager, scrollbackManager, state, effectiveLogger);
        var (titleIconEventsOps, oscTitleIconOps, oscWindowManipulationOps, oscClipboardOps, oscHyperlinkOps, oscColorQueryOps) = BuildOscOps(emulator, attributeManager, state, effectiveLogger);
        var (charsetDesignationOps, charsetTranslationOps, lineFeedOps, indexOps, carriageReturnOps, bellOps, backspaceOps, tabOps, responseOps, inputOps, resetOps) = BuildMiscOps(emulator, cursorManager, screenBufferManager, attributeManager, modeManager, characterSetManager, state, screenBuffer, cursor, effectiveLogger);

        // Initialize parser with terminal handlers and optional RPC components
        var handlers = new TerminalParserHandlers(emulator, effectiveLogger, rpcHandler, oscRpcHandler);
        var parserOptions = new ParserOptions
        {
            Handlers = handlers,
            Logger = effectiveLogger,
            EmitNormalBytesDuringEscapeSequence = false,
            ProcessC0ControlsDuringEscapeSequence = true,
            CursorPositionProvider = emulator
        };

        // Wire RPC components if RPC handler is provided
        if (rpcHandler != null)
        {
            // Create RPC components for integration
            // Note: These would typically be injected, but for clean integration we create them here
            var rpcSequenceDetector = new RpcSequenceDetector();
            var rpcSequenceParser = new RpcSequenceParser();

            parserOptions.RpcSequenceDetector = rpcSequenceDetector;
            parserOptions.RpcSequenceParser = rpcSequenceParser;
            parserOptions.RpcHandler = rpcHandler;

            effectiveLogger.LogDebug("RPC functionality enabled for terminal emulator");
        }
        else
        {
            effectiveLogger.LogDebug("RPC functionality disabled - no RPC handler provided");
        }

        var parser = new Parser(parserOptions);

        // Initialize all fields in the emulator
        emulator.Initialize(
            cursor,
            state,
            screenBuffer,
            scrollbackBuffer,
            scrollbackManager,
            screenBufferManager,
            cursorManager,
            modeManager,
            attributeManager,
            alternateScreenManager,
            characterSetManager,
            viewportOps,
            resizeOps,
            cursorMovementOps,
            cursorSaveRestoreOps,
            cursorStyleOps,
            eraseInDisplayOps,
            eraseInLineOps,
            selectiveEraseInDisplayOps,
            selectiveEraseInLineOps,
            scrollOps,
            scrollRegionOps,
            insertLinesOps,
            deleteLinesOps,
            insertCharsOps,
            deleteCharsOps,
            eraseCharsOps,
            insertModeOps,
            alternateScreenOps,
            decModeOps,
            privateModesOps,
            bracketedPasteOps,
            oscTitleIconOps,
            oscWindowManipulationOps,
            oscClipboardOps,
            oscHyperlinkOps,
            oscColorQueryOps,
            charsetDesignationOps,
            charsetTranslationOps,
            lineFeedOps,
            indexOps,
            carriageReturnOps,
            bellOps,
            backspaceOps,
            tabOps,
            responseOps,
            screenUpdateOps,
            titleIconEventsOps,
            inputOps,
            resetOps,
            parser,
            rpcHandler,
            effectiveLogger
        );

        return emulator;
    }

    /// <summary>
    ///     Builds cursor-related operation classes.
    /// </summary>
    private static (
        EmulatorOps.TerminalCursorMovementOps cursorMovementOps,
        EmulatorOps.TerminalCursorSaveRestoreOps cursorSaveRestoreOps,
        EmulatorOps.TerminalCursorStyleOps cursorStyleOps)
    BuildCursorOps(
        TerminalEmulator emulator,
        CursorManager cursorManager,
        TerminalState state,
        ILogger effectiveLogger)
    {
        var cursorMovementOps = new EmulatorOps.TerminalCursorMovementOps(cursorManager, () => state, () => emulator.Width);
        var cursorSaveRestoreOps = new EmulatorOps.TerminalCursorSaveRestoreOps(cursorManager, () => state, () => emulator.Width, () => emulator.Height, effectiveLogger);
        var cursorStyleOps = new EmulatorOps.TerminalCursorStyleOps(cursorManager, () => state, effectiveLogger);

        return (cursorMovementOps, cursorSaveRestoreOps, cursorStyleOps);
    }

    /// <summary>
    ///     Builds erase-related operation classes.
    /// </summary>
    private static (
        EmulatorOps.TerminalEraseInDisplayOps eraseInDisplayOps,
        EmulatorOps.TerminalEraseInLineOps eraseInLineOps,
        EmulatorOps.TerminalSelectiveEraseInDisplayOps selectiveEraseInDisplayOps,
        EmulatorOps.TerminalSelectiveEraseInLineOps selectiveEraseInLineOps,
        EmulatorOps.TerminalEraseCharsOps eraseCharsOps)
    BuildEraseOps(
        TerminalEmulator emulator,
        CursorManager cursorManager,
        AttributeManager attributeManager,
        ScreenBufferManager screenBufferManager,
        IScrollbackManager scrollbackManager,
        TerminalState state,
        ILogger effectiveLogger)
    {
        var eraseInDisplayOps = new EmulatorOps.TerminalEraseInDisplayOps(cursorManager, attributeManager, screenBufferManager, scrollbackManager, () => state, () => emulator.Width, () => emulator.Height, emulator.ClearLine, effectiveLogger);
        var eraseInLineOps = new EmulatorOps.TerminalEraseInLineOps(cursorManager, attributeManager, screenBufferManager, () => state, () => emulator.Width, () => emulator.Height, effectiveLogger);
        var selectiveEraseInLineOps = new EmulatorOps.TerminalSelectiveEraseInLineOps(cursorManager, attributeManager, screenBufferManager, () => state, () => emulator.Width, () => emulator.Height, effectiveLogger);
        var selectiveEraseInDisplayOps = new EmulatorOps.TerminalSelectiveEraseInDisplayOps(cursorManager, attributeManager, screenBufferManager, () => state, () => emulator.Width, () => emulator.Height, emulator.ClearLineSelective, effectiveLogger);
        var eraseCharsOps = new EmulatorOps.TerminalEraseCharsOps(cursorManager, screenBufferManager, attributeManager, () => state);

        return (eraseInDisplayOps, eraseInLineOps, selectiveEraseInDisplayOps, selectiveEraseInLineOps, eraseCharsOps);
    }

    /// <summary>
    ///     Builds scroll-related operation classes.
    /// </summary>
    private static (
        EmulatorOps.TerminalScrollOps scrollOps,
        EmulatorOps.TerminalScrollRegionOps scrollRegionOps,
        EmulatorOps.TerminalInsertLinesOps insertLinesOps,
        EmulatorOps.TerminalDeleteLinesOps deleteLinesOps,
        EmulatorOps.TerminalInsertCharsOps insertCharsOps,
        EmulatorOps.TerminalDeleteCharsOps deleteCharsOps)
    BuildScrollOps(
        TerminalEmulator emulator,
        CursorManager cursorManager,
        ScreenBufferManager screenBufferManager,
        AttributeManager attributeManager,
        TerminalState state,
        Cursor cursor)
    {
        var scrollOps = new EmulatorOps.TerminalScrollOps(cursorManager, screenBufferManager, attributeManager, () => state, () => cursor);
        var scrollRegionOps = new EmulatorOps.TerminalScrollRegionOps(cursorManager, () => state, () => emulator.Height);
        var insertLinesOps = new EmulatorOps.TerminalInsertLinesOps(cursorManager, screenBufferManager, attributeManager, () => state);
        var deleteLinesOps = new EmulatorOps.TerminalDeleteLinesOps(cursorManager, screenBufferManager, attributeManager, () => state);
        var insertCharsOps = new EmulatorOps.TerminalInsertCharsOps(cursorManager, screenBufferManager, attributeManager, () => state);
        var deleteCharsOps = new EmulatorOps.TerminalDeleteCharsOps(cursorManager, screenBufferManager, attributeManager, () => state);

        return (scrollOps, scrollRegionOps, insertLinesOps, deleteLinesOps, insertCharsOps, deleteCharsOps);
    }

    /// <summary>
    ///     Builds mode-related operation classes.
    /// </summary>
    private static (
        EmulatorOps.TerminalInsertModeOps insertModeOps,
        EmulatorOps.TerminalAlternateScreenOps alternateScreenOps,
        EmulatorOps.TerminalDecModeOps decModeOps,
        EmulatorOps.TerminalPrivateModesOps privateModesOps,
        EmulatorOps.TerminalBracketedPasteOps bracketedPasteOps)
    BuildModeOps(
        TerminalEmulator emulator,
        CursorManager cursorManager,
        ScreenBufferManager screenBufferManager,
        AttributeManager attributeManager,
        ModeManager modeManager,
        AlternateScreenManager alternateScreenManager,
        CharacterSetManager characterSetManager,
        ScrollbackManager scrollbackManager,
        TerminalState state,
        ILogger effectiveLogger)
    {
        var insertModeOps = new EmulatorOps.TerminalInsertModeOps(cursorManager, screenBufferManager, attributeManager, modeManager, () => state, () => emulator.Width, () => emulator.Height);
        var alternateScreenOps = new EmulatorOps.TerminalAlternateScreenOps(cursorManager, alternateScreenManager, scrollbackManager, () => state);
        var decModeOps = new EmulatorOps.TerminalDecModeOps(cursorManager, modeManager, alternateScreenManager, characterSetManager, scrollbackManager, () => state, alternateScreenOps.HandleAlternateScreenMode, effectiveLogger);
        var privateModesOps = new EmulatorOps.TerminalPrivateModesOps(modeManager);
        var bracketedPasteOps = new EmulatorOps.TerminalBracketedPasteOps(() => state);

        return (insertModeOps, alternateScreenOps, decModeOps, privateModesOps, bracketedPasteOps);
    }

    /// <summary>
    ///     Builds OSC-related operation classes.
    /// </summary>
    private static (
        EmulatorOps.TerminalTitleIconEventsOps titleIconEventsOps,
        EmulatorOps.TerminalOscTitleIconOps oscTitleIconOps,
        EmulatorOps.TerminalOscWindowManipulationOps oscWindowManipulationOps,
        EmulatorOps.TerminalOscClipboardOps oscClipboardOps,
        EmulatorOps.TerminalOscHyperlinkOps oscHyperlinkOps,
        EmulatorOps.TerminalOscColorQueryOps oscColorQueryOps)
    BuildOscOps(
        TerminalEmulator emulator,
        AttributeManager attributeManager,
        TerminalState state,
        ILogger effectiveLogger)
    {
        var titleIconEventsOps = new EmulatorOps.TerminalTitleIconEventsOps(e => emulator.RaiseTitleChanged(e), e => emulator.RaiseIconNameChanged(e));
        var oscTitleIconOps = new EmulatorOps.TerminalOscTitleIconOps(() => state, titleIconEventsOps.OnTitleChanged, titleIconEventsOps.OnIconNameChanged);
        var oscWindowManipulationOps = new EmulatorOps.TerminalOscWindowManipulationOps(() => state, emulator.SetWindowTitle, emulator.SetIconName, emulator.EmitResponse, () => emulator.Height, () => emulator.Width, effectiveLogger);
        var oscClipboardOps = new EmulatorOps.TerminalOscClipboardOps(effectiveLogger, emulator.OnClipboardRequestInternal);
        var oscHyperlinkOps = new EmulatorOps.TerminalOscHyperlinkOps(effectiveLogger, attributeManager, () => state);
        var oscColorQueryOps = new EmulatorOps.TerminalOscColorQueryOps(attributeManager);

        return (titleIconEventsOps, oscTitleIconOps, oscWindowManipulationOps, oscClipboardOps, oscHyperlinkOps, oscColorQueryOps);
    }

    /// <summary>
    ///     Builds miscellaneous operation classes.
    /// </summary>
    private static (
        EmulatorOps.TerminalCharsetDesignationOps charsetDesignationOps,
        EmulatorOps.TerminalCharsetTranslationOps charsetTranslationOps,
        EmulatorOps.TerminalLineFeedOps lineFeedOps,
        EmulatorOps.TerminalIndexOps indexOps,
        EmulatorOps.TerminalCarriageReturnOps carriageReturnOps,
        EmulatorOps.TerminalBellOps bellOps,
        EmulatorOps.TerminalBackspaceOps backspaceOps,
        EmulatorOps.TerminalTabOps tabOps,
        EmulatorOps.TerminalResponseOps responseOps,
        EmulatorOps.TerminalInputOps inputOps,
        EmulatorOps.TerminalResetOps resetOps)
    BuildMiscOps(
        TerminalEmulator emulator,
        CursorManager cursorManager,
        ScreenBufferManager screenBufferManager,
        AttributeManager attributeManager,
        ModeManager modeManager,
        CharacterSetManager characterSetManager,
        TerminalState state,
        DualScreenBuffer screenBuffer,
        Cursor cursor,
        ILogger effectiveLogger)
    {
        var charsetDesignationOps = new EmulatorOps.TerminalCharsetDesignationOps(characterSetManager);
        var charsetTranslationOps = new EmulatorOps.TerminalCharsetTranslationOps(characterSetManager);
        var lineFeedOps = new EmulatorOps.TerminalLineFeedOps(cursorManager, screenBufferManager, attributeManager, () => state);
        var indexOps = new EmulatorOps.TerminalIndexOps(screenBufferManager, attributeManager, () => state, () => cursor, () => emulator.Height);
        var carriageReturnOps = new EmulatorOps.TerminalCarriageReturnOps(cursorManager, () => state);
        var bellOps = new EmulatorOps.TerminalBellOps(emulator.OnBellInternal);
        var backspaceOps = new EmulatorOps.TerminalBackspaceOps(cursorManager, () => state);
        var tabOps = new EmulatorOps.TerminalTabOps(cursorManager, () => state, () => cursor, () => emulator.Width);
        var responseOps = new EmulatorOps.TerminalResponseOps(e => emulator.OnResponseEmittedEvent(e));
        var inputOps = new EmulatorOps.TerminalInputOps(() => emulator.GetParser(), emulator.ThrowIfDisposedInternal, emulator.OnScreenUpdatedInternal);
        var resetOps = new EmulatorOps.TerminalResetOps(() => state, () => screenBuffer, () => cursor, cursorManager, attributeManager, modeManager, () => emulator.Width, () => emulator.Height, effectiveLogger);

        return (charsetDesignationOps, charsetTranslationOps, lineFeedOps, indexOps, carriageReturnOps, bellOps, backspaceOps, tabOps, responseOps, inputOps, resetOps);
    }
}
