using caTTY.Core.Terminal;
using FsCheck;
using NUnit.Framework;

namespace caTTY.CustomShellContract.Tests.Property;

/// <summary>
///     Property-based tests for custom shell interface compliance.
///     These tests verify that custom shell implementations conform to the ICustomShell contract.
///     **Feature: custom-game-shells, Property 1: Custom Shell Interface Compliance**
///     **Validates: Requirements 1.1, 5.1, 7.3**
/// </summary>
[TestFixture]
[Category("Property")]
public class CustomShellInterfaceProperties
{
    /// <summary>
    ///     Generator for valid shell names.
    /// </summary>
    public static Arbitrary<string> ShellNameArb =>
        Arb.From(Gen.Elements("GameRCS", "TestShell", "MockShell", "CustomShell", "DemoShell"));

    /// <summary>
    ///     Generator for valid shell descriptions.
    /// </summary>
    public static Arbitrary<string> ShellDescriptionArb =>
        Arb.From(Gen.Elements(
            "A game remote control shell",
            "Test shell for validation",
            "Mock shell implementation",
            "Custom shell for testing",
            "Demo shell with features"
        ));

    /// <summary>
    ///     Generator for valid version numbers.
    /// </summary>
    public static Arbitrary<Version> VersionArb =>
        Arb.From(Gen.Choose(1, 10).SelectMany(major =>
            Gen.Choose(0, 20).SelectMany(minor =>
                Gen.Choose(0, 100).Select(patch => new Version(major, minor, patch)))));

    /// <summary>
    ///     Generator for valid author names.
    /// </summary>
    public static Arbitrary<string> AuthorArb =>
        Arb.From(Gen.Elements("Test Author", "Game Developer", "Shell Creator", "caTTY Team", "Unknown"));

    /// <summary>
    ///     Generator for supported features arrays.
    /// </summary>
    public static Arbitrary<string[]> SupportedFeaturesArb =>
        Arb.From(Gen.SubListOf(new[] { "colors", "history", "completion", "async", "resize", "cancellation" })
            .Select(list => list.ToArray()));

    /// <summary>
    ///     Generator for terminal dimensions.
    /// </summary>
    public static Arbitrary<(int width, int height)> TerminalDimensionsArb =>
        Arb.From(Gen.Choose(10, 200).SelectMany(width =>
            Gen.Choose(5, 100).Select(height => (width, height))));

    /// <summary>
    ///     Generator for input data.
    /// </summary>
    public static Arbitrary<byte[]> InputDataArb =>
        Arb.From(Gen.Choose(1, 100).SelectMany(length =>
            Gen.ArrayOf(length, Gen.Choose(32, 126).Select(i => (byte)i))));

    /// <summary>
    ///     **Feature: custom-game-shells, Property 1: Custom Shell Interface Compliance**
    ///     **Validates: Requirements 1.1, 5.1, 7.3**
    ///     Property: For any custom shell implementation, it must implement the ICustomShell interface
    ///     and provide valid metadata including name, description, and version information.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property CustomShellMetadataMustBeValid()
    {
        return Prop.ForAll(ShellNameArb, name =>
        {
            // Arrange: Create metadata with the generated name
            var metadata = CustomShellMetadata.Create(name, "Test description");

            // Act & Assert: Verify all metadata properties are valid
            bool nameValid = !string.IsNullOrEmpty(metadata.Name);
            bool descriptionValid = !string.IsNullOrEmpty(metadata.Description);
            bool versionValid = metadata.Version != null;
            bool authorValid = !string.IsNullOrEmpty(metadata.Author);
            bool featuresValid = metadata.SupportedFeatures != null;

            // Verify metadata values match input
            bool valuesMatch = metadata.Name == name &&
                              metadata.Description == "Test description" &&
                              metadata.Version != null &&
                              metadata.Author == "Unknown" &&
                              (metadata.SupportedFeatures?.Length ?? 0) == 0;

            return nameValid && descriptionValid && versionValid && authorValid && featuresValid && valuesMatch;
        });
    }

    /// <summary>
    ///     Property: CustomShellMetadata factory methods should create valid instances.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property CustomShellMetadataFactoryMethodsWork()
    {
        return Prop.ForAll(ShellNameArb, name =>
        {
            var description = "Test description";
            var version = new Version(1, 0, 0);
            var author = "Test Author";
            var features = new[] { "colors", "history" };

            // Test Create method with minimal parameters
            var metadata1 = CustomShellMetadata.Create(name, description);
            bool basicCreateWorks = metadata1.Name == name &&
                                   metadata1.Description == description &&
                                   metadata1.Version != null &&
                                   metadata1.Author == "Unknown" &&
                                   metadata1.SupportedFeatures.Length == 0;

            // Test Create method with version and author
            var metadata2 = CustomShellMetadata.Create(name, description, version, author);
            bool versionCreateWorks = metadata2.Name == name &&
                                     metadata2.Description == description &&
                                     metadata2.Version == version &&
                                     metadata2.Author == author &&
                                     metadata2.SupportedFeatures.Length == 0;

            // Test Create method with all parameters
            var metadata3 = CustomShellMetadata.Create(name, description, version, author, features);
            bool fullCreateWorks = metadata3.Name == name &&
                                  metadata3.Description == description &&
                                  metadata3.Version == version &&
                                  metadata3.Author == author &&
                                  metadata3.SupportedFeatures.Length == features.Length;

            return basicCreateWorks && versionCreateWorks && fullCreateWorks;
        });
    }

    /// <summary>
    ///     Property: CustomShellStartOptions should provide valid default values.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property CustomShellStartOptionsDefaultsAreValid()
    {
        return Prop.ForAll(TerminalDimensionsArb, dimensions =>
        {
            var (width, height) = dimensions;

            // Test default creation
            var defaultOptions = CustomShellStartOptions.CreateDefault();
            bool defaultsValid = defaultOptions.InitialWidth == 80 &&
                                defaultOptions.InitialHeight == 24 &&
                                defaultOptions.WorkingDirectory != null &&
                                defaultOptions.EnvironmentVariables.Count > 0 &&
                                defaultOptions.Configuration != null;

            // Test creation with dimensions
            var dimensionOptions = CustomShellStartOptions.CreateWithDimensions(width, height);
            bool dimensionsValid = dimensionOptions.InitialWidth == width &&
                                  dimensionOptions.InitialHeight == height &&
                                  dimensionOptions.WorkingDirectory != null &&
                                  dimensionOptions.EnvironmentVariables.Count > 0;

            // Test creation with working directory
            var workingDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var workingDirOptions = CustomShellStartOptions.CreateWithWorkingDirectory(workingDir);
            bool workingDirValid = workingDirOptions.WorkingDirectory == workingDir &&
                                  workingDirOptions.InitialWidth == 80 &&
                                  workingDirOptions.InitialHeight == 24 &&
                                  workingDirOptions.EnvironmentVariables.Count > 0;

            return defaultsValid && dimensionsValid && workingDirValid;
        });
    }

    /// <summary>
    ///     Property: Shell event arguments should preserve data integrity.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ShellEventArgumentsPreserveData()
    {
        return Prop.ForAll(InputDataArb, data =>
        {
            var outputType = ShellOutputType.Stdout;
            var exitCode = 0;
            var reason = "Test exit";

            // Test ShellOutputEventArgs with byte data
            var outputArgs1 = new ShellOutputEventArgs(data, outputType);
            bool outputDataPreserved = outputArgs1.Data.Span.SequenceEqual(data) &&
                                      outputArgs1.OutputType == outputType &&
                                      outputArgs1.Timestamp <= DateTime.UtcNow;

            // Test ShellOutputEventArgs with string data
            var testString = System.Text.Encoding.UTF8.GetString(data);
            var outputArgs2 = new ShellOutputEventArgs(testString, outputType);
            var expectedBytes = System.Text.Encoding.UTF8.GetBytes(testString);
            bool stringDataPreserved = outputArgs2.Data.Span.SequenceEqual(expectedBytes) &&
                                      outputArgs2.OutputType == outputType &&
                                      outputArgs2.Timestamp <= DateTime.UtcNow;

            // Test ShellTerminatedEventArgs
            var terminatedArgs = new ShellTerminatedEventArgs(exitCode, reason);
            bool terminatedDataPreserved = terminatedArgs.ExitCode == exitCode &&
                                          terminatedArgs.Reason == reason &&
                                          terminatedArgs.Timestamp <= DateTime.UtcNow;

            return outputDataPreserved && stringDataPreserved && terminatedDataPreserved;
        });
    }

    /// <summary>
    ///     Property: Shell event arguments should handle edge cases correctly.
    /// </summary>
    [Test]
    public void ShellEventArgumentsHandleEdgeCases()
    {
        var outputType = ShellOutputType.Stdout;

        // Test with empty data
        var emptyData = Array.Empty<byte>();
        var emptyArgs = new ShellOutputEventArgs(emptyData, outputType);
        bool emptyDataHandled = emptyArgs.Data.Length == 0 &&
                               emptyArgs.OutputType == outputType;

        // Test with empty string
        var emptyStringArgs = new ShellOutputEventArgs(string.Empty, outputType);
        bool emptyStringHandled = emptyStringArgs.Data.Length == 0 &&
                                 emptyStringArgs.OutputType == outputType;

        // Test with null reason
        var nullReasonArgs = new ShellTerminatedEventArgs(0, null);
        bool nullReasonHandled = nullReasonArgs.ExitCode == 0 &&
                                nullReasonArgs.Reason == null;

        // Test with empty reason
        var emptyReasonArgs = new ShellTerminatedEventArgs(1, string.Empty);
        bool emptyReasonHandled = emptyReasonArgs.ExitCode == 1 &&
                                 emptyReasonArgs.Reason == string.Empty;

        Assert.That(emptyDataHandled && emptyStringHandled && nullReasonHandled && emptyReasonHandled, Is.True);
    }

    /// <summary>
    ///     Property: CustomShellStartOptions should handle configuration data correctly.
    /// </summary>
    [Test]
    public void CustomShellStartOptionsHandleConfiguration()
    {
        var configCount = 5;
        var options = CustomShellStartOptions.CreateDefault();

        // Add various configuration items
        for (int i = 0; i < configCount; i++)
        {
            options.Configuration[$"key{i}"] = $"value{i}";
            options.EnvironmentVariables[$"ENV{i}"] = $"envvalue{i}";
        }

        // Verify configuration is preserved
        bool configPreserved = options.Configuration.Count == configCount;
        bool envPreserved = options.EnvironmentVariables.Count >= configCount; // May have defaults

        // Verify we can retrieve values
        bool valuesCorrect = true;
        for (int i = 0; i < configCount && valuesCorrect; i++)
        {
            if (!options.Configuration.TryGetValue($"key{i}", out var configValue) ||
                configValue?.ToString() != $"value{i}")
            {
                valuesCorrect = false;
            }

            if (!options.EnvironmentVariables.TryGetValue($"ENV{i}", out var envValue) ||
                envValue != $"envvalue{i}")
            {
                valuesCorrect = false;
            }
        }

        Assert.That(configPreserved && envPreserved && valuesCorrect, Is.True);
    }
}
