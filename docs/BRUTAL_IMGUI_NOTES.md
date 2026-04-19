# Fonts

Fonts will be loaded automatically.  Any font file ending in ".ttf" in a `Content/` folder in the pwd of the program will be loaded, the "name" it can be looked up by at runtime matches the filename before the `.ttf` filename extension.

For example file `Content/Hack.ttf` can be referenced in code by the name `Hack`

To avoid KSA from auto loading our font files, they will be named with a `.iamttf` extension and a custom LoadFonts function will be used (see example below)

# Setting font in BRUTAL ImGui code

This uses a Push / Pop sematic pattern.

Here are some example functions which simplify that for a known font name.

```csharp
using KSA; // FontManager is under KSA namespace

private static void PushHackFont(out bool fontUsed, float size)
{
  if (FontManager.Fonts.TryGetValue("HackNerdFontMono-Regular", out ImFontPtr fontPtr))
  {
    ImGui.PushFont(fontPtr, size);
    fontUsed = true;
    return;
  }

  fontUsed = false;
}

private static void MaybePopFont(bool wasUsed)
{
  if (wasUsed) {
    ImGui.PopFont();
  }
}
```

## Preferred Font

`HackNerdFontMono-Regular.ttf` file with name `HackNerdFontMono-Regular` in code

This is the "Hack" font with nerd extensions (glyphs used in terminals) in regular weight and monospaced.


## GameMod font loading

When run inside a game mod, fonts must be loaded explicitly (as opposed to standalone apps which auto detect font files)

Here's an example (janky, but useful as reference) class which loads fonts programmatically in BRUTAL ImGui

```csharp
using System.Reflection;
using Brutal.ImGuiApi;

public class FontLoader
{
  private static Dictionary<string, ImFontPtr> _loadedFonts = new Dictionary<string, ImFontPtr>();
  private static bool _fontsLoaded = false;

  private static void LoadFonts()
  {
    if (_fontsLoaded)
      return;

    try
    {
      // Get the directory where the mod DLL is located
      string? dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

      if (!string.IsNullOrEmpty(dllDir))
      {
        string fontsDir = Path.Combine(dllDir, "TerminalFonts");
        Console.WriteLine($"TestCaTTY GameMod: Loading fonts from directory: {fontsDir}");

        if (Directory.Exists(fontsDir))
        {
          // Get all .iamttf files from TerminalFonts folder (we use .iamttf to avoid auto loading by the KSA game engine that searches recursively for *.ttf)
          var fontFiles = Directory.GetFiles(fontsDir, "*.iamttf");

          if (fontFiles.Length > 0)
          {

            var io = ImGui.GetIO();
            var atlas = io.Fonts;

            for (int i = 0; i < fontFiles.Length; i++)
            {
              string fontPath = fontFiles[i];
              string fontName = Path.GetFileNameWithoutExtension(fontPath);

              Console.WriteLine($"TestCaTTY GameMod: Loading font: {fontPath}");


              if (File.Exists(fontPath))
              {
                // Use a reasonable default font size (14pt)
                float fontSize = 14.0f;
                ImString fontPathStr = new ImString(fontPath);
                ImFontPtr font = atlas.AddFontFromFileTTF(fontPathStr, fontSize);
                _loadedFonts[fontName] = font;

                Console.WriteLine($"TestCaTTY GameMod: Loaded font '{fontName}' from {fontPath}");
              }
            }

            Console.WriteLine($"TestCaTTY GameMod: Loaded {_loadedFonts.Count} fonts - {string.Join(", ", _loadedFonts.Keys)}");
          }
          else
          {
            Console.WriteLine("TestCaTTY GameMod: No font files found in Fonts folder");
          }
        }
        else
        {
          Console.WriteLine($"TestCaTTY GameMod: Fonts directory not found at: {fontsDir}");
        }
      }

      _fontsLoaded = true;
    }
    catch (Exception ex)
    {
      Console.WriteLine($"TestCaTTY GameMod: Error loading fonts: {ex.Message}");
    }
  }
}
```