# Game Config - Compact Runtime

Unity 2022.3 package. No dependency on Dice Town, UT-RS, xLua or a service.
Uses the standard `com.unity.nuget.newtonsoft-json` dependency, not a vendored assembly.
Package identity `com.game.json` and C# namespace `GameJson` are preserved.
See `使用指南.zh-CN.md` for the Chinese integration guide.

## Source-independent build (0.4)

JSON / CSV / your `IConfigSource` -> immutable `ConfigDocument` -> shared
validation and `ConfigBuild` -> GJC2 binary + generated C# -> compact runtime.
Runtime queries do not parse a source format or traverse a generic object tree.

| Assembly | Responsibility | Dependencies |
| --- | --- | --- |
| Game.Json.Compact | Load, typed columns, generated-view contracts, snapshots | No JSON/build dependency |
| Game.Config.Build | Typed model, common validation, compiler, code generator | Runtime |
| Game.Config.Json | JSON source and legacy JSON wrappers/ObjectConfig | Build, Runtime, Newtonsoft |
| Game.Config.Csv | Strict CSV source with explicit schema | Build, Runtime |

Import **Source Independent** and run **Tools > Game-Json > Verify source-independent sample**.
It compares three sources byte-for-byte and ships a generated runtime-only reader.
CSV and C# schemas in that sample are constructed in code without JSON.
Implement `IConfigSource.Read()` to adapt another source to `ConfigDocument`.
Supporting an adapter does not mean supporting every arbitrary data shape.

```csharp
var document = yourSource.Read(); // IConfigSource; build-time allocations allowed
byte[] binary = ConfigBuild.Compile(document);
string code = ConfigBuild.Generate(document, "MyGame.Config", "GameConfig");
```

Types are int32, int64, finite float32, bool, string and int32 list. Nullable
cells, arbitrary objects and native XLSX are not implemented. Excel can export
CSV. Missing values only use an explicitly declared, matching typed default;
explicit null is invalid. Defaults and references are validated before output.
CSV headers map by exact field name, may reorder or omit defaulted fields, and
reject unknown/duplicate headers. Quoted commas, doubled quotes and multiline
strings are supported; numbers use invariant culture, lists use `;`, booleans
use true/false. An empty string or list is valid; an empty numeric cell is not.
Blank records are not skipped. Text is not globally trimmed. The common model
copies caller-owned lists; it is a build representation, not compact runtime storage.

Custom asmdefs using old `ConfigCompiler`, `ConfigCodeGenerator` or `ObjectConfig`
must add `Game.Config.Json`; code calling new build APIs adds `Game.Config.Build`.
Existing source method signatures remain, but assembly-qualified/precompiled
consumers need rebuilding. JSON remains a declared UPM dependency for the included
adapter and tools; this is not a claim that the complete package has no JSON dependency.
The runtime and build DLLs themselves do not reference it. A separately compiled
no-JSON consumer verifies CSV compilation and runtime reads without those DLLs.

## Install and use

Install `com.game.json-0.4.0.tgz` using Package Manager > Add package from tarball.
Alternatively select this directory's `package.json` with Add package from disk.
Import the Quick Start sample. Select its `rewards.json`, then run
Tools > Game-Json > Compile selected JSON. The adjacent `rewards_packed.bytes`
is the runtime artifact. Attach ReadConfigExample to a GameObject and assign it.

```csharp
var config = CompactConfig.Load(packedBytes);
var rewards = config.Table("Reward");
int coins = rewards.Bind("coins", FieldKind.Int); // Bind once.
int row = rewards.Find(1);
if (row >= 0) { int value = rewards.Int(row, coins); }
```

Source JSON has a positive `version` and `tables`. Each table has `name`, `fields`,
and array-shaped `rows`. The first field must be `id:int`; IDs are unique and
nonnegative. Supported field types: `int`, `long`, `float`, `bool`, `string`, `int[]`. Add `ref:"OtherTable"`
to an integer or integer-list field for cross-table ID validation. Mixed nested
records are normalized to referenced tables, not arbitrary JSON object trees.

The compiler validates types, overflow, row width, duplicates and references;
sorts tables and rows deterministically; and writes a checksummed binary file.
The loader validates before exposing data, creates column arrays, uses a bounded
index selected for the ID distribution, and shares strings within a column. No GCHandle,
native allocation, unsafe code, runtime reflection for field access or global
string interning. This is column-array storage, not memory mapping or zero-copy loading.

`long` preserves 64-bit integer values; `float` is IEEE float32 (finite values
only, with normal rounding); booleans use one bit per row. Extended scalar
columns require runtime 0.3 or later. Old three-type GJC1/GJC2 data remains readable.

Contiguous IDs use arithmetic indexing without an extra index array. Mostly
dense IDs use a bounded row-offset array. Other IDs use `Dictionary<int,int>`
under the default `ConfigIndexPolicy.FastLookup`. Choose
`CompactConfig.Load(bytes, ConfigIndexPolicy.Compact)` to use binary search for
sparse IDs and save index memory, at the cost of O(log n) lookups. The binary
artifact is unchanged by this runtime policy. No claim that every lookup beats
a typed object dictionary is made.

`IntList` is a read-only value view. No mutable arrays are exposed. Bind field
slots once against their owning table, then read by row. Slots belong to that
table; do not reuse them for a different schema. String reads return strings
created during loading. Loading allocates; normal warmed reads do not require
new managed objects. Missing IDs return -1; invalid fields/types throw.

## Generated access

Import the **Typed Snapshots** sample. It already contains generated C# and a
compiled binary; it does not depend on the game. For your own JSON, select it
and run **Tools > Game-Json > Compile and generate typed API**. Names must be
ASCII C# identifiers; keyword escaping and symbol-collision checks are built in.
An explicit namespace/class name is available through `ConfigMenus.GenerateFile`.
Attach `TypedSnapshotExample`, assign `ExampleConfig.bytes` as InitialConfig and
`ExampleConfigUpdate.bytes` as UpdatedConfig, then use its context-menu commands
while playing to publish the update and release the captured old turn.

```csharp
var config = new ExampleConfig(CompactConfig.Load(packedBytes));
int coins = config.Reward.Get(1).Coins;
int referenced = config.Tile.Get(5).RewardRecord.Coins;
foreach (var reward in config.Reward)
    foreach (int tag in reward.Tags) { /* consume tag */ }
```

The generated facade binds immutable column handles once. Compact reads go
directly through typed arrays without field-name lookup or repeated type checks.
Rows and enumerators are structs, not materialized row objects. Strings and list
storage are shared; no mutable array is exposed. `TryGet` handles missing IDs,
`Get` throws, and reading a default/invalid row throws. Enumeration deliberately
does not implement `IEnumerable<T>`: LINQ and interface boxing are not part of
the allocation-free path. Normal reads and iteration allocate no new managed
objects after warmup; load, bind and exceptional paths do allocate.

Numeric bulk scans can avoid per-row facade calls, which matters on IL2CPP:

```csharp
if (config.Reward.CoinsColumn.TryGetSpan(out var coins)) {
    long total = 0;
    for (int i = 0; i < coins.Length; i++) total += coins[i];
}
```

Generated tables expose read-only column handles. `int`, `long` and `float`
columns support `TryGetSpan(out ReadOnlySpan<T>)` on compact storage; custom
backends without contiguous arrays return false, without copying. Spans are
stack-scoped views, cannot cross await/yield, and do not expose mutable arrays.
Use row APIs for cross-table navigation; use numeric spans for bulk filters and
statistics. Span indices are sorted row positions, not primary-key IDs; use
`At(index)` to obtain the corresponding row. Neither API guarantees a speedup
for every workload. The caller must not mutate input byte arrays during loading.

## Contract compatibility

- Required table/field removal or a field-type change fails while binding.
- Extra fields/tables and field reordering are accepted; bindings use names once.
- Changing or removing a required reference target is rejected, even if IDs in
  both targets happen to overlap. The client-generated contract is authoritative.
- Scalar references expose `FieldRecord`; list references expose
  `ResolveField(index)`. They resolve against the owning snapshot, never a global
  store, so a captured old row cannot accidentally cross into a newer version.
- Format GJC2 retains reference metadata and validates references when loading
  the binary, not only when compiling JSON. GJC1 remains readable by the dynamic
  API. Generated contracts containing references require recompiling old GJC1
  assets; their missing reference metadata is not silently trusted.
- `version` is a content revision, not a binary format version. The two are
  separate. The hash detects corruption, not authenticity or an authorized update.

## Prepared snapshot publication

```csharp
var store = new ConfigStore<ExampleConfig>(
    data => new ExampleConfig(data), ConfigVersionPolicy.Increasing);

// Caller chooses the scheduler. Load/bind may run off-thread if the loader and
// binder use only thread-safe managed APIs. Fetch Unity TextAsset.bytes first.
if (!store.TryPrepare(() => CompactConfig.Load(candidateBytes),
        out var candidate, out var error) || !store.TryCommit(candidate, out error))
    throw new System.InvalidOperationException(error.Message);

var turn = store.Capture(); // capture ONCE for a multi-read business operation
int reward = turn.Value.Tile.Get(5).RewardRecord.Coins;
```

Preparation never changes the active snapshot. It captures the current
generation before loading and validating. Commit serializes publishers, rejects
stale/foreign/reused candidates and atomically publishes one complete snapshot.
`Increasing` rejects equal and older revisions. The default `AllowAny` preserves
the 0.1 dynamic switching behavior; select the policy deliberately.

`Capture` is an allocation-free volatile reference read; readers take no lock.
`Version`, `Generation` and `Value` are published together. Repeated `Current`
reads can observe different versions, so use one captured snapshot when atomic
cross-table consistency matters. Caller binders must be side-effect free and
return immutable, thread-safe data. Publication cannot make mutable user objects
thread-safe. With concurrent preparation the binder itself must also be safe.

Old rows/snapshots stay valid while referenced and are reclaimed by managed GC
when released. A retained row retains its owning snapshot, including other
tables; do not cache rows indefinitely across updates. A prepared ticket supports
`IDisposable`: dispose it when an update is cancelled or rejected. Commit and
discard are serialized, and both release the ticket's candidate reference.
Discarded tickets cannot be committed; disposing after a commit is harmless.
Retaining a ticket still retains its owning store, so do not keep ticket history
indefinitely. There is no forced snapshot disposal or native
pointer invalidation. Publication is in-memory, not a crash-atomic downloader or
multi-file installer. A stale preparation must be rebuilt, not blindly retried.

The ObjectConfig backend is a conventional row-dictionary JSON reference
implementation with an eager ID index. It is NOT a copy of the original
Game-Json CompactConfig implementation. Both backends can have allocation-free
hot reads; avoiding allocations alone is not a reason to claim one is superior.

The source checkout has independent `Tests/FrameworkTests.csproj` and
`scripts/verify-framework.ps1`; neither requires game source. The optional
`-IncludeGameRegression` adds existing consumer compatibility tests. The offline
compiler/generator is `Tools/GameJson.Tool`; Unity users need no .NET SDK.
Generated code uses no runtime reflection, Reflection.Emit or dynamic compilation.
That is a design property, not proof of every AOT platform: consult the shipped
verification report for the exact targets tested. Android/iOS, Lua and long-run
device profiling are not implied by .NET or desktop results.
