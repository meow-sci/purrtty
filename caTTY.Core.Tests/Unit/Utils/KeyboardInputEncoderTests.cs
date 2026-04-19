using NUnit.Framework;
using caTTY.Core.Utils;

namespace caTTY.Core.Tests.Unit.Utils;

[TestFixture]
public class KeyboardInputEncoderTests
{
    [Test]
    public void EncodeKeyEvent_BasicKeys_ReturnsCorrectSequences()
    {
        var noModifiers = new KeyModifiers();
        
        // Basic keys
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("Enter", noModifiers, false), Is.EqualTo("\r"));
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("Backspace", noModifiers, false), Is.EqualTo("\x7f"));
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("Tab", noModifiers, false), Is.EqualTo("\t"));
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("Escape", noModifiers, false), Is.EqualTo("\x1b"));
    }

    [Test]
    public void EncodeKeyEvent_ArrowKeys_RespectsApplicationCursorKeysMode()
    {
        var noModifiers = new KeyModifiers();
        
        // Normal mode (application cursor keys disabled)
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("ArrowUp", noModifiers, false), Is.EqualTo("\x1b[A"));
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("ArrowDown", noModifiers, false), Is.EqualTo("\x1b[B"));
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("ArrowRight", noModifiers, false), Is.EqualTo("\x1b[C"));
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("ArrowLeft", noModifiers, false), Is.EqualTo("\x1b[D"));
        
        // Application cursor keys mode (enabled)
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("ArrowUp", noModifiers, true), Is.EqualTo("\x1bOA"));
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("ArrowDown", noModifiers, true), Is.EqualTo("\x1bOB"));
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("ArrowRight", noModifiers, true), Is.EqualTo("\x1bOC"));
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("ArrowLeft", noModifiers, true), Is.EqualTo("\x1bOD"));
    }

    [Test]
    public void EncodeKeyEvent_NavigationKeys_ReturnsCorrectSequences()
    {
        var noModifiers = new KeyModifiers();
        
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("Home", noModifiers, false), Is.EqualTo("\x1b[H"));
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("End", noModifiers, false), Is.EqualTo("\x1b[F"));
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("Delete", noModifiers, false), Is.EqualTo("\x1b[3~"));
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("Insert", noModifiers, false), Is.EqualTo("\x1b[2~"));
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("PageUp", noModifiers, false), Is.EqualTo("\x1b[5~"));
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("PageDown", noModifiers, false), Is.EqualTo("\x1b[6~"));
    }

    [Test]
    public void EncodeKeyEvent_FunctionKeysF1ToF4_WithoutModifiers()
    {
        var noModifiers = new KeyModifiers();
        
        // F1-F4 use SS3 format without modifiers
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("F1", noModifiers, false), Is.EqualTo("\x1bOP"));
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("F2", noModifiers, false), Is.EqualTo("\x1bOQ"));
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("F3", noModifiers, false), Is.EqualTo("\x1bOR"));
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("F4", noModifiers, false), Is.EqualTo("\x1bOS"));
    }

    [Test]
    public void EncodeKeyEvent_FunctionKeysF1ToF4_WithModifiers()
    {
        var shiftModifier = new KeyModifiers(shift: true);
        var ctrlModifier = new KeyModifiers(ctrl: true);
        var altModifier = new KeyModifiers(alt: true);
        
        // F1-F4 use CSI format with modifiers
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("F1", shiftModifier, false), Is.EqualTo("\x1b[1;2P"));
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("F2", ctrlModifier, false), Is.EqualTo("\x1b[1;5Q"));
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("F3", altModifier, false), Is.EqualTo("\x1b[1;3R"));
    }

    [Test]
    public void EncodeKeyEvent_FunctionKeysF5ToF12_WithoutModifiers()
    {
        var noModifiers = new KeyModifiers();
        
        // F5-F12 use CSI format
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("F5", noModifiers, false), Is.EqualTo("\x1b[15~"));
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("F6", noModifiers, false), Is.EqualTo("\x1b[17~"));
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("F7", noModifiers, false), Is.EqualTo("\x1b[18~"));
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("F8", noModifiers, false), Is.EqualTo("\x1b[19~"));
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("F9", noModifiers, false), Is.EqualTo("\x1b[20~"));
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("F10", noModifiers, false), Is.EqualTo("\x1b[21~"));
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("F11", noModifiers, false), Is.EqualTo("\x1b[23~"));
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("F12", noModifiers, false), Is.EqualTo("\x1b[24~"));
    }

    [Test]
    public void EncodeKeyEvent_FunctionKeysF5ToF12_WithModifiers()
    {
        var shiftModifier = new KeyModifiers(shift: true);
        var ctrlModifier = new KeyModifiers(ctrl: true);
        
        // F5-F12 use CSI format with modifiers
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("F5", shiftModifier, false), Is.EqualTo("\x1b[15;2~"));
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("F12", ctrlModifier, false), Is.EqualTo("\x1b[24;5~"));
    }

    [Test]
    public void EncodeKeyEvent_CtrlCombinations_ReturnsControlCharacters()
    {
        var ctrlModifier = new KeyModifiers(ctrl: true);
        
        // Common Ctrl combinations
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("c", ctrlModifier, false), Is.EqualTo("\x03")); // Ctrl+C
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("d", ctrlModifier, false), Is.EqualTo("\x04")); // Ctrl+D
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("z", ctrlModifier, false), Is.EqualTo("\x1a")); // Ctrl+Z
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("a", ctrlModifier, false), Is.EqualTo("\x01")); // Ctrl+A
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("w", ctrlModifier, false), Is.EqualTo("\x17")); // Ctrl+W
    }

    [Test]
    public void EncodeKeyEvent_AltCombinations_ReturnsEscapePrefixed()
    {
        var altModifier = new KeyModifiers(alt: true);
        
        // Alt combinations should be ESC-prefixed for printable ASCII
        var result1 = KeyboardInputEncoder.EncodeKeyEvent("a", altModifier, false);
        Assert.That(result1, Is.Not.Null);
        Assert.That(result1!.Length, Is.EqualTo(2));
        Assert.That((int)result1[0], Is.EqualTo(0x1b)); // ESC
        Assert.That(result1[1], Is.EqualTo('a'));
        
        var result2 = KeyboardInputEncoder.EncodeKeyEvent("x", altModifier, false);
        Assert.That(result2, Is.Not.Null);
        Assert.That(result2!.Length, Is.EqualTo(2));
        Assert.That((int)result2[0], Is.EqualTo(0x1b)); // ESC
        Assert.That(result2[1], Is.EqualTo('x'));
        
        var result3 = KeyboardInputEncoder.EncodeKeyEvent("1", altModifier, false);
        Assert.That(result3, Is.Not.Null);
        Assert.That(result3!.Length, Is.EqualTo(2));
        Assert.That((int)result3[0], Is.EqualTo(0x1b)); // ESC
        Assert.That(result3[1], Is.EqualTo('1'));
    }

    [Test]
    public void EncodeKeyEvent_PrintableCharacters_ReturnsAsIs()
    {
        var noModifiers = new KeyModifiers();
        
        // Single printable characters should be returned as-is
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("a", noModifiers, false), Is.EqualTo("a"));
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("Z", noModifiers, false), Is.EqualTo("Z"));
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("1", noModifiers, false), Is.EqualTo("1"));
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("@", noModifiers, false), Is.EqualTo("@"));
    }

    [Test]
    public void EncodeKeyEvent_NonTextKeys_ReturnsNull()
    {
        var noModifiers = new KeyModifiers();
        
        // Non-text keys should return null
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("Shift", noModifiers, false), Is.Null);
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("Control", noModifiers, false), Is.Null);
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("Alt", noModifiers, false), Is.Null);
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("CapsLock", noModifiers, false), Is.Null);
    }

    [Test]
    public void EncodeKeyEvent_MetaKey_IgnoresInput()
    {
        var metaModifier = new KeyModifiers(meta: true);
        
        // Meta key combinations should be ignored (return null)
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("c", metaModifier, false), Is.Null);
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("v", metaModifier, false), Is.Null);
    }

    [Test]
    public void EncodeKeyEvent_CtrlWithMeta_IgnoresInput()
    {
        var ctrlMetaModifier = new KeyModifiers(ctrl: true, meta: true);
        
        // Ctrl+Meta combinations should be ignored to allow browser/OS shortcuts
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("c", ctrlMetaModifier, false), Is.Null);
        Assert.That(KeyboardInputEncoder.EncodeKeyEvent("v", ctrlMetaModifier, false), Is.Null);
    }

    [Test]
    public void KeyModifiers_Constructor_SetsPropertiesCorrectly()
    {
        var modifiers = new KeyModifiers(shift: true, alt: true, ctrl: false, meta: true);
        
        Assert.That(modifiers.Shift, Is.True);
        Assert.That(modifiers.Alt, Is.True);
        Assert.That(modifiers.Ctrl, Is.False);
        Assert.That(modifiers.Meta, Is.True);
    }

    [Test]
    public void KeyModifiers_DefaultConstructor_AllFalse()
    {
        var modifiers = new KeyModifiers();
        
        Assert.That(modifiers.Shift, Is.False);
        Assert.That(modifiers.Alt, Is.False);
        Assert.That(modifiers.Ctrl, Is.False);
        Assert.That(modifiers.Meta, Is.False);
    }
}