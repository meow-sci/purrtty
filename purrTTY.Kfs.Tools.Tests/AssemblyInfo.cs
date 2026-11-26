using NUnit.Framework;

// Process-wide state is shared by the tests of this assembly: the game assembly resolver, Harmony
// patches, the game's SystemHeaders statics and PartsRegistry, all owned by the one KfsRuntime that
// Runtime/ToolsRuntimeSetUp.cs starts for namespace purrTTY.Kfs.Tools.Tests.Runtime. Running nothing
// in parallel keeps that namespace's suite, set-up fixture included, one block.
[assembly: NonParallelizable]
[assembly: LevelOfParallelism(1)]
