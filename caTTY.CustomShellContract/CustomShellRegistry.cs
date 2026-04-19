using System.Collections.Concurrent;
using System.Reflection;

namespace caTTY.Core.Terminal;

/// <summary>
///     Registry for managing custom shell implementations.
///     Handles shell discovery, registration, validation, and instantiation.
/// </summary>
public class CustomShellRegistry
{
    private readonly ConcurrentDictionary<string, Func<ICustomShell>> _shellFactories = new();
    private readonly ConcurrentDictionary<string, CustomShellMetadata> _shellMetadata = new();
    private readonly object _discoveryLock = new();
    private bool _discoveryCompleted = false;

    /// <summary>
    ///     Gets the singleton instance of the CustomShellRegistry.
    /// </summary>
    public static CustomShellRegistry Instance { get; } = new CustomShellRegistry();

    /// <summary>
    ///     Initializes a new instance of the CustomShellRegistry.
    ///     This constructor is public to allow testing with fresh instances.
    /// </summary>
    public CustomShellRegistry()
    {
        // Allow public construction for testing purposes
    }

    /// <summary>
    ///     Registers a custom shell with the registry.
    /// </summary>
    /// <typeparam name="T">The custom shell type that implements ICustomShell</typeparam>
    /// <param name="shellId">Unique identifier for the shell</param>
    /// <param name="factory">Factory function to create shell instances</param>
    /// <exception cref="ArgumentException">Thrown when shellId is null, empty, or already registered</exception>
    /// <exception cref="ArgumentNullException">Thrown when factory is null</exception>
    /// <exception cref="CustomShellRegistrationException">Thrown when shell validation fails</exception>
    public void RegisterShell<T>(string shellId, Func<T> factory) where T : class, ICustomShell
    {
        if (string.IsNullOrWhiteSpace(shellId))
        {
            throw new ArgumentException("Shell ID cannot be null or empty", nameof(shellId));
        }

        if (factory == null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        // Check if shell is already registered
        if (_shellFactories.ContainsKey(shellId))
        {
            throw new ArgumentException($"Shell with ID '{shellId}' is already registered", nameof(shellId));
        }

        try
        {
            // Validate the shell implementation by creating a temporary instance
            using var testInstance = factory();
            ValidateShellImplementation(testInstance, shellId);

            // Store metadata from the test instance
            var metadata = testInstance.Metadata;
            _shellMetadata[shellId] = metadata;

            // Store the factory function
            _shellFactories[shellId] = () => factory();

        }
        catch (Exception ex) when (!(ex is ArgumentException || ex is ArgumentNullException))
        {
            throw new CustomShellRegistrationException($"Failed to register shell '{shellId}': {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Gets all available custom shells with their metadata.
    /// </summary>
    /// <returns>Enumerable of shell ID and metadata pairs</returns>
    public IEnumerable<(string Id, CustomShellMetadata Metadata)> GetAvailableShells()
    {
        EnsureDiscoveryCompleted();
        return _shellMetadata.Select(kvp => (kvp.Key, kvp.Value));
    }

    /// <summary>
    ///     Gets the metadata for a specific shell.
    /// </summary>
    /// <param name="shellId">The shell ID to look up</param>
    /// <returns>The shell metadata, or null if not found</returns>
    public CustomShellMetadata? GetShellMetadata(string shellId)
    {
        if (string.IsNullOrWhiteSpace(shellId))
        {
            return null;
        }

        EnsureDiscoveryCompleted();
        return _shellMetadata.TryGetValue(shellId, out var metadata) ? metadata : null;
    }

    /// <summary>
    ///     Creates a new instance of the specified custom shell.
    /// </summary>
    /// <param name="shellId">The shell ID to instantiate</param>
    /// <returns>A new instance of the custom shell</returns>
    /// <exception cref="ArgumentException">Thrown when shellId is null, empty, or not found</exception>
    /// <exception cref="CustomShellInstantiationException">Thrown when shell creation fails</exception>
    public ICustomShell CreateShell(string shellId)
    {
        if (string.IsNullOrWhiteSpace(shellId))
        {
            throw new ArgumentException("Shell ID cannot be null or empty", nameof(shellId));
        }

        EnsureDiscoveryCompleted();

        if (!_shellFactories.TryGetValue(shellId, out var factory))
        {
            var availableShells = string.Join(", ", _shellFactories.Keys);
            throw new ArgumentException($"Unknown shell type: '{shellId}'. Available shells: {availableShells}");
        }

        try
        {
            return factory();
        }
        catch (Exception ex)
        {
            throw new CustomShellInstantiationException($"Failed to create shell '{shellId}': {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Checks if a shell with the specified ID is registered.
    /// </summary>
    /// <param name="shellId">The shell ID to check</param>
    /// <returns>True if the shell is registered, false otherwise</returns>
    public bool IsShellRegistered(string shellId)
    {
        if (string.IsNullOrWhiteSpace(shellId))
        {
            return false;
        }

        EnsureDiscoveryCompleted();
        return _shellFactories.ContainsKey(shellId);
    }

    /// <summary>
    ///     Performs automatic discovery of custom shell implementations in the current application domain.
    /// </summary>
    public void DiscoverShells()
    {
        lock (_discoveryLock)
        {
            if (_discoveryCompleted)
            {
                return;
            }

            try
            {
                // Console.WriteLine("CustomShellRegistry: Starting automatic shell discovery...");

                var discoveredCount = 0;
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();

                foreach (var assembly in assemblies)
                {
                    try
                    {
                        discoveredCount += DiscoverShellsInAssembly(assembly);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"CustomShellRegistry: Warning - Failed to discover shells in assembly '{assembly.FullName}': {ex.Message}");
                        // Continue with other assemblies
                    }
                }

                Console.WriteLine($"CustomShellRegistry: Discovery completed. Found {discoveredCount} custom shell implementations. Available shells: {string.Join(", ", _shellFactories.Keys)}");
                _discoveryCompleted = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CustomShellRegistry: Error during shell discovery: {ex.Message}");
                // Mark as completed even on error to prevent infinite retry
                _discoveryCompleted = true;
            }
        }
    }

    /// <summary>
    ///     Discovers custom shell implementations in a specific assembly.
    /// </summary>
    /// <param name="assembly">The assembly to search</param>
    /// <returns>The number of shells discovered in the assembly</returns>
    private int DiscoverShellsInAssembly(Assembly assembly)
    {
        var discoveredCount = 0;

        // Get types from assembly, handling partial load failures
        Type[] allTypes;
        try
        {
            allTypes = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            Console.WriteLine($"CustomShellRegistry: Warning - Partial type load failure in assembly '{assembly.GetName().Name}': {ex.Message}");
            // Use the types that DID load successfully (filtering out nulls from failed types)
            allTypes = ex.Types.Where(t => t != null).ToArray()!;
            Console.WriteLine($"CustomShellRegistry: Recovered {allTypes.Length} loadable types from '{assembly.GetName().Name}'");
        }

        var shellTypes = allTypes
            .Where(type => type.IsClass && !type.IsAbstract && typeof(ICustomShell).IsAssignableFrom(type))
            .ToList();

        if (shellTypes.Count > 0)
        {
            Console.WriteLine($"CustomShellRegistry: Found {shellTypes.Count} ICustomShell type(s) in assembly '{assembly.GetName().Name}': {string.Join(", ", shellTypes.Select(t => t.Name))}");
        }

        foreach (var shellType in shellTypes)
        {
            try
            {
                // Look for a parameterless constructor
                var constructor = shellType.GetConstructor(Type.EmptyTypes);
                if (constructor == null)
                {
                    Console.WriteLine($"CustomShellRegistry: Skipping '{shellType.FullName}' - no parameterless constructor found");
                    continue;
                }

                // Create a factory function for this type
                var factory = () => (ICustomShell)Activator.CreateInstance(shellType)!;

                // Use the type name as the shell ID (can be overridden by explicit registration)
                var shellId = shellType.Name;

                // Skip if already registered (explicit registration takes precedence)
                if (_shellFactories.ContainsKey(shellId))
                {
                    Console.WriteLine($"CustomShellRegistry: Skipping auto-discovery of '{shellId}' - already explicitly registered");
                    continue;
                }

                // Register the discovered shell
                RegisterShell(shellId, factory);
                Console.WriteLine($"CustomShellRegistry: Successfully registered shell '{shellId}'");
                discoveredCount++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CustomShellRegistry: Failed to register shell '{shellType.FullName}': {ex.Message}");
                // Continue with other types
            }
        }

        return discoveredCount;
    }

    /// <summary>
    ///     Validates that a custom shell implementation meets the requirements.
    /// </summary>
    /// <param name="shell">The shell instance to validate</param>
    /// <param name="shellId">The shell ID for error reporting</param>
    /// <exception cref="CustomShellRegistrationException">Thrown when validation fails</exception>
    private static void ValidateShellImplementation(ICustomShell shell, string shellId)
    {
        // Validate metadata
        var metadata = shell.Metadata;
        if (metadata == null)
        {
            throw new CustomShellRegistrationException($"Shell '{shellId}' has null metadata");
        }

        if (string.IsNullOrWhiteSpace(metadata.Name))
        {
            throw new CustomShellRegistrationException($"Shell '{shellId}' has null or empty name in metadata");
        }

        if (string.IsNullOrWhiteSpace(metadata.Description))
        {
            throw new CustomShellRegistrationException($"Shell '{shellId}' has null or empty description in metadata");
        }

        if (metadata.Version == null)
        {
            throw new CustomShellRegistrationException($"Shell '{shellId}' has null version in metadata");
        }

        if (string.IsNullOrWhiteSpace(metadata.Author))
        {
            throw new CustomShellRegistrationException($"Shell '{shellId}' has null or empty author in metadata");
        }

        // Validate that the shell is not already running
        if (shell.IsRunning)
        {
            throw new CustomShellRegistrationException($"Shell '{shellId}' is already running during registration");
        }

        // Additional validation could be added here (e.g., checking for required methods)
    }

    /// <summary>
    ///     Ensures that shell discovery has been completed.
    /// </summary>
    private void EnsureDiscoveryCompleted()
    {
        if (!_discoveryCompleted)
        {
            DiscoverShells();
        }
    }
}

/// <summary>
///     Exception thrown when custom shell registration fails.
/// </summary>
public class CustomShellRegistrationException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the CustomShellRegistrationException class.
    /// </summary>
    /// <param name="message">The error message</param>
    public CustomShellRegistrationException(string message) : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the CustomShellRegistrationException class.
    /// </summary>
    /// <param name="message">The error message</param>
    /// <param name="innerException">The inner exception</param>
    public CustomShellRegistrationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
///     Exception thrown when custom shell instantiation fails.
/// </summary>
public class CustomShellInstantiationException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the CustomShellInstantiationException class.
    /// </summary>
    /// <param name="message">The error message</param>
    public CustomShellInstantiationException(string message) : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the CustomShellInstantiationException class.
    /// </summary>
    /// <param name="message">The error message</param>
    /// <param name="innerException">The inner exception</param>
    public CustomShellInstantiationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}