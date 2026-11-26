using System.Runtime.CompilerServices;

// The providers' Fault seams (an exception injected inside a provider must become an LSP error
// response) are internal; the language-server tests reach them.
[assembly: InternalsVisibleTo("purrTTY.Kfs.Tools.Tests")]
