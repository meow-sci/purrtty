using caTTY.Core.Tracing;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit;

/// <summary>
/// Unit tests for TerminalTracer Reset functionality.
/// </summary>
[TestFixture]
[Category("Unit")]
public class TerminalTracerResetTests
{
    private bool _originalEnabled;
    private string? _originalDbFilename;

    [SetUp]
    public void SetUp()
    {
        _originalEnabled = TerminalTracer.Enabled;
        _originalDbFilename = TerminalTracer.DbFilename;
    }

    [TearDown]
    public void TearDown()
    {
        TerminalTracer.Enabled = _originalEnabled;
        TerminalTracer.DbFilename = _originalDbFilename;
        TerminalTracer.Reset();
    }

    [Test]
    public void Reset_ClearsStateAndAllowsReinitialization()
    {
        // Arrange
        TerminalTracer.SetupTestDatabase();
        TerminalTracer.Enabled = true;
        
        // Initialize tracer
        TerminalTracer.TraceEscape("test1");
        TerminalTracer.Flush(); // Ensure tracer is fully initialized
        Assert.That(TerminalTracer.IsActive, Is.True);
        
        // Act - Reset the tracer
        TerminalTracer.Reset();
        
        // Assert - Should be able to reinitialize with different settings
        TerminalTracer.SetupTestDatabase();
        TerminalTracer.TraceEscape("test2");
        TerminalTracer.Flush(); // Ensure tracer is fully initialized
        Assert.That(TerminalTracer.IsActive, Is.True);
    }

    [Test]
    public void DbPath_Override_UsesCustomPath()
    {
        // Arrange
        var tempDir = Path.GetTempPath();
        var testFilename = TerminalTracer.SetupTestDatabase();
        
        TerminalTracer.DbPath = tempDir;
        TerminalTracer.Enabled = true;
        
        // Act
        TerminalTracer.TraceEscape("test");
        TerminalTracer.Flush(); // Ensure database is created
        
        // Assert
        var expectedPath = Path.Combine(tempDir, testFilename);
        var actualPath = TerminalTracer.GetDatabasePath();
        
        Assert.That(actualPath, Is.EqualTo(expectedPath));
        Assert.That(File.Exists(expectedPath), Is.True);
        
        // Cleanup - Reset first to close connection, then delete file
        TerminalTracer.Reset();
        try
        {
            if (File.Exists(expectedPath))
            {
                File.Delete(expectedPath);
            }
        }
        catch
        {
            // Ignore cleanup failures
        }
    }

    [Test]
    public void DbFilename_Override_UsesCustomFilename()
    {
        // Arrange
        var customFilename = TerminalTracer.SetupTestDatabase();
        TerminalTracer.Enabled = true;
        
        // Act
        TerminalTracer.TraceEscape("test");
        
        // Assert
        var databasePath = TerminalTracer.GetDatabasePath();
        Assert.That(databasePath, Is.Not.Null);
        Assert.That(Path.GetFileName(databasePath), Is.EqualTo(customFilename));
        
        // Cleanup - Reset first to close connection, then delete file
        TerminalTracer.Reset();
        try
        {
            if (databasePath != null && File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
        catch
        {
            // Ignore cleanup failures
        }
    }

    [Test]
    public void SetupTestDatabase_GeneratesUniqueFilenames()
    {
        // Act - Generate multiple test database filenames
        var filename1 = TerminalTracer.SetupTestDatabase();
        var filename2 = TerminalTracer.SetupTestDatabase();
        var filename3 = TerminalTracer.SetupTestDatabase();
        
        // Assert - All filenames should be unique and follow UUID pattern
        Assert.That(filename1, Is.Not.EqualTo(filename2));
        Assert.That(filename2, Is.Not.EqualTo(filename3));
        Assert.That(filename1, Is.Not.EqualTo(filename3));
        
        // Should all end with .db
        Assert.That(filename1, Does.EndWith(".db"));
        Assert.That(filename2, Does.EndWith(".db"));
        Assert.That(filename3, Does.EndWith(".db"));
        
        // Should be 36 characters total (32 hex chars + ".db")
        Assert.That(filename1.Length, Is.EqualTo(35));
        Assert.That(filename2.Length, Is.EqualTo(35));
        Assert.That(filename3.Length, Is.EqualTo(35));
    }
}