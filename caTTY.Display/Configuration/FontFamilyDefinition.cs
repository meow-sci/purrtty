using System;
using System.Collections.Generic;
using System.Linq;

namespace caTTY.Display.Configuration;

/// <summary>
/// Represents a font family definition with display name, base font name, and variant availability.
/// Used by the font registry to map user-friendly display names to technical font names
/// and track which font variants (Regular, Bold, Italic, BoldItalic) are available.
/// </summary>
public class FontFamilyDefinition
{
    /// <summary>
    /// Gets or sets the user-friendly display name shown in the font selection UI.
    /// </summary>
    /// <value>The display name for the font family (e.g., "Jet Brains Mono").</value>
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// Gets or sets the base name used for font file naming.
    /// This is combined with variant suffixes to create full font names.
    /// </summary>
    /// <value>The base font name (e.g., "JetBrainsMonoNerdFontMono").</value>
    public string FontBaseName { get; set; } = "";

    /// <summary>
    /// Gets or sets whether the Regular variant is available for this font family.
    /// </summary>
    /// <value>True if Regular variant is available, false otherwise. Defaults to true.</value>
    public bool HasRegular { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the Bold variant is available for this font family.
    /// </summary>
    /// <value>True if Bold variant is available, false otherwise. Defaults to false.</value>
    public bool HasBold { get; set; } = false;

    /// <summary>
    /// Gets or sets whether the Italic variant is available for this font family.
    /// </summary>
    /// <value>True if Italic variant is available, false otherwise. Defaults to false.</value>
    public bool HasItalic { get; set; } = false;

    /// <summary>
    /// Gets or sets whether the BoldItalic variant is available for this font family.
    /// </summary>
    /// <value>True if BoldItalic variant is available, false otherwise. Defaults to false.</value>
    public bool HasBoldItalic { get; set; } = false;

    /// <summary>
    /// Returns a string representation of the font family definition showing the display name
    /// and available variants for debugging purposes.
    /// </summary>
    /// <returns>A formatted string showing display name and available variants.</returns>
    public override string ToString()
    {
        var variants = new List<string>();
        
        if (HasRegular) variants.Add("Regular");
        if (HasBold) variants.Add("Bold");
        if (HasItalic) variants.Add("Italic");
        if (HasBoldItalic) variants.Add("BoldItalic");
        
        return $"{DisplayName} ({string.Join(", ", variants)})";
    }
}