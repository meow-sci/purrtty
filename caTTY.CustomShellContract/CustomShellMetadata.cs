namespace caTTY.Core.Terminal;

/// <summary>
///     Metadata for a custom shell implementation.
/// </summary>
/// <param name="Name">The display name of the shell</param>
/// <param name="Description">A description of what the shell does</param>
/// <param name="Version">The version of the shell implementation</param>
/// <param name="Author">The author or organization that created the shell</param>
/// <param name="SupportedFeatures">Array of features supported by this shell (e.g., "colors", "history", "completion")</param>
public record CustomShellMetadata(
    string Name,
    string Description,
    Version Version,
    string Author,
    string[] SupportedFeatures
)
{
    /// <summary>
    ///     Creates a new CustomShellMetadata with default values.
    /// </summary>
    /// <param name="name">The shell name</param>
    /// <param name="description">The shell description</param>
    /// <returns>A new CustomShellMetadata instance</returns>
    public static CustomShellMetadata Create(string name, string description)
    {
        return new CustomShellMetadata(
            name,
            description,
            new Version(1, 0, 0),
            "Unknown",
            Array.Empty<string>()
        );
    }

    /// <summary>
    ///     Creates a new CustomShellMetadata with version information.
    /// </summary>
    /// <param name="name">The shell name</param>
    /// <param name="description">The shell description</param>
    /// <param name="version">The shell version</param>
    /// <param name="author">The shell author</param>
    /// <returns>A new CustomShellMetadata instance</returns>
    public static CustomShellMetadata Create(string name, string description, Version version, string author)
    {
        return new CustomShellMetadata(
            name,
            description,
            version,
            author,
            Array.Empty<string>()
        );
    }

    /// <summary>
    ///     Creates a new CustomShellMetadata with full information.
    /// </summary>
    /// <param name="name">The shell name</param>
    /// <param name="description">The shell description</param>
    /// <param name="version">The shell version</param>
    /// <param name="author">The shell author</param>
    /// <param name="supportedFeatures">Array of supported features</param>
    /// <returns>A new CustomShellMetadata instance</returns>
    public static CustomShellMetadata Create(string name, string description, Version version, string author, params string[] supportedFeatures)
    {
        return new CustomShellMetadata(
            name,
            description,
            version,
            author,
            supportedFeatures
        );
    }
}