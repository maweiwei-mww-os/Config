# Changelog

## 0.4.0

- Separate runtime, source-independent build core, JSON adapter and CSV adapter assemblies.
- Add immutable typed ConfigDocument/ConfigSchema/ConfigValue and IConfigSource.
- Centralize schema, explicit default, scalar and reference validation for all sources.
- Add strict CSV import, generic Editor build entry and offline CSV compilation.
- Preserve existing JSON entry points and GJC2 layout; preserve date-like strings.
- Add a three-source package sample and no-JSON consumer verification.
- Assembly migration: custom asmdefs using ConfigCompiler, ConfigCodeGenerator or
  ObjectConfig must now reference Game.Config.Json; build integrations also use
  Game.Config.Build. Runtime-only users keep Game.Json.Compact.

## 0.3.1

- Add generated read-only column handles and zero-allocation primitive spans
  for bulk scanning, following the measured IL2CPP row-iteration overhead.
- Keep row scanning and column scanning as distinct benchmark workloads.

## 0.3.0

- Add int64, finite float32 and bit-packed boolean columns and generated access.
- Use arithmetic indexing for contiguous IDs; offer fast sparse hash indexing
  or lower-memory binary search without changing compiled assets.
- Cache generated table counts and inline small row/iteration accessors.
- Discard prepared updates with IDisposable; serialize discard against commit
  and release candidate ownership after either operation.
- Add scalar boundaries, index equivalence, cancellation races and 1000-update
  lifetime tests. Add typed DTO and pooled-string baselines at three scales.

## 0.2.0

- Generate typed read-only table/row APIs and allocation-free struct enumerators.
- Bind typed column views, preserving the dynamic API and reference backend.
- Preserve cross-table reference metadata in GJC2; validate references in the
  binary loader and generated contract. Read legacy GJC1 with the dynamic API.
- Add compatible additive schema changes and explicit breaking-change rejection.
- Prepare and atomically commit versioned snapshots with stale/foreign/reused
  candidate detection, opt-in increasing revisions and lock-free captured reads.
- Add Typed Snapshots sample, editor generation command and offline build tool.
- Add independent framework tests for compatibility, concurrency, allocations and
  lifetime. This release does not implement remote delivery or disk transactions.

## 0.1.0

- Initial column-array configuration, compiler, dynamic binding and quick start.
