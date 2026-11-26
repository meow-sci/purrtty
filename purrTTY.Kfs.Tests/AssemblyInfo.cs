using NUnit.Framework;

// Process-wide state is shared by the tests of this assembly: Harmony patches, the game's
// SystemHeaders statics and PartsRegistry. The tests under purrTTY.Kfs.Tests.Runtime run inside a
// KfsRuntime that keeps the header and the host hooks installed for their whole namespace
// (Runtime/RuntimeSetUp.cs), while CatalogCompileTests register the header briefly and assert it is
// gone afterwards, and the tests under purrTTY.Kfs.Tests.Installer install and uninstall the whole
// extension (patches, header, KfsIntegration._tables) and assert the process is clean after each.
// Running nothing in parallel makes these never overlap: NUnit then runs a namespace's suite,
// set-up fixture included, as one block.
[assembly: NonParallelizable]
[assembly: LevelOfParallelism(1)]
