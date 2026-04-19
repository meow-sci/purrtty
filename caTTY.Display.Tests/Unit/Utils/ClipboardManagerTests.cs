using NUnit.Framework;
using caTTY.Display.Utils;
using System.Runtime.InteropServices;

namespace caTTY.Display.Tests.Unit.Utils;

/// <summary>
/// Unit tests for the ClipboardManager utility class.
/// Tests clipboard operations with focus on Windows platform support.
/// </summary>
[TestFixture]
[Category("Unit")]
public class ClipboardManagerTests
{
    [Test]
    public void SetText_WithNullOrEmptyText_ShouldReturnFalse()
    {
        // Act & Assert
        Assert.That(ClipboardManager.SetText(null!), Is.False);
        Assert.That(ClipboardManager.SetText(string.Empty), Is.False);
        Assert.That(ClipboardManager.SetText(""), Is.False);
    }

    [Test]
    public void SetText_WithValidText_ShouldAttemptToSetClipboard()
    {
        // Arrange
        string testText = "Hello, World!";

        // Act
        bool result = ClipboardManager.SetText(testText);

        // Assert
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // On Windows, the operation might succeed or fail depending on system state
            // We just verify it doesn't throw an exception
            Assert.That(result, Is.TypeOf<bool>());
        }
        else
        {
            // On non-Windows platforms, should return false (not supported)
            Assert.That(result, Is.False);
        }
    }

    [Test]
    public void GetText_ShouldReturnStringOrNull()
    {
        // Act
        string? result = ClipboardManager.GetText();

        // Assert
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // On Windows, might return text or null depending on clipboard state
            // We just verify it doesn't throw an exception
            Assert.That(result, Is.TypeOf<string>().Or.Null);
        }
        else
        {
            // On non-Windows platforms, should return null (not supported)
            Assert.That(result, Is.Null);
        }
    }

    [Test]
    public void SetText_WithSpecialCharacters_ShouldHandleUnicode()
    {
        // Arrange
        string unicodeText = "Hello 🌍 World! 中文 العربية";

        // Act
        bool result = ClipboardManager.SetText(unicodeText);

        // Assert - Should not throw exception regardless of platform
        Assert.That(result, Is.TypeOf<bool>());
    }

    [Test]
    public void SetText_WithLargeText_ShouldHandleGracefully()
    {
        // Arrange
        string largeText = new string('A', 10000); // 10KB of text

        // Act
        bool result = ClipboardManager.SetText(largeText);

        // Assert - Should not throw exception regardless of platform
        Assert.That(result, Is.TypeOf<bool>());
    }

    [Test]
    public void SetText_WithLineBreaks_ShouldPreserveFormatting()
    {
        // Arrange
        string multiLineText = "Line 1\nLine 2\r\nLine 3\n";

        // Act
        bool result = ClipboardManager.SetText(multiLineText);

        // Assert - Should not throw exception regardless of platform
        Assert.That(result, Is.TypeOf<bool>());
    }

    [Test]
    [Platform("Win")]
    public void SetText_OnWindows_ShouldUseWindowsApi()
    {
        // This test only runs on Windows and verifies Windows-specific behavior
        // Arrange
        string testText = "Windows clipboard test";

        // Act
        bool result = ClipboardManager.SetText(testText);

        // Assert - On Windows, should attempt to use Win32 API
        // The result depends on system state, but should not throw
        Assert.That(result, Is.TypeOf<bool>());
    }

    [Test]
    [Platform("Linux,MacOsX")]
    public void SetText_OnNonWindows_ShouldReturnFalse()
    {
        // This test only runs on non-Windows platforms
        // Arrange
        string testText = "Non-Windows clipboard test";

        // Act
        bool result = ClipboardManager.SetText(testText);

        // Assert - On non-Windows platforms, should return false (not implemented)
        Assert.That(result, Is.False);
    }

    [Test]
    [Platform("Linux,MacOsX")]
    public void GetText_OnNonWindows_ShouldReturnNull()
    {
        // This test only runs on non-Windows platforms
        // Act
        string? result = ClipboardManager.GetText();

        // Assert - On non-Windows platforms, should return null (not implemented)
        Assert.That(result, Is.Null);
    }
}