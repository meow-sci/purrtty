using caTTY.Display.Configuration;
using NUnit.Framework;

namespace caTTY.Display.Tests.Unit.Configuration;

/// <summary>
/// Unit tests for FontFamilyDefinition class.
/// Tests property initialization, default values, and ToString() method output format.
/// </summary>
[TestFixture]
[Category("Unit")]
public class FontFamilyDefinitionTests
{
    [Test]
    public void DefaultConstructor_ShouldSetCorrectDefaultValues()
    {
        // Act
        var definition = new FontFamilyDefinition();

        // Assert
        Assert.That(definition.DisplayName, Is.EqualTo(""));
        Assert.That(definition.FontBaseName, Is.EqualTo(""));
        Assert.That(definition.HasRegular, Is.True); // Should default to true
        Assert.That(definition.HasBold, Is.False);
        Assert.That(definition.HasItalic, Is.False);
        Assert.That(definition.HasBoldItalic, Is.False);
    }

    [Test]
    public void PropertyInitialization_ShouldSetValuesCorrectly()
    {
        // Act
        var definition = new FontFamilyDefinition
        {
            DisplayName = "Test Font",
            FontBaseName = "TestFontNerdFont",
            HasRegular = true,
            HasBold = true,
            HasItalic = false,
            HasBoldItalic = false
        };

        // Assert
        Assert.That(definition.DisplayName, Is.EqualTo("Test Font"));
        Assert.That(definition.FontBaseName, Is.EqualTo("TestFontNerdFont"));
        Assert.That(definition.HasRegular, Is.True);
        Assert.That(definition.HasBold, Is.True);
        Assert.That(definition.HasItalic, Is.False);
        Assert.That(definition.HasBoldItalic, Is.False);
    }

    [Test]
    public void ToString_WithAllVariants_ShouldShowAllVariants()
    {
        // Arrange
        var definition = new FontFamilyDefinition
        {
            DisplayName = "Complete Font",
            HasRegular = true,
            HasBold = true,
            HasItalic = true,
            HasBoldItalic = true
        };

        // Act
        var result = definition.ToString();

        // Assert
        Assert.That(result, Is.EqualTo("Complete Font (Regular, Bold, Italic, BoldItalic)"));
    }

    [Test]
    public void ToString_WithRegularOnly_ShouldShowRegularOnly()
    {
        // Arrange
        var definition = new FontFamilyDefinition
        {
            DisplayName = "Simple Font",
            HasRegular = true,
            HasBold = false,
            HasItalic = false,
            HasBoldItalic = false
        };

        // Act
        var result = definition.ToString();

        // Assert
        Assert.That(result, Is.EqualTo("Simple Font (Regular)"));
    }

    [Test]
    public void ToString_WithRegularAndBold_ShouldShowBothVariants()
    {
        // Arrange
        var definition = new FontFamilyDefinition
        {
            DisplayName = "Bold Font",
            HasRegular = true,
            HasBold = true,
            HasItalic = false,
            HasBoldItalic = false
        };

        // Act
        var result = definition.ToString();

        // Assert
        Assert.That(result, Is.EqualTo("Bold Font (Regular, Bold)"));
    }

    [Test]
    public void ToString_WithRegularAndItalic_ShouldShowBothVariants()
    {
        // Arrange
        var definition = new FontFamilyDefinition
        {
            DisplayName = "Italic Font",
            HasRegular = true,
            HasBold = false,
            HasItalic = true,
            HasBoldItalic = false
        };

        // Act
        var result = definition.ToString();

        // Assert
        Assert.That(result, Is.EqualTo("Italic Font (Regular, Italic)"));
    }

    [Test]
    public void ToString_WithNoVariants_ShouldShowEmptyVariantList()
    {
        // Arrange
        var definition = new FontFamilyDefinition
        {
            DisplayName = "No Variants Font",
            HasRegular = false,
            HasBold = false,
            HasItalic = false,
            HasBoldItalic = false
        };

        // Act
        var result = definition.ToString();

        // Assert
        Assert.That(result, Is.EqualTo("No Variants Font ()"));
    }

    [Test]
    public void ToString_WithMixedVariants_ShouldShowCorrectOrder()
    {
        // Arrange
        var definition = new FontFamilyDefinition
        {
            DisplayName = "Mixed Font",
            HasRegular = true,
            HasBold = false,
            HasItalic = true,
            HasBoldItalic = true
        };

        // Act
        var result = definition.ToString();

        // Assert
        Assert.That(result, Is.EqualTo("Mixed Font (Regular, Italic, BoldItalic)"));
    }

    [Test]
    public void HasRegular_ShouldDefaultToTrue()
    {
        // Arrange & Act
        var definition = new FontFamilyDefinition();

        // Assert
        Assert.That(definition.HasRegular, Is.True, "HasRegular should default to true for all font families");
    }

    [Test]
    public void AllVariantProperties_ShouldBeSettableIndependently()
    {
        // Arrange
        var definition = new FontFamilyDefinition();

        // Act & Assert - Test each property can be set independently
        definition.HasRegular = false;
        Assert.That(definition.HasRegular, Is.False);
        Assert.That(definition.HasBold, Is.False);
        Assert.That(definition.HasItalic, Is.False);
        Assert.That(definition.HasBoldItalic, Is.False);

        definition.HasBold = true;
        Assert.That(definition.HasRegular, Is.False);
        Assert.That(definition.HasBold, Is.True);
        Assert.That(definition.HasItalic, Is.False);
        Assert.That(definition.HasBoldItalic, Is.False);

        definition.HasItalic = true;
        Assert.That(definition.HasRegular, Is.False);
        Assert.That(definition.HasBold, Is.True);
        Assert.That(definition.HasItalic, Is.True);
        Assert.That(definition.HasBoldItalic, Is.False);

        definition.HasBoldItalic = true;
        Assert.That(definition.HasRegular, Is.False);
        Assert.That(definition.HasBold, Is.True);
        Assert.That(definition.HasItalic, Is.True);
        Assert.That(definition.HasBoldItalic, Is.True);
    }
}