using System;
using Brutal.ImGuiApi;
using caTTY.Display.Configuration;
using caTTY.Display.Controllers.TerminalUi.Fonts;
using caTTY.Display.Utils;

namespace caTTY.Display.Controllers.TerminalUi;

/// <summary>
///     Handles font loading, metrics calculation, and font size adjustments for the terminal UI.
/// </summary>
internal class TerminalUiFonts
{
  private readonly TerminalRenderingConfig _config;
  private readonly Performance.PerformanceStopwatch _perfWatch;
  private TerminalFontConfig _fontConfig;

  // Font loader handles font discovery and loading
  private FontLoader _fontLoader;

  // Font metrics calculator handles character width/height calculations
  private FontMetricsCalculator _metricsCalculator;

  // Font family selector handles font family selection and state
  private FontFamilySelector _familySelector;

  // Font config persistence handles saving/loading font configuration
  private readonly FontConfigPersistence _configPersistence;

  // Font loading state
  private bool _fontsLoaded = false;

  // Current metrics (delegated to FontMetricsCalculator)
  public float CurrentCharacterWidth => _metricsCalculator.CurrentCharacterWidth;
  public float CurrentLineHeight => _metricsCalculator.CurrentLineHeight;
  public float CurrentFontSize { get; private set; }

  public TerminalUiFonts(TerminalRenderingConfig config, TerminalFontConfig fontConfig, string currentFontFamily, Performance.PerformanceStopwatch perfWatch)
  {
    _config = config ?? throw new ArgumentNullException(nameof(config));
    _fontConfig = fontConfig ?? throw new ArgumentNullException(nameof(fontConfig));
    _perfWatch = perfWatch ?? throw new ArgumentNullException(nameof(perfWatch));

    // Initialize font loader
    _fontLoader = new FontLoader(_fontConfig);

    // Initialize metrics calculator
    _metricsCalculator = new FontMetricsCalculator(_config);

    // Initialize font family selector
    _familySelector = new FontFamilySelector(_fontConfig, currentFontFamily ?? throw new ArgumentNullException(nameof(currentFontFamily)));

    // Initialize config persistence
    _configPersistence = new FontConfigPersistence();

    CurrentFontSize = _fontConfig.FontSize;
  }

  // Public properties for accessing current font configuration
  public TerminalFontConfig CurrentFontConfig => _fontConfig;
  public string CurrentRegularFontName => _fontConfig.RegularFontName;
  public string CurrentBoldFontName => _fontConfig.BoldFontName;
  public string CurrentItalicFontName => _fontConfig.ItalicFontName;
  public string CurrentBoldItalicFontName => _fontConfig.BoldItalicFontName;
  public string CurrentFontFamily => _familySelector.CurrentFontFamily;

  /// <summary>
  ///     Ensures fonts are loaded before rendering.
  /// </summary>
  public void EnsureFontsLoaded()
  {
//    _perfWatch.Start("TerminalUiFonts.EnsureFontsLoaded");
    try
    {
      if (_fontsLoaded)
      {
        return;
      }

      try
      {
        // Load fonts from ImGui font system
        LoadFonts();

        // Calculate character metrics from loaded fonts
        CalculateCharacterMetrics();

        // Log configuration for debugging
        LogFontConfiguration();

        _fontsLoaded = true;
      }
      catch (Exception ex)
      {
        Console.WriteLine($"Font loading error: {ex.Message}");

        // Set fallback values to prevent crashes
        _metricsCalculator.ResetToDefaults();

        // Mark as loaded to prevent repeated attempts
        _fontsLoaded = true;
      }
    }
    finally
    {
//      _perfWatch.Stop("TerminalUiFonts.EnsureFontsLoaded");
    }
  }

  private void LoadFonts() => _fontLoader.LoadFonts();

  private void CalculateCharacterMetrics() => _metricsCalculator.CalculateCharacterMetrics(_fontLoader.RegularFont, _fontConfig.FontSize);

  /// <summary>
  ///     Selects the appropriate font based on SGR attributes.
  /// </summary>
  /// <param name="attributes">The SGR attributes of the character</param>
  /// <returns>The appropriate font pointer for the attributes</returns>
  public ImFontPtr SelectFont(Core.Types.SgrAttributes attributes)
  {
//    _perfWatch.Start("TerminalUiFonts.SelectFont");
    try
    {
      if (attributes.Bold && attributes.Italic)
        return _fontLoader.BoldItalicFont;
      else if (attributes.Bold)
        return _fontLoader.BoldFont;
      else if (attributes.Italic)
        return _fontLoader.ItalicFont;
      else
        return _fontLoader.RegularFont;
    }
    finally
    {
//      _perfWatch.Stop("TerminalUiFonts.SelectFont");
    }
  }

  private void LogFontConfiguration() => Console.WriteLine($"Font Config: {_fontConfig.RegularFontName} @ {_fontConfig.FontSize}pt, CharWidth: {CurrentCharacterWidth:F1}, LineHeight: {CurrentLineHeight:F1}");

  public void PushUIFont(out bool fontUsed) => _fontLoader.PushUIFont(out fontUsed);

  public void PushTerminalContentFont(out bool fontUsed) => _fontLoader.PushTerminalContentFont(out fontUsed);

  // DEPRECATED: Use PushUIFont() for UI elements or PushTerminalContentFont() for terminal content.
  public void PushMonospaceFont(out bool fontUsed) => PushTerminalContentFont(out fontUsed);

  public static void MaybePopFont(bool wasUsed)
  {
    if (wasUsed) ImGui.PopFont();
  }

  public void IncreaseFontSize(Action onFontChanged)
  {
    var newSize = Math.Min(LayoutConstants.MAX_FONT_SIZE, _fontConfig.FontSize + 1.0f);
    UpdateFontConfig(CreateFontConfigWithNewSize(newSize), onFontChanged);
  }

  public void DecreaseFontSize(Action onFontChanged)
  {
    var newSize = Math.Max(LayoutConstants.MIN_FONT_SIZE, _fontConfig.FontSize - 1.0f);
    UpdateFontConfig(CreateFontConfigWithNewSize(newSize), onFontChanged);
  }

  private TerminalFontConfig CreateFontConfigWithNewSize(float newSize)
  {
    return new TerminalFontConfig
    {
      FontSize = newSize,
      RegularFontName = _fontConfig.RegularFontName,
      BoldFontName = _fontConfig.BoldFontName,
      ItalicFontName = _fontConfig.ItalicFontName,
      BoldItalicFontName = _fontConfig.BoldItalicFontName,
      AutoDetectContext = _fontConfig.AutoDetectContext
    };
  }

  public void UpdateFontConfig(TerminalFontConfig newFontConfig, Action onFontChanged)
  {
    if (newFontConfig == null) throw new ArgumentNullException(nameof(newFontConfig));

    newFontConfig.Validate();

    _fontConfig = newFontConfig;
    _fontLoader = new FontLoader(_fontConfig);
    _familySelector = new FontFamilySelector(_fontConfig, _familySelector.CurrentFontFamily);
    _fontsLoaded = false;

    LoadFonts();
    CalculateCharacterMetrics();
    CurrentFontSize = _fontConfig.FontSize;

    onFontChanged?.Invoke();
    Console.WriteLine($"Font updated: {_fontConfig.RegularFontName} @ {_fontConfig.FontSize}pt");
    LogFontConfiguration();
  }

  public void SelectFontFamily(string displayName, Action onFontChanged)
  {
    var newFontConfig = _familySelector.CreateFontConfigForFamily(displayName, _fontConfig.FontSize);
    UpdateFontConfig(newFontConfig, onFontChanged);
    _familySelector.UpdateCurrentFontFamily(displayName);
  }

  public void LoadFontSettingsInConstructor()
  {
    (_fontConfig, _fontLoader, _familySelector) = _configPersistence.LoadFontSettingsInConstructor(_fontConfig, _fontLoader, _familySelector);
  }

  public void InitializeCurrentFontFamily() => _familySelector.InitializeCurrentFontFamily();

  public void LoadFontSettings(Action onFontChanged)
  {
    bool fontConfigChanged;
    (_fontConfig, _fontLoader, _familySelector, fontConfigChanged) = _configPersistence.LoadFontSettings(_fontConfig, _fontLoader, _familySelector);

    if (fontConfigChanged)
    {
      _fontsLoaded = false;
      onFontChanged?.Invoke();
    }
  }

  public void SaveFontSettings() => _configPersistence.SaveFontSettings(_familySelector, _fontConfig);
}
