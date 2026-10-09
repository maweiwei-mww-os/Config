# Source-Independent Configuration Package Implementation Plan

> For agentic workers: use superpowers:executing-plans. Test each boundary before implementation.

**Goal:** Ship com.game.json 0.4.0 as a source-independent configuration package, preserving compact storage, typed access and calibrated allocation-free reads.
**Architecture:** Immutable typed build model -> common validation -> unchanged GJC2 runtime layout. JSON and CSV are adapters. Runtime, build core and adapters are separate Unity assemblies.
**Tech Stack:** C#, Unity 2022.3, .NET 8 verification, existing Newtonsoft.Json only in JSON compatibility adapter.
**Spec:** Docs/ConfigFramework-Requirements-Analysis.zh-CN.md.

## Constraints and Rulings

- This project is not a Git checkout; keep changes in this existing dedicated project, no invented branch or commits.
- Preserve GameJson namespace, package identity, generated consumer API and old string-based JSON entry points.
- First release keeps the six existing field kinds and nonnegative int32 primary keys. Add explicit defaults; reject unsupported types rather than silently boxing at runtime.
- JSON, CSV and programmatic IConfigSource exercise the abstraction. Excel exports may use CSV; native XLSX is not claimed.
- Runtime data layout and successful hot-path implementation remain unchanged. Generic source adaptation is build-time only.
- CSV uses a documented strict comma/quote dialect, invariant scalars and semicolon-separated int lists; no extra package dependency.
- No fresh reviewer tool is available; perform a separate self-review and disclose this limitation.

## Tasks

- [x] 1. Add cross-source tests, runtime/build assembly-only projects and missing-default/diagnostic tests; observe failures.
- [x] 2. Implement ConfigDocument/Schema/Value and IConfigSource; common validation and ConfigBuild; move compiler/generator out of runtime assembly.
- [x] 3. Implement JSON/CSV adapters and legacy wrappers; prove identical bytes and generated code from all sources; run existing regressions.
- [x] 4. Add CLI CSV mode, Editor generic source entry, independent package sample and source isolation smoke tests.
- [x] 5. Package, install in independent Unity project, execute IL2CPP source equivalence and performance checks; freeze fresh evidence and document scope.

## Verification

Run Tests/SourceTests.csproj, Tests/RuntimeOnlyTests.csproj, existing FrameworkTests and CoreTests; then scripts/verify-framework-unity.ps1 -Target IL2CPP -Benchmark and .NET benchmarks. Test strict CSV quoting/headers/defaults, input mutation after construction, source-independent diagnostics, schema mismatch, broken refs, and no JSON assembly reference in runtime/build core.

## Progress

- Requirements reviewed. Existing compiler contains JToken/JArray; runtime refers to compiler magic constants. These dependencies must move, not merely gain interfaces.
- Observed isolation-test compile failure before separating assemblies. Then 11 source tests, 30 framework tests and 28 game regressions passed. Independent no-JSON consumer completed 200,000 warmed reads with 0 B allocation.
- Expanded boundary tests exposed JSON automatic date conversion; reproduced failure and disabled automatic date parsing in the adapter. Full rerun passed.
- Final functional rerun: 18 source tests (including frozen 0.3.1 compatibility), 30 framework tests, 28 game regressions, no-JSON consumer and CSV CLI all passed.
- Package-installed Unity project built Windows x64 IL2CPP, High stripping. Source equivalence and calibrated 200,000 reads passed with 0 allocation events.
- Final .NET and IL2CPP benchmarks ran sequentially after the build. All four backends, three scales and five workloads passed checksum/allocation verification. Current 300k IL2CPP retained graph: CompactFastLookup 9.37 MiB vs typed DTO dictionary 39.47 MiB; negative query/row-scan results retained.
- Final package SHA256: 7374551807F5A6E5FA30284F3335528216300718D623EA6B9D4B96EFBD358D56. Evidence freeze verified source/assembly/data/metadata against the independently installed package. Documentation added after execution; code and data unchanged.
- Separate self-review and requirements mapping are in Docs/Framework-0.4.0.md. Device loading/update peak memory remains an explicit open acceptance item, not a hidden claim of full requirements or a guaranteed interview score.
