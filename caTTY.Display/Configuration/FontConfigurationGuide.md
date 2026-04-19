# Font Configuration Guide

This guide explains the different font configuration options available in the caTTY terminal emulator and when to use each approach.

## Configuration Options

The caTTY terminal emulator supports three different font configuration approaches:

### 1. Explicit Configuration (Recommended for Production)

Use explicit configuration when you need precise control over font selection and want predictable behavior across different environments.

#### TestApp Example
```csharp
// Create explicit font configuration for TestApp
var fontConfig = TerminalFontConfig.CreateForTestApp();
var controller = new TerminalController(terminal, processManager, fontConfig);
```

#### GameMod Example
```csharp
// Create explicit font configuration for GameMod
var fontConfig = TerminalFontConfig.CreateForGameMod();
var controller = new TerminalController(terminal, processManager, fontConfig);
```

#### Custom Configuration Example
```csharp
// Create custom font configuration
var fontConfig = new TerminalFontConfig
{
    RegularFontName = "MyCustomFont-Regular",
    BoldFontName = "MyCustomFont-Bold",
    ItalicFontName = "MyCustomFont-Italic",
    BoldItalicFontName = "MyCustomFont-BoldItalic",
    FontSize = 18.0f,
    AutoDetectContext = false
};
var controller = new TerminalController(terminal, processManager, fontConfig);
```

**When to use explicit configuration:**
- Production deployments where consistency is critical
- When you need specific font families or sizes
- When you want to avoid runtime detection overhead
- When you need to ensure specific fonts are used regardless of environment

### 2. Automatic Detection (Convenient for Development)

Use automatic detection when you want the system to choose appropriate fonts based on the execution context.

#### Automatic Detection Example
```csharp
// Let the system automatically detect context and choose appropriate fonts
var controller = new TerminalController(terminal, processManager);
```

#### Alternative Explicit Automatic Detection
```csharp
// Explicitly request automatic detection
var fontConfig = FontContextDetector.DetectAndCreateConfig();
var controller = new TerminalController(terminal, processManager, fontConfig);
```

**When to use automatic detection:**
- Development and testing scenarios
- When you want different font settings for TestApp vs GameMod automatically
- When you don't have specific font requirements
- For rapid prototyping and experimentation

### 3. Hybrid Configuration (Advanced)

Combine automatic detection with custom overrides for maximum flexibility.

#### Hybrid Configuration Example
```csharp
// Start with automatic detection, then customize
var fontConfig = FontContextDetector.DetectAndCreateConfig();
fontConfig.FontSize = 20.0f; // Override just the font size
fontConfig.RegularFontName = "MyPreferredFont-Regular"; // Override font family
var controller = new TerminalController(terminal, processManager, fontConfig);
```

**When to use hybrid configuration:**
- When you want context-aware defaults with specific customizations
- When you need different font sizes but want automatic font family selection
- For advanced scenarios requiring fine-tuned control

## Context Detection Details

The automatic detection system uses the following logic:

1. **GameMod Context Detection:**
   - Looks for KSA-related assemblies (KSA, Kitten, Space, Agency, BRUTAL, StarMap)
   - Uses smaller font size (14.0f) optimized for game integration
   - Applies game-appropriate font settings

2. **TestApp Context Detection:**
   - Looks for TestApp or Playground assemblies
   - Uses larger font size (16.0f) optimized for development
   - Applies development-friendly font settings

3. **Fallback Behavior:**
   - If context cannot be determined, defaults to TestApp settings
   - Provides safe fallback fonts if specified fonts are unavailable

## Font Loading and Fallback

The font system includes comprehensive fallback logic:

1. **Primary:** Use specified font configuration if valid and fonts are available
2. **Secondary:** Use automatic detection based on execution context
3. **Tertiary:** Use ImGui default font system
4. **Final:** Use hardcoded safe defaults (HackNerdFontMono-Regular, 16.0f size)

## Runtime Font Updates

All configuration approaches support runtime font updates:

```csharp
// Update font configuration at runtime
var newFontConfig = new TerminalFontConfig
{
    RegularFontName = "NewFont-Regular",
    FontSize = 14.0f
};
controller.UpdateFontConfig(newFontConfig);
```

## Best Practices

### For Production Applications
- Use explicit configuration with `CreateForTestApp()` or `CreateForGameMod()`
- Validate that required fonts are available before deployment
- Test font rendering across different DPI settings
- Include fallback fonts in your deployment

### For Development and Testing
- Use automatic detection for convenience: `new TerminalController(terminal, processManager)`
- Enable logging to verify correct font detection
- Test both TestApp and GameMod contexts to ensure proper detection

### For Custom Applications
- Start with automatic detection, then customize as needed
- Use hybrid configuration for context-aware customization
- Implement proper error handling for font loading failures
- Consider DPI scaling requirements for your target environment

## Troubleshooting

### Font Not Found
If specified fonts are not available:
1. Check that font files are properly loaded in ImGui
2. Verify font names match exactly (case-sensitive)
3. Check console output for font loading warnings
4. Use fallback fonts or automatic detection

### Context Detection Issues
If automatic detection chooses wrong context:
1. Use explicit configuration instead of automatic detection
2. Check console output for context detection logging
3. Verify assembly names match expected patterns
4. Consider using hybrid configuration with manual overrides

### Performance Considerations
- Automatic detection has minimal runtime overhead
- Font loading occurs once during initialization
- Runtime font updates trigger immediate reloading and metric recalculation
- Consider caching font configurations for frequently created controllers