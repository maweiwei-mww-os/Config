# Source Independent

1. Import this sample from Package Manager. No game project is required.
2. Run `Tools > Game-Json > Verify source-independent sample`.
3. The command compares JSON, reordered CSV and a C#-defined schema/document.
   It checks identical binary bytes AND generated C#; Console prints PASS.
4. Add `SourceSampleReader` to a GameObject, assign the shipped `SourceConfig.bytes`,
   and enter Play mode. Tile 5 grants 20 coins, named Welcome.

The reader assembly references only `Game.Json.Compact`. The Editor assembly
owns importing/compiling and is excluded from the player. `SourceConfig.g.cs`
and `SourceConfig.bytes` are included, so the sample can run before regeneration.
CSV and C# inputs here do not use JSON to define their schema.

Change all three inputs consistently before checking equivalence. This is a
correctness example, not a performance benchmark. Its Console logging allocates.
See the package VERIFICATION.md for calibrated warmed-read measurements.
