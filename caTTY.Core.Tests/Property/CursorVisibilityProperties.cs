using caTTY.Core.Terminal;
using FsCheck;
using NUnit.Framework;

namespace caTTY.Core.Tests.Property;

/// <summary>
///     Property-based tests for cursor visibility tracking in the terminal emulator.
///     These tests verify universal properties that should hold for all cursor visibility operations.
///     Validates Requirements 8.5.
/// </summary>
[TestFixture]
[Category("Property")]
public class CursorVisibilityProperties
{
    /// <summary>
    ///     Generator for cursor visibility toggle sequences.
    ///     Generates sequences of DECTCEM (cursor visibility) control sequences.
    /// </summary>
    public static Arbitrary<bool[]> CursorVisibilitySequenceArb =>
        Arb.From(Gen.ArrayOf(Gen.Elements(true, false)).Where(arr => arr.Length > 0 && arr.Length <= 20));

    /// <summary>
    ///     **Feature: catty-ksa, Property 14: Cursor visibility tracking**
    ///     **Validates: Requirements 8.5**
    ///     Property: For any sequence of cursor visibility toggles, querying the cursor state should return the visibility value from the most recent toggle.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property CursorVisibilityTracking()
    {
        return Prop.ForAll(CursorVisibilitySequenceArb, visibilitySequence =>
        {
            // Arrange
            var terminal = TerminalEmulator.Create(80, 24);

            // Track the expected final visibility state
            bool expectedVisibility = true; // Default cursor visibility is true

            // Act: Apply each visibility toggle in sequence
            foreach (bool visible in visibilitySequence)
            {
                string sequence = visible ? "\x1b[?25h" : "\x1b[?25l"; // DECTCEM enable/disable
                terminal.Write(sequence);
                expectedVisibility = visible; // Track the most recent toggle
            }

            // Assert: Cursor visibility should match the most recent toggle
            bool actualVisibility = terminal.Cursor.Visible;

            // Additional verification: terminal should still be functional
            terminal.Write("Test");
            bool terminalFunctional = terminal.Cursor.Col > 0; // Cursor should have moved after writing

            terminal.Dispose();

            return actualVisibility == expectedVisibility && terminalFunctional;
        });
    }

    /// <summary>
    ///     Property: Cursor visibility state is preserved across cursor movements.
    ///     Moving the cursor should not affect its visibility state.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property CursorVisibilityPreservedAcrossMovements()
    {
        return Prop.ForAll(
            Arb.From(Gen.Elements(true, false)), // Initial visibility state
            Arb.From(Gen.Elements("\x1b[A", "\x1b[B", "\x1b[C", "\x1b[D", "\x1b[10;20H", "\x1b[H")), // Movement sequences
            (initialVisibility, movementSequence) =>
            {
                // Arrange
                var terminal = TerminalEmulator.Create(80, 24);

                // Set initial visibility state
                string visibilitySequence = initialVisibility ? "\x1b[?25h" : "\x1b[?25l";
                terminal.Write(visibilitySequence);

                // Verify initial state
                bool visibilityBeforeMovement = terminal.Cursor.Visible;

                // Act: Apply cursor movement
                terminal.Write(movementSequence);

                // Assert: Visibility should be preserved
                bool visibilityAfterMovement = terminal.Cursor.Visible;

                terminal.Dispose();

                return visibilityBeforeMovement == initialVisibility &&
                       visibilityAfterMovement == initialVisibility &&
                       visibilityBeforeMovement == visibilityAfterMovement;
            });
    }

    /// <summary>
    ///     Property: Cursor visibility state is preserved across character writing.
    ///     Writing characters should not affect cursor visibility state.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property CursorVisibilityPreservedAcrossCharacterWriting()
    {
        return Prop.ForAll(
            Arb.From(Gen.Elements(true, false)), // Initial visibility state
            Arb.From(Gen.Choose(1, 50).Select(n => new string('X', n))), // Text to write
            (initialVisibility, textToWrite) =>
            {
                // Arrange
                var terminal = TerminalEmulator.Create(80, 24);

                // Set initial visibility state
                string visibilitySequence = initialVisibility ? "\x1b[?25h" : "\x1b[?25l";
                terminal.Write(visibilitySequence);

                // Verify initial state
                bool visibilityBeforeWriting = terminal.Cursor.Visible;

                // Act: Write characters
                terminal.Write(textToWrite);

                // Assert: Visibility should be preserved
                bool visibilityAfterWriting = terminal.Cursor.Visible;

                terminal.Dispose();

                return visibilityBeforeWriting == initialVisibility &&
                       visibilityAfterWriting == initialVisibility &&
                       visibilityBeforeWriting == visibilityAfterWriting;
            });
    }

    /// <summary>
    ///     Property: Cursor visibility toggles are deterministic.
    ///     Applying the same sequence of visibility toggles should always produce the same result.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property CursorVisibilityTogglesDeterministic()
    {
        return Prop.ForAll(CursorVisibilitySequenceArb, visibilitySequence =>
        {
            // Arrange: Create two identical terminals
            var terminal1 = TerminalEmulator.Create(80, 24);
            var terminal2 = TerminalEmulator.Create(80, 24);

            // Act: Apply the same sequence to both terminals
            foreach (bool visible in visibilitySequence)
            {
                string sequence = visible ? "\x1b[?25h" : "\x1b[?25l";
                terminal1.Write(sequence);
                terminal2.Write(sequence);
            }

            // Assert: Both terminals should have the same cursor visibility
            bool visibility1 = terminal1.Cursor.Visible;
            bool visibility2 = terminal2.Cursor.Visible;

            terminal1.Dispose();
            terminal2.Dispose();

            return visibility1 == visibility2;
        });
    }

    /// <summary>
    ///     Property: Cursor visibility state is preserved across terminal resize.
    ///     Resizing the terminal should not affect cursor visibility state.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property CursorVisibilityPreservedAcrossResize()
    {
        return Prop.ForAll(
            Arb.From(Gen.Elements(true, false)), // Initial visibility state
            Arb.From(Gen.Choose(10, 120)), // New width
            Arb.From(Gen.Choose(5, 50)),   // New height
            (initialVisibility, newWidth, newHeight) =>
            {
                // Arrange
                var terminal = TerminalEmulator.Create(80, 24);

                // Set initial visibility state
                string visibilitySequence = initialVisibility ? "\x1b[?25h" : "\x1b[?25l";
                terminal.Write(visibilitySequence);

                // Verify initial state
                bool visibilityBeforeResize = terminal.Cursor.Visible;

                // Act: Resize terminal
                terminal.Resize(newWidth, newHeight);

                // Assert: Visibility should be preserved
                bool visibilityAfterResize = terminal.Cursor.Visible;

                terminal.Dispose();

                return visibilityBeforeResize == initialVisibility &&
                       visibilityAfterResize == initialVisibility &&
                       visibilityBeforeResize == visibilityAfterResize;
            });
    }
}
