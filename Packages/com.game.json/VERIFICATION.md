# Verification - 0.4.0

Date: 2026-10-09. Observed Windows results, not a platform-wide guarantee.

| Check | Result |
| --- | --- |
| Independent .NET 8 framework suite | 30 passed |
| Source-independent build and adapter suite | 18 passed, including frozen 0.3.1 binary/code compatibility |
| Existing game consumer regression | 28 passed |
| No-JSON consumer | Separate runtime/build/CSV DLLs, no JSON DLL present; 200,000 reads, 0 B |
| CSV CLI | Binary and generated API match shipped sample |
| Install tgz into independent Unity project | Passed |
| Three-source sample | JSON, CSV and C# schema/data: identical binary and generated API, executed in Unity Editor |
| Player source equivalence | JSON and CSV fixture bytes match; same generated facade reads both |
| Unity 2022.3.60f1c1 Windows x64 IL2CPP | Built and executed successfully |
| Build configuration | Development, High managed stripping |
| Generated fixture assembly | Separate assembly, no linker preservation directive |
| Typed fields, references, row/list iteration, Capture | 200,000 iterations, 0 allocation events |
| Allocation calibration | Empty: 0 events; new 4 KB array: 1 event |
| Typed DTO benchmark | 3,000 / 30,000 / 300,000 records; four backends; five workloads |
| Benchmark read allocations | Zero for every tested backend/workload/scale |
| Lifetime and fault tests | 1000 updates, retained old snapshot, retired graph collection, failed loads |
| Concurrent publication | Multiple readers, competing commits, cancellation races, stale/foreign/reused tickets |

## IL2CPP Measurements

Three tables with 100,000 records each; median warm in-process times. Input
buffers are already loaded and excluded from retained managed graph estimates.
1 MiB = 1,048,576 bytes. These are not process peak-memory or FPS measurements.

| Metric | Compact FastLookup | Typed DTO + Dictionary | DTO + pooled strings | Compact small index |
| --- | ---: | ---: | ---: | ---: |
| Retained managed MiB | 9.37 | 39.47 | 35.40 | 7.29 |
| Load and validate ms | 169.38 | 735.13 | 750.01 | 165.81 |
| Dense mixed read ns/op | 513.47 | 795.96 | 797.73 | 460.91 |
| Sparse hit ns/op | 203.14 | 200.24 | 198.79 | 438.50 |
| Missing ID ns/op | 121.18 | 72.32 | 82.61 | 384.89 |
| Row scan ns/element | 25.46 | 8.38 | 8.73 | 26.76 |
| Numeric column scan ns/element | 0.87 | 8.64 | 7.30 | 0.94 |
| Warm read allocated bytes | 0 | 0 | 0 | 0 |

The DTO baseline uses Unity JsonUtility, typed row arrays and prebuilt indexes,
not the weaker ObjectConfig dictionary-per-row baseline. Both baselines validate
IDs/references; compact loading additionally checks SHA256 and binds the generated
contract. Equal logical values and query checksums are required. JSON shapes
differ: source row-array JSON is 10,009,787 UTF-8 bytes; DTO JSON is 18,709,305;
compiled binary is 7,613,076 bytes. Offline compilation is excluded from runtime
loading, not claimed to be free. .NET reports record its compilation time.

Load timing uses five interleaved rounds; queries use seven. Reports preserve
minimum/median/maximum, not just one favorable sample. Final .NET and IL2CPP runs
are sequential, after the Unity native build exits. Allocation recording runs
separately from timing. Unity uses GC.Alloc event counts; marker duration is
not an allocated byte count. Load allocated bytes are unavailable (-1), while
load event counts are reported. Calibrated zero events supports zero allocation
on the tested synchronous read path. .NET uses a byte counter and is reported
separately; it uses Newtonsoft.Json for its DTO baseline.

Important negative results: small-table mixed lookup can be slower than DTOs;
sparse/missing lookups are not universally faster. IL2CPP row scanning has
significant wrapper overhead. Use primitive ReadOnlySpan column access for bulk
statistics; the original row-scan results remain visible. FastLookup spends
about 2.1 MiB more than binary-search indexing at this scale. String pooling alone
does not account for the full retained-memory difference.

These are new 0.4.0 measurements, not copied 0.3.1 numbers or a claim of a
cross-version speedup. Host timing varies: for example the 300k CompactFastLookup
mixed-query range was 307.60..540.17 ns/op. Keep the raw range and rerun on target
hardware before setting a performance budget. The source abstraction executes
during construction, not in each runtime lookup.

## Reproduce

From the source checkout:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/verify-framework.ps1 -IncludeGameRegression
dotnet run --project Tests/BenchmarkTests.csproj -c Release
powershell -ExecutionPolicy Bypass -File scripts/pack-framework.ps1
powershell -ExecutionPolicy Bypass -File scripts/verify-framework-unity.ps1 -Target IL2CPP -Benchmark
powershell -ExecutionPolicy Bypass -File scripts/run-framework-benchmark.ps1
```

The final command runs the existing executable without launching the Editor and
prints a table. Unity build verification needs the Windows IL2CPP module, an
appropriate C++ toolchain, a valid Unity license and sufficient temporary disk
space. The full script was executed, not merely syntax-checked. The standalone
benchmark script also completed independently. Frozen raw reports and source
hashes are retained in `Tests/Evidence/0.4.0`; working logs are in `Artifacts`.

## Boundaries

Loading, binding, publication and error paths allocate. A captured old row keeps
its graph alive; multiple versions can coexist, so updates need memory headroom.
The 1000-update test verifies reachability/reclamation, not an hours-long mobile
soak or a precise peak-memory bound. Checksum is corruption detection, not
authentication. Publication is in-memory, not crash-atomic multi-file delivery.

The new typed model is a build-time representation, not compact runtime storage.
JSON/CSV importers materialize input; maximum binary size is not a bound on build
peak memory. R05's device loading/update peak-memory acceptance remains open.
Native XLSX, arbitrary object graphs, nullable fields and composite keys are not
implemented. The complete UPM package still declares Newtonsoft for the JSON
adapter, while runtime/build assemblies do not depend on it. Old public method
signatures remain, but custom asmdef references and precompiled consumers must
account for assembly relocation. Review was a separate self-review; no independent
reviewer agent was available in this session.

Android/iOS hardware, Release-player allocation instrumentation, Lua bridging,
remote signing/download and arbitrary nested JSON are not claimed. Generated
access does not use runtime reflection; the benchmark's DTO/report reflection
is preserved in its separate consumer assembly. Interview scores remain human
judgments; this report supplies reproducible engineering evidence, not a score
guarantee.
