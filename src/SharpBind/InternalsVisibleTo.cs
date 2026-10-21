using System.Runtime.CompilerServices;

// Test fixtures expose allocation counts through their own API, never through SharpBind's public API.
[assembly: InternalsVisibleTo("Fixture")]
[assembly: InternalsVisibleTo("SharpBind.Tests")]
