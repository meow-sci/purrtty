using caTTY.Core.Managers;
using caTTY.Core.Types;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Managers;

/// <summary>
///     Unit tests for AttributeManager class.
///     Tests SGR attribute state management in isolation.
/// </summary>
[TestFixture]
public class AttributeManagerTests
{
    private AttributeManager _manager = null!;

    [SetUp]
    public void SetUp()
    {
        _manager = new AttributeManager();
    }

    [Test]
    public void CurrentAttributes_DefaultsToDefault()
    {
        Assert.That(_manager.CurrentAttributes, Is.EqualTo(SgrAttributes.Default));
    }

    [Test]
    public void CurrentCharacterProtection_DefaultsToFalse()
    {
        Assert.That(_manager.CurrentCharacterProtection, Is.False);
    }

    [Test]
    public void CurrentCharacterProtection_CanBeSetAndGet()
    {
        _manager.CurrentCharacterProtection = true;
        Assert.That(_manager.CurrentCharacterProtection, Is.True);

        _manager.CurrentCharacterProtection = false;
        Assert.That(_manager.CurrentCharacterProtection, Is.False);
    }

    [Test]
    public void CurrentAttributes_CanBeSetAndGet()
    {
        var redColor = new Color(NamedColor.Red);
        var blueColor = new Color(NamedColor.Blue);
        
        var attrs = new SgrAttributes(
            bold: true,
            faint: false,
            italic: true,
            underline: false,
            underlineStyle: UnderlineStyle.Single,
            blink: false,
            inverse: false,
            hidden: false,
            strikethrough: false,
            foregroundColor: redColor,
            backgroundColor: blueColor,
            underlineColor: null,
            font: 0);

        _manager.CurrentAttributes = attrs;

        var result = _manager.CurrentAttributes;
        Assert.That(result.Bold, Is.True);
        Assert.That(result.Italic, Is.True);
        Assert.That(result.ForegroundColor, Is.EqualTo(redColor));
        Assert.That(result.BackgroundColor, Is.EqualTo(blueColor));
    }

    [Test]
    public void SetTextStyle_UpdatesAttributes()
    {
        _manager.SetTextStyle(true, true, true);

        var attrs = _manager.CurrentAttributes;
        Assert.That(attrs.Bold, Is.True);
        Assert.That(attrs.Italic, Is.True);
        Assert.That(attrs.Underline, Is.True);
    }

    [Test]
    public void SetForegroundColor_UpdatesColor()
    {
        var greenColor = new Color(NamedColor.Green);
        _manager.SetForegroundColor(greenColor);

        var attrs = _manager.CurrentAttributes;
        Assert.That(attrs.ForegroundColor, Is.EqualTo(greenColor));
    }

    [Test]
    public void SetBackgroundColor_UpdatesColor()
    {
        var yellowColor = new Color(NamedColor.Yellow);
        _manager.SetBackgroundColor(yellowColor);

        var attrs = _manager.CurrentAttributes;
        Assert.That(attrs.BackgroundColor, Is.EqualTo(yellowColor));
    }

    [Test]
    public void SetInverse_UpdatesInverseFlag()
    {
        _manager.SetInverse(true);

        var attrs = _manager.CurrentAttributes;
        Assert.That(attrs.Inverse, Is.True);

        _manager.SetInverse(false);
        attrs = _manager.CurrentAttributes;
        Assert.That(attrs.Inverse, Is.False);
    }

    [Test]
    public void SetDim_UpdatesFaintFlag()
    {
        _manager.SetDim(true);

        var attrs = _manager.CurrentAttributes;
        Assert.That(attrs.Faint, Is.True);

        _manager.SetDim(false);
        attrs = _manager.CurrentAttributes;
        Assert.That(attrs.Faint, Is.False);
    }

    [Test]
    public void SetStrikethrough_UpdatesStrikethroughFlag()
    {
        _manager.SetStrikethrough(true);

        var attrs = _manager.CurrentAttributes;
        Assert.That(attrs.Strikethrough, Is.True);

        _manager.SetStrikethrough(false);
        attrs = _manager.CurrentAttributes;
        Assert.That(attrs.Strikethrough, Is.False);
    }

    [Test]
    public void SetBlink_UpdatesBlinkFlag()
    {
        _manager.SetBlink(true);

        var attrs = _manager.CurrentAttributes;
        Assert.That(attrs.Blink, Is.True);

        _manager.SetBlink(false);
        attrs = _manager.CurrentAttributes;
        Assert.That(attrs.Blink, Is.False);
    }

    [Test]
    public void ResetAttributes_RestoresDefaults()
    {
        // Set various attributes
        _manager.SetTextStyle(true, true, true);
        _manager.SetForegroundColor(new Color(NamedColor.Red));
        _manager.SetBackgroundColor(new Color(NamedColor.Blue));
        _manager.CurrentCharacterProtection = true;

        _manager.ResetAttributes();

        Assert.That(_manager.CurrentAttributes, Is.EqualTo(SgrAttributes.Default));
        Assert.That(_manager.CurrentCharacterProtection, Is.False);
    }

    [Test]
    public void GetDefaultAttributes_ReturnsDefault()
    {
        var defaultAttrs = _manager.GetDefaultAttributes();
        Assert.That(defaultAttrs, Is.EqualTo(SgrAttributes.Default));
    }

    [Test]
    public void ApplySgrMessage_WithBoldMessage_SetsBold()
    {
        var message = new SgrMessage { Type = "sgr.bold", Data = null };
        _manager.ApplySgrMessage(message);

        var attrs = _manager.CurrentAttributes;
        Assert.That(attrs.Bold, Is.True);
    }

    [Test]
    public void ApplySgrMessage_WithNoBoldMessage_ClearsBold()
    {
        // First set bold
        _manager.SetTextStyle(true, false, false);

        var message = new SgrMessage { Type = "sgr.normalIntensity", Data = null };
        _manager.ApplySgrMessage(message);

        var attrs = _manager.CurrentAttributes;
        Assert.That(attrs.Bold, Is.False);
    }

    [Test]
    public void ApplySgrMessage_WithItalicMessage_SetsItalic()
    {
        var message = new SgrMessage { Type = "sgr.italic", Data = null };
        _manager.ApplySgrMessage(message);

        var attrs = _manager.CurrentAttributes;
        Assert.That(attrs.Italic, Is.True);
    }

    [Test]
    public void ApplySgrMessage_WithUnderlineMessage_SetsUnderline()
    {
        var message = new SgrMessage { Type = "sgr.underline", Data = null };
        _manager.ApplySgrMessage(message);

        var attrs = _manager.CurrentAttributes;
        Assert.That(attrs.Underline, Is.True);
    }

    [Test]
    public void ApplySgrMessage_WithResetMessage_ResetsAttributes()
    {
        // Set some attributes first
        _manager.SetTextStyle(true, true, true);
        _manager.SetForegroundColor(new Color(NamedColor.Red));

        var message = new SgrMessage { Type = "sgr.reset", Data = null };
        _manager.ApplySgrMessage(message);

        Assert.That(_manager.CurrentAttributes, Is.EqualTo(SgrAttributes.Default));
    }

    [Test]
    public void ApplySgrMessage_WithForegroundColorMessage_SetsColor()
    {
        var cyanColor = new Color(NamedColor.Cyan);
        var message = new SgrMessage { Type = "sgr.foregroundColor", Data = cyanColor };
        _manager.ApplySgrMessage(message);

        var attrs = _manager.CurrentAttributes;
        Assert.That(attrs.ForegroundColor, Is.EqualTo(cyanColor));
    }

    [Test]
    public void ApplySgrMessage_WithBackgroundColorMessage_SetsColor()
    {
        var magentaColor = new Color(NamedColor.Magenta);
        var message = new SgrMessage { Type = "sgr.backgroundColor", Data = magentaColor };
        _manager.ApplySgrMessage(message);

        var attrs = _manager.CurrentAttributes;
        Assert.That(attrs.BackgroundColor, Is.EqualTo(magentaColor));
    }

    [Test]
    public void ApplySgrMessage_WithNullMessage_DoesNothing()
    {
        var originalAttrs = _manager.CurrentAttributes;

        _manager.ApplySgrMessage(null!);

        Assert.That(_manager.CurrentAttributes, Is.EqualTo(originalAttrs));
    }

    [Test]
    public void MultipleAttributeChanges_CanBeCombined()
    {
        _manager.SetTextStyle(true, true, false);
        _manager.SetForegroundColor(new Color(NamedColor.Red));
        _manager.SetBackgroundColor(new Color(NamedColor.Blue));

        var attrs = _manager.CurrentAttributes;
        Assert.That(attrs.Bold, Is.True);
        Assert.That(attrs.Italic, Is.True);
        Assert.That(attrs.Underline, Is.False);
        Assert.That(attrs.ForegroundColor, Is.EqualTo(new Color(NamedColor.Red)));
        Assert.That(attrs.BackgroundColor, Is.EqualTo(new Color(NamedColor.Blue)));
    }

    [Test]
    public void AttributeChanges_PreserveOtherAttributes()
    {
        _manager.SetTextStyle(true, true, true);
        _manager.SetForegroundColor(new Color(NamedColor.Red));

        // Change only background color
        _manager.SetBackgroundColor(new Color(NamedColor.Green));

        var attrs = _manager.CurrentAttributes;
        Assert.That(attrs.Bold, Is.True);
        Assert.That(attrs.Italic, Is.True);
        Assert.That(attrs.Underline, Is.True);
        Assert.That(attrs.ForegroundColor, Is.EqualTo(new Color(NamedColor.Red)));
        Assert.That(attrs.BackgroundColor, Is.EqualTo(new Color(NamedColor.Green)));
    }
}