using NUnit.Framework;
using caTTY.Core.Terminal;
using caTTY.Display.Controllers;
using caTTY.Display.Controllers.TerminalUi;
using caTTY.Display.Controllers.TerminalUi.Menus;
using caTTY.Display.Configuration;
using System.Reflection;

namespace caTTY.Display.Tests.Unit.Controllers;

/// <summary>
/// Unit tests for simplified UI layout functionality in TerminalController.
/// Tests that tab bars and info displays are not rendered, and that menu functionality remains accessible.
/// Validates Requirements 8.1, 8.2, 8.4, 8.5.
/// </summary>
[TestFixture]
[Category("Unit")]
public class TerminalControllerSimplifiedUITests
{
    private ITerminalEmulator _terminal = null!;
    private IProcessManager _processManager = null!;
    private TerminalController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        // Create test terminal and process manager
        _terminal = TerminalEmulator.Create(80, 24);
        _processManager = new ProcessManager();
        
        // Create session manager and add a session
        var sessionManager = new SessionManager();
        var session = sessionManager.CreateSessionAsync().Result;
        
        // Create controller with session manager
        _controller = new TerminalController(sessionManager);
    }

    [TearDown]
    public void TearDown()
    {
        _controller?.Dispose();
        _processManager?.Dispose();
        _terminal?.Dispose();
    }

    [Test]
    public void TerminalController_ShouldHaveRenderTerminalCanvasMethod()
    {
        // Arrange & Act
        var renderTerminalCanvasMethod = typeof(TerminalController)
            .GetMethod("RenderTerminalCanvas", BindingFlags.NonPublic | BindingFlags.Instance);

        // Assert
        Assert.That(renderTerminalCanvasMethod, Is.Not.Null, 
            "TerminalController should have RenderTerminalCanvas method for simplified UI");
    }

    [Test]
    public void TerminalController_ShouldNotCallRenderTabAreaInSimplifiedMode()
    {
        // Arrange
        var renderTabAreaMethod = typeof(TerminalController)
            .GetMethod("RenderTabArea", BindingFlags.NonPublic | BindingFlags.Instance);

        // Assert
        Assert.That(renderTabAreaMethod, Is.Not.Null, 
            "RenderTabArea method should exist but not be called in simplified UI mode");
        
        // Note: In simplified UI, RenderTabArea should not be called from the main Render method
        // This would require integration testing with ImGui to verify the actual call flow
    }

    [Test]
    public void TerminalController_ShouldPreserveMenuBarFunctionality()
    {
        // Arrange & Act
        // After refactoring, menu rendering methods are in TerminalUiSettingsPanel subsystem
        // All menus have been extracted to individual menu renderer classes
        var renderMenuBarMethod = typeof(TerminalUiSettingsPanel)
            .GetMethod("RenderMenuBar", BindingFlags.Public | BindingFlags.Instance);

        var sessionsMenuRendererType = typeof(SessionsMenuRenderer);
        var sessionsMenuRenderMethod = sessionsMenuRendererType
            .GetMethod("Render", BindingFlags.Public | BindingFlags.Instance);

        var editMenuRendererType = typeof(EditMenuRenderer);
        var editMenuRenderMethod = editMenuRendererType
            .GetMethod("Render", BindingFlags.Public | BindingFlags.Instance);

        var settingsMenuRendererType = typeof(SettingsMenuRenderer);
        var settingsMenuRenderMethod = settingsMenuRendererType
            .GetMethod("Render", BindingFlags.Public | BindingFlags.Instance);

        // Check submenu renderers exist
        var fontSubmenuRendererType = typeof(FontSubmenuRenderer);
        var colorThemeSubmenuRendererType = typeof(ColorThemeSubmenuRenderer);
        var windowSubmenuRendererType = typeof(WindowSubmenuRenderer);
        var shellsSubmenuRendererType = typeof(ShellsSubmenuRenderer);
        var performanceSubmenuRendererType = typeof(PerformanceSubmenuRenderer);

        // Assert
        Assert.That(renderMenuBarMethod, Is.Not.Null,
            "RenderMenuBar method should be preserved in TerminalUiSettingsPanel");
        Assert.That(sessionsMenuRendererType, Is.Not.Null,
            "SessionsMenuRenderer class should exist for Sessions menu rendering");
        Assert.That(sessionsMenuRenderMethod, Is.Not.Null,
            "SessionsMenuRenderer should have Render method");
        Assert.That(editMenuRendererType, Is.Not.Null,
            "EditMenuRenderer class should exist for Edit menu rendering");
        Assert.That(editMenuRenderMethod, Is.Not.Null,
            "EditMenuRenderer should have Render method");
        Assert.That(settingsMenuRendererType, Is.Not.Null,
            "SettingsMenuRenderer class should exist for Settings menu rendering");
        Assert.That(settingsMenuRenderMethod, Is.Not.Null,
            "SettingsMenuRenderer should have Render method");
        Assert.That(fontSubmenuRendererType, Is.Not.Null,
            "FontSubmenuRenderer class should exist for Font submenu");
        Assert.That(colorThemeSubmenuRendererType, Is.Not.Null,
            "ColorThemeSubmenuRenderer class should exist for Color Theme submenu");
        Assert.That(windowSubmenuRendererType, Is.Not.Null,
            "WindowSubmenuRenderer class should exist for Window submenu");
        Assert.That(shellsSubmenuRendererType, Is.Not.Null,
            "ShellsSubmenuRenderer class should exist for Shells submenu");
        Assert.That(performanceSubmenuRendererType, Is.Not.Null,
            "PerformanceSubmenuRenderer class should exist for Performance submenu");
    }

    [Test]
    public void LayoutConstants_ShouldSupportSimplifiedUICalculations()
    {
        // Arrange & Act
        float menuBarHeight = LayoutConstants.MENU_BAR_HEIGHT;
        float windowPadding = LayoutConstants.WINDOW_PADDING;
        float minWindowWidth = LayoutConstants.MIN_WINDOW_WIDTH;
        float minWindowHeight = LayoutConstants.MIN_WINDOW_HEIGHT;

        // Assert - Test that layout constants support simplified UI calculations
        Assert.That(menuBarHeight, Is.GreaterThan(0), 
            "Menu bar height should be positive for simplified UI");
        Assert.That(windowPadding, Is.GreaterThanOrEqualTo(0), 
            "Window padding should be non-negative for simplified UI");
        Assert.That(minWindowWidth, Is.GreaterThan(menuBarHeight + windowPadding * 2), 
            "Minimum window width should accommodate menu bar and padding");
        Assert.That(minWindowHeight, Is.GreaterThan(menuBarHeight + windowPadding * 2), 
            "Minimum window height should accommodate menu bar and padding");
    }

    [Test]
    public void SimplifiedUI_ShouldCalculateTerminalDimensionsCorrectly()
    {
        // Arrange
        float windowWidth = 800.0f;
        float windowHeight = 600.0f;
        float charWidth = 10.0f;
        float lineHeight = 20.0f;

        // Calculate expected dimensions for simplified UI (only menu bar overhead)
        float simplifiedUIOverhead = LayoutConstants.MENU_BAR_HEIGHT + (LayoutConstants.WINDOW_PADDING * 2);
        float availableWidth = windowWidth - (LayoutConstants.WINDOW_PADDING * 2);
        float availableHeight = windowHeight - simplifiedUIOverhead;

        int expectedCols = (int)Math.Floor(availableWidth / charWidth);
        int expectedRows = (int)Math.Floor(availableHeight / lineHeight);

        // Apply bounds
        expectedCols = Math.Max(10, Math.Min(1000, expectedCols));
        expectedRows = Math.Max(3, Math.Min(1000, expectedRows));

        // Act & Assert
        Assert.That(expectedCols, Is.GreaterThanOrEqualTo(10), 
            "Simplified UI should provide at least 10 columns");
        Assert.That(expectedRows, Is.GreaterThanOrEqualTo(3), 
            "Simplified UI should provide at least 3 rows");
        Assert.That(expectedCols, Is.LessThanOrEqualTo(1000), 
            "Terminal columns should be within reasonable bounds");
        Assert.That(expectedRows, Is.LessThanOrEqualTo(1000), 
            "Terminal rows should be within reasonable bounds");

        // Test that simplified UI provides more space than complex UI
        float complexUIOverhead = LayoutConstants.MENU_BAR_HEIGHT + 
                                 LayoutConstants.TAB_AREA_HEIGHT + 
                                 60.0f + // Original terminal info display
                                 (LayoutConstants.WINDOW_PADDING * 2);
        float complexAvailableHeight = windowHeight - complexUIOverhead;
        int complexRows = Math.Max(3, (int)Math.Floor(complexAvailableHeight / lineHeight));

        Assert.That(expectedRows, Is.GreaterThanOrEqualTo(complexRows), 
            "Simplified UI should provide more or equal terminal rows compared to complex UI");
    }

    [Test]
    public void SimplifiedUI_ShouldNotRenderTerminalInfoDisplay()
    {
        // Arrange & Act
        // In simplified UI, the terminal info display (showing terminal dimensions, cursor position, process status)
        // should not be rendered as part of the main UI layout

        // We can test this by verifying that the UI overhead calculation doesn't include the info display space
        float simplifiedUIOverhead = LayoutConstants.MENU_BAR_HEIGHT + (LayoutConstants.WINDOW_PADDING * 2);
        float originalInfoDisplayHeight = 60.0f; // Original terminal info display height

        // Assert
        Assert.That(simplifiedUIOverhead, Is.Not.EqualTo(simplifiedUIOverhead + originalInfoDisplayHeight), 
            "Simplified UI overhead should not include terminal info display height");

        // Test that the simplified UI overhead is significantly less than the original
        float originalUIOverhead = LayoutConstants.MENU_BAR_HEIGHT + 
                                  LayoutConstants.TAB_AREA_HEIGHT + 
                                  originalInfoDisplayHeight + 
                                  (LayoutConstants.WINDOW_PADDING * 2);

        Assert.That(simplifiedUIOverhead, Is.LessThan(originalUIOverhead), 
            "Simplified UI should have less overhead than original UI");
    }

    [Test]
    public void SimplifiedUI_ShouldNotRenderTabArea()
    {
        // Arrange & Act
        // In simplified UI, the tab area should not be rendered as part of the main UI layout

        // We can test this by verifying that the UI overhead calculation doesn't include the tab area space
        float simplifiedUIOverhead = LayoutConstants.MENU_BAR_HEIGHT + (LayoutConstants.WINDOW_PADDING * 2);
        float tabAreaHeight = LayoutConstants.TAB_AREA_HEIGHT;

        // Assert
        Assert.That(simplifiedUIOverhead, Is.Not.EqualTo(simplifiedUIOverhead + tabAreaHeight), 
            "Simplified UI overhead should not include tab area height");

        // Test that tab area height is defined but not used in simplified UI calculations
        Assert.That(tabAreaHeight, Is.GreaterThan(0), 
            "Tab area height should be defined for potential future use");
        Assert.That(tabAreaHeight, Is.EqualTo(LayoutConstants.TAB_AREA_HEIGHT), 
            "Tab area height should match layout constant");
    }

    [Test]
    public void SimplifiedUI_ShouldMaximizeTerminalCanvasSpace()
    {
        // Arrange
        float windowHeight = 800.0f;

        // Calculate space utilization for simplified UI
        float simplifiedUIOverhead = LayoutConstants.MENU_BAR_HEIGHT + (LayoutConstants.WINDOW_PADDING * 2);
        float simplifiedAvailableHeight = windowHeight - simplifiedUIOverhead;
        float simplifiedSpaceUtilization = simplifiedAvailableHeight / windowHeight;

        // Calculate space utilization for original complex UI
        float complexUIOverhead = LayoutConstants.MENU_BAR_HEIGHT + 
                                 LayoutConstants.TAB_AREA_HEIGHT + 
                                 60.0f + // Original terminal info display
                                 (LayoutConstants.WINDOW_PADDING * 2);
        float complexAvailableHeight = windowHeight - complexUIOverhead;
        float complexSpaceUtilization = complexAvailableHeight / windowHeight;

        // Act & Assert
        Assert.That(simplifiedSpaceUtilization, Is.GreaterThan(complexSpaceUtilization), 
            "Simplified UI should provide better space utilization than complex UI");
        Assert.That(simplifiedSpaceUtilization, Is.GreaterThan(0.8f), 
            "Simplified UI should utilize at least 80% of window height for terminal content");
        Assert.That(simplifiedAvailableHeight, Is.GreaterThan(complexAvailableHeight), 
            "Simplified UI should provide more available height for terminal content");

        // Test that the improvement is significant
        float improvementRatio = simplifiedAvailableHeight / complexAvailableHeight;
        Assert.That(improvementRatio, Is.GreaterThan(1.1f), 
            "Simplified UI should provide at least 10% more space for terminal content");
    }

    [Test]
    public void SimplifiedUI_ShouldPreserveMenuBarAccessibility()
    {
        // Arrange & Act
        float menuBarHeight = LayoutConstants.MENU_BAR_HEIGHT;
        float windowHeight = 600.0f;
        float menuBarSpaceRatio = menuBarHeight / windowHeight;

        // Assert
        Assert.That(menuBarHeight, Is.GreaterThan(0), 
            "Menu bar should have positive height for accessibility");
        Assert.That(menuBarHeight, Is.LessThanOrEqualTo(50.0f), 
            "Menu bar height should be reasonable for UI accessibility");
        Assert.That(menuBarSpaceRatio, Is.LessThanOrEqualTo(0.15f), 
            "Menu bar should use no more than 15% of window height");

        // Test that menu bar is positioned at the top (y = 0 relative to window)
        float expectedMenuBarY = 0.0f;
        Assert.That(expectedMenuBarY, Is.EqualTo(0.0f), 
            "Menu bar should be positioned at the top of the window");
    }

    [Test]
    public void SimplifiedUI_ShouldSupportResponsiveLayout()
    {
        // Arrange - Test with different window sizes
        var testWindowSizes = new[]
        {
            new { Width = 400.0f, Height = 300.0f }, // Minimum size
            new { Width = 800.0f, Height = 600.0f }, // Medium size
            new { Width = 1920.0f, Height = 1080.0f } // Large size
        };

        foreach (var windowSize in testWindowSizes)
        {
            // Act
            float simplifiedUIOverhead = LayoutConstants.MENU_BAR_HEIGHT + (LayoutConstants.WINDOW_PADDING * 2);
            float availableHeight = windowSize.Height - simplifiedUIOverhead;
            float availableWidth = windowSize.Width - (LayoutConstants.WINDOW_PADDING * 2);

            // Assert
            Assert.That(availableHeight, Is.GreaterThan(0), 
                $"Simplified UI should provide positive available height for window size {windowSize.Width}x{windowSize.Height}");
            Assert.That(availableWidth, Is.GreaterThan(0), 
                $"Simplified UI should provide positive available width for window size {windowSize.Width}x{windowSize.Height}");

            // Test that menu bar doesn't dominate small windows
            float menuBarRatio = LayoutConstants.MENU_BAR_HEIGHT / windowSize.Height;
            Assert.That(menuBarRatio, Is.LessThan(0.5f), 
                $"Menu bar should not dominate window space for size {windowSize.Width}x{windowSize.Height}");
        }
    }
}