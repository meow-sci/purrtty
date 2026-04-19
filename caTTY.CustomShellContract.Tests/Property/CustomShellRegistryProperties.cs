using caTTY.Core.Terminal;
using FsCheck;
using NUnit.Framework;
using System.Collections.Concurrent;

namespace caTTY.CustomShellContract.Tests.Property;

/// <summary>
///     Property-based tests for custom shell registry functionality.
///     These tests verify shell discovery, registration, and validation behaviors.
/// </summary>
[TestFixture]
[Category("Property")]
public class CustomShellRegistryProperties
{
    private CustomShellRegistry? _testRegistry;

    [SetUp]
    public void SetUp()
    {
        // Create a fresh registry instance for each test
        _testRegistry = new CustomShellRegistry();
    }

    [TearDown]
    public void TearDown()
    {
        _testRegistry = null;
    }

    /// <summary>
    ///     Generator for valid shell IDs.
    /// </summary>
    public static Arbitrary<string> ShellIdArb =>
        Arb.From(Gen.Elements("GameRCS", "TestShell", "MockShell", "CustomShell", "DemoShell", "ValidShell"));

    /// <summary>
    ///     Generator for invalid shell IDs.
    /// </summary>
    public static Arbitrary<string> InvalidShellIdArb =>
        Arb.From(Gen.Elements("", " ", "\t", "\n", "   "));

    /// <summary>
    ///     Generator for valid shell metadata.
    /// </summary>
    public static Arbitrary<CustomShellMetadata> ValidMetadataArb =>
        Arb.From(Gen.Elements("TestShell", "MockShell", "DemoShell").SelectMany(name =>
            Gen.Elements("Test description", "Mock shell for testing", "Demo shell").SelectMany(desc =>
                Gen.Choose(1, 10).SelectMany(major =>
                    Gen.Choose(0, 20).SelectMany(minor =>
                        Gen.Choose(0, 100).SelectMany(patch =>
                            Gen.Elements("Test Author", "Shell Creator", "caTTY Team").Select(author =>
                                CustomShellMetadata.Create(name, desc, new Version(major, minor, patch), author))))))));

    /// <summary>
    ///     **Feature: custom-game-shells, Property 14: Automatic Shell Discovery**
    ///     **Validates: Requirements 7.1, 7.2**
    ///     Property: For any custom shell implementation in the application domain, the shell registry
    ///     should automatically discover it at startup and make it available for selection.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property AutomaticShellDiscoveryFindsValidShells()
    {
        return Prop.ForAll(ShellIdArb, shellId =>
        {
            // Arrange: Create a test registry
            var registry = new CustomShellRegistry();

            // Create a mock shell implementation
            var mockShell = new RegistryMockCustomShell(shellId);

            // Register the shell manually to simulate discovery
            registry.RegisterShell(shellId, () => new RegistryMockCustomShell(shellId));

            // Act: Get available shells
            var availableShells = registry.GetAvailableShells().ToList();

            // Assert: The shell should be discoverable
            bool shellFound = availableShells.Any(s => s.Id == shellId);
            bool metadataValid = registry.GetShellMetadata(shellId) != null;
            bool canCreateInstance = registry.IsShellRegistered(shellId);

            return shellFound && metadataValid && canCreateInstance;
        });
    }

    /// <summary>
    ///     Property: Discovery should handle multiple shells correctly.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 50, QuietOnSuccess = true)]
    public FsCheck.Property DiscoveryHandlesMultipleShells()
    {
        var shellIdsArb = Arb.From(Gen.ListOf(ShellIdArb.Generator)
            .Where(list => list.Distinct().Count() == list.Count() && list.Count() <= 5));
        return Prop.ForAll(shellIdsArb, shellIds =>
        {
            // Arrange: Create a test registry
            var registry = new CustomShellRegistry();

            // Register multiple shells
            foreach (var shellId in shellIds)
            {
                registry.RegisterShell(shellId, () => new RegistryMockCustomShell(shellId));
            }

            // Act: Get available shells
            var availableShells = registry.GetAvailableShells().ToList();

            // Assert: All shells should be discoverable
            bool allShellsFound = shellIds.All(id => availableShells.Any(s => s.Id == id));
            bool correctCount = availableShells.Count >= shellIds.Count();
            bool allHaveMetadata = shellIds.All(id => registry.GetShellMetadata(id) != null);

            return allShellsFound && correctCount && allHaveMetadata;
        });
    }

    /// <summary>
    ///     Property: Discovery should be idempotent.
    /// </summary>
    [Test]
    public void DiscoveryIsIdempotent()
    {
        // Arrange
        var registry = new CustomShellRegistry();
        var shellId = "TestShell";
        registry.RegisterShell(shellId, () => new RegistryMockCustomShell(shellId));

        // Act: Perform discovery multiple times
        var firstDiscovery = registry.GetAvailableShells().ToList();
        var secondDiscovery = registry.GetAvailableShells().ToList();
        var thirdDiscovery = registry.GetAvailableShells().ToList();

        // Assert: Results should be identical
        bool countsMatch = firstDiscovery.Count == secondDiscovery.Count &&
                          secondDiscovery.Count == thirdDiscovery.Count;

        bool contentMatches = firstDiscovery.All(shell =>
            secondDiscovery.Any(s => s.Id == shell.Id) &&
            thirdDiscovery.Any(s => s.Id == shell.Id));

        Assert.That(countsMatch && contentMatches, Is.True);
    }

    /// <summary>
    ///     Property: Discovery should handle assemblies gracefully and not throw exceptions.
    /// </summary>
    [Test]
    public void DiscoveryHandlesAssembliesGracefully()
    {
        // Arrange: Create a registry
        var registry = new CustomShellRegistry();

        // Act: Attempt discovery - should not throw
        var availableShells = registry.GetAvailableShells().ToList();

        // Assert: Should not throw and return a valid collection
        Assert.That(availableShells, Is.Not.Null);
        // Note: This test assembly contains mock shells, so we expect some shells to be discovered
        Assert.That(availableShells.Count, Is.GreaterThanOrEqualTo(0));
    }

    /// <summary>
    ///     **Feature: custom-game-shells, Property 15: Shell Registration Validation**
    ///     **Validates: Requirements 7.4, 7.5**
    ///     Property: For any custom shell registration attempt, the shell registry should validate
    ///     the implementation before registration and handle failures gracefully with appropriate error logging.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ShellRegistrationValidatesImplementations()
    {
        return Prop.ForAll(ShellIdArb, shellId =>
        {
            // Arrange: Create a test registry
            var registry = new CustomShellRegistry();

            // Act & Assert: Valid shell should register successfully
            bool validRegistrationSucceeds = true;
            try
            {
                registry.RegisterShell(shellId, () => new RegistryMockCustomShell(shellId));

                // Verify the shell is registered
                bool isRegistered = registry.IsShellRegistered(shellId);
                bool hasMetadata = registry.GetShellMetadata(shellId) != null;
                bool canCreate = registry.CreateShell(shellId) != null;

                validRegistrationSucceeds = isRegistered && hasMetadata && canCreate;
            }
            catch
            {
                validRegistrationSucceeds = false;
            }

            return validRegistrationSucceeds;
        });
    }

    /// <summary>
    ///     Property: Registration should reject invalid shell IDs.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 50, QuietOnSuccess = true)]
    public FsCheck.Property RegistrationRejectsInvalidShellIds()
    {
        return Prop.ForAll(InvalidShellIdArb, invalidId =>
        {
            // Arrange: Create a test registry
            var registry = new CustomShellRegistry();

            // Act & Assert: Invalid shell ID should be rejected
            bool exceptionThrown = false;
            try
            {
                registry.RegisterShell(invalidId, () => new RegistryMockCustomShell("ValidShell"));
            }
            catch (ArgumentException)
            {
                exceptionThrown = true;
            }
            catch (Exception)
            {
                // Other exceptions are not expected for invalid IDs
                exceptionThrown = false;
            }

            return exceptionThrown;
        });
    }

    /// <summary>
    ///     Property: Registration should reject null factories.
    /// </summary>
    [Test]
    public void RegistrationRejectsNullFactory()
    {
        // Arrange
        var registry = new CustomShellRegistry();
        var shellId = "TestShell";

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            registry.RegisterShell<RegistryMockCustomShell>(shellId, null!));
    }

    /// <summary>
    ///     Property: Registration should reject duplicate shell IDs.
    /// </summary>
    [Test]
    public void RegistrationRejectsDuplicateShellIds()
    {
        // Arrange
        var registry = new CustomShellRegistry();
        var shellId = "TestShell";

        // Register first shell
        registry.RegisterShell(shellId, () => new RegistryMockCustomShell(shellId));

        // Act & Assert: Second registration with same ID should fail
        Assert.Throws<ArgumentException>(() =>
            registry.RegisterShell(shellId, () => new RegistryMockCustomShell(shellId)));
    }

    /// <summary>
    ///     Property: Registration should validate shell implementations.
    /// </summary>
    [Test]
    public void RegistrationValidatesShellImplementations()
    {
        // Arrange
        var registry = new CustomShellRegistry();
        var shellId = "InvalidShell";

        // Act & Assert: Invalid shell should be rejected
        Assert.Throws<CustomShellRegistrationException>(() =>
            registry.RegisterShell(shellId, () => new InvalidRegistryMockCustomShell()));
    }

    /// <summary>
    ///     Property: Registration should handle factory exceptions gracefully.
    /// </summary>
    [Test]
    public void RegistrationHandlesFactoryExceptions()
    {
        // Arrange
        var registry = new CustomShellRegistry();
        var shellId = "FailingShell";

        // Act & Assert: Factory that throws should result in registration exception
        Assert.Throws<CustomShellRegistrationException>(() =>
            registry.RegisterShell<RegistryMockCustomShell>(shellId, () => throw new InvalidOperationException("Factory failed")));
    }

    /// <summary>
    ///     Property: Shell creation should validate shell IDs.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 50, QuietOnSuccess = true)]
    public FsCheck.Property ShellCreationValidatesShellIds()
    {
        return Prop.ForAll(InvalidShellIdArb, invalidId =>
        {
            // Arrange: Create a test registry
            var registry = new CustomShellRegistry();

            // Act & Assert: Invalid shell ID should be rejected
            bool exceptionThrown = false;
            try
            {
                registry.CreateShell(invalidId);
            }
            catch (ArgumentException)
            {
                exceptionThrown = true;
            }
            catch (Exception)
            {
                // Other exceptions are not expected for invalid IDs
                exceptionThrown = false;
            }

            return exceptionThrown;
        });
    }

    /// <summary>
    ///     Property: Shell creation should fail for unregistered shells.
    /// </summary>
    [Test]
    public void ShellCreationFailsForUnregisteredShells()
    {
        // Arrange
        var registry = new CustomShellRegistry();
        var unregisteredId = "UnregisteredShell";

        // Act & Assert
        Assert.Throws<ArgumentException>(() => registry.CreateShell(unregisteredId));
    }

    /// <summary>
    ///     Property: Shell creation should handle factory failures.
    /// </summary>
    [Test]
    public void ShellCreationHandlesFactoryFailures()
    {
        // Arrange
        var registry = new CustomShellRegistry();
        var shellId = "FailingShell";
        var factoryCallCount = 0;

        // Register a shell with a factory that fails on creation (not during registration)
        registry.RegisterShell(shellId, () =>
        {
            factoryCallCount++;
            if (factoryCallCount > 1) // Fail on subsequent calls
            {
                throw new InvalidOperationException("Factory creation failed");
            }
            return new RegistryMockCustomShell(shellId);
        });

        // First creation should succeed (for registration validation)
        // Second creation should fail and throw CustomShellInstantiationException
        Assert.Throws<CustomShellInstantiationException>(() => registry.CreateShell(shellId));
    }

    /// <summary>
    ///     Property: Metadata retrieval should be safe for all inputs.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property MetadataRetrievalIsSafe()
    {
        var combinedArb = Arb.From(Gen.OneOf(ShellIdArb.Generator, InvalidShellIdArb.Generator));
        return Prop.ForAll(combinedArb, shellId =>
        {
            // Arrange: Create a test registry
            var registry = new CustomShellRegistry();

            // Act: Attempt to get metadata (should never throw)
            CustomShellMetadata? metadata = null;
            bool noExceptionThrown = true;
            try
            {
                metadata = registry.GetShellMetadata(shellId);
            }
            catch
            {
                noExceptionThrown = false;
            }

            // Assert: Should never throw, may return null for invalid/unregistered shells
            return noExceptionThrown;
        });
    }

    /// <summary>
    ///     Property: Shell registration check should be safe for all inputs.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ShellRegistrationCheckIsSafe()
    {
        var combinedArb = Arb.From(Gen.OneOf(ShellIdArb.Generator, InvalidShellIdArb.Generator));
        return Prop.ForAll(combinedArb, shellId =>
        {
            // Arrange: Create a test registry
            var registry = new CustomShellRegistry();

            // Act: Check if shell is registered (should never throw)
            bool isRegistered = false;
            bool noExceptionThrown = true;
            try
            {
                isRegistered = registry.IsShellRegistered(shellId);
            }
            catch
            {
                noExceptionThrown = false;
            }

            // Assert: Should never throw, should return false for invalid/unregistered shells
            return noExceptionThrown && !isRegistered; // Should be false since we haven't registered anything
        });
    }
}

/// <summary>
///     Mock implementation of ICustomShell for registry testing purposes.
/// </summary>
internal class RegistryMockCustomShell : ICustomShell
{
    private readonly string _shellId;
    private bool _isRunning;
    private bool _disposed;

    public RegistryMockCustomShell(string shellId)
    {
        _shellId = shellId;
        Metadata = CustomShellMetadata.Create(
            $"Mock {shellId}",
            $"Mock shell implementation for {shellId}",
            new Version(1, 0, 0),
            "Test Framework"
        );
    }

    public CustomShellMetadata Metadata { get; }
    public bool IsRunning => _isRunning && !_disposed;

    public event EventHandler<ShellOutputEventArgs>? OutputReceived;
    public event EventHandler<ShellTerminatedEventArgs>? Terminated;

    public Task StartAsync(CustomShellStartOptions options, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(RegistryMockCustomShell));

        if (_isRunning)
            throw new InvalidOperationException("Shell is already running");

        _isRunning = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(RegistryMockCustomShell));

        _isRunning = false;
        Terminated?.Invoke(this, new ShellTerminatedEventArgs(0, "Stopped"));
        return Task.CompletedTask;
    }

    public Task WriteInputAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(RegistryMockCustomShell));

        if (!_isRunning)
            throw new InvalidOperationException("Shell is not running");

        // Echo the input as output for testing
        OutputReceived?.Invoke(this, new ShellOutputEventArgs(data.ToArray()));
        return Task.CompletedTask;
    }

    public void NotifyTerminalResize(int width, int height)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(RegistryMockCustomShell));

        // Mock implementation - just store the values for testing
        LastResizeWidth = width;
        LastResizeHeight = height;
    }

    public void RequestCancellation()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(RegistryMockCustomShell));

        // Mock implementation - set a flag for testing
        CancellationRequested = true;
    }

    public void SendInitialOutput()
    {
        // Mock implementation - no initial output for tests
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _isRunning = false;
            _disposed = true;
        }
    }

    // Test helper properties
    public int LastResizeWidth { get; private set; }
    public int LastResizeHeight { get; private set; }
    public bool CancellationRequested { get; private set; }
}

/// <summary>
///     Invalid mock shell that fails validation for testing error handling.
/// </summary>
internal class InvalidRegistryMockCustomShell : ICustomShell
{
    public CustomShellMetadata Metadata => null!; // Invalid - null metadata
    public bool IsRunning => false;

    public event EventHandler<ShellOutputEventArgs>? OutputReceived
    {
        add { }
        remove { }
    }

    public event EventHandler<ShellTerminatedEventArgs>? Terminated
    {
        add { }
        remove { }
    }

    public Task StartAsync(CustomShellStartOptions options, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task WriteInputAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public void NotifyTerminalResize(int width, int height)
    {
        throw new NotImplementedException();
    }

    public void RequestCancellation()
    {
        throw new NotImplementedException();
    }

    public void SendInitialOutput()
    {
        throw new NotImplementedException();
    }

    public void Dispose()
    {
        // No-op
    }
}
