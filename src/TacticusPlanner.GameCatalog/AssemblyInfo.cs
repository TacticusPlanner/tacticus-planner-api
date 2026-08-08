using System.Runtime.CompilerServices;

// Lets the events denormalization/projection logic (internal, deterministic given an injected "now") be
// unit tested directly rather than only indirectly through GameCatalogLoader.Load()'s wall-clock time.
[assembly: InternalsVisibleTo("TacticusPlanner.GameCatalog.Tests")]
