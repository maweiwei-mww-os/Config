using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using FrameworkFixture;
using GameJson;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
#if UNITY_2022_3_OR_NEWER
using UnityEngine;
using Unity.Profiling;
#endif

public static class BenchmarkProgram
{
    [Serializable] public sealed class Reward { public int Id, Coins; public string Name; public int[] Tags; public long Price; public float Rate; public bool Enabled; }
    [Serializable] public sealed class Tile { public int Id, Reward; public int[] Bonus; }
    [Serializable] public sealed class Sparse { public int Id, Value; }
    [Serializable] public sealed class Document { public int Version; public Reward[] Rewards; public Tile[] Tiles; public Sparse[] Sparse; }
    sealed class TypedData
    {
        public Document Data;
        public Dictionary<int, Reward> Rewards;
        public Dictionary<int, Tile> Tiles;
        public Dictionary<int, Sparse> Sparse;
        public static TypedData Load(string json, bool poolStrings)
        {
#if UNITY_2022_3_OR_NEWER
            var d = JsonUtility.FromJson<Document>(json);
#else
            var d = JsonConvert.DeserializeObject<Document>(json);
#endif
            var result = new TypedData { Data = d, Rewards = new Dictionary<int, Reward>(d.Rewards.Length),
                Tiles = new Dictionary<int, Tile>(d.Tiles.Length), Sparse = new Dictionary<int, Sparse>(d.Sparse.Length) };
            var pool = poolStrings ? new Dictionary<string, string>(StringComparer.Ordinal) : null;
            foreach (var row in d.Rewards) {
                if (row.Id < 0 || row.Name == null || row.Tags == null || float.IsNaN(row.Rate) || float.IsInfinity(row.Rate)) throw new InvalidDataException();
                if (pool != null) { if (pool.TryGetValue(row.Name, out var existing)) row.Name = existing; else pool.Add(row.Name, row.Name); }
                result.Rewards.Add(row.Id, row);
            }
            foreach (var row in d.Tiles) {
                if (row.Id < 0 || row.Bonus == null || !result.Rewards.ContainsKey(row.Reward)) throw new InvalidDataException();
                foreach (int id in row.Bonus) if (!result.Rewards.ContainsKey(id)) throw new InvalidDataException();
                result.Tiles.Add(row.Id, row);
            }
            foreach (var row in d.Sparse) { if (row.Id < 0) throw new InvalidDataException(); result.Sparse.Add(row.Id, row); }
            return result;
        }
    }
    public sealed class Measurement
    {
        public string Backend;
        public long RetainedManagedBytes, LoadAllocatedBytes, LoadAllocationEvents;
        public double LoadMedianMs, LoadMinMs, LoadMaxMs;
        public List<QueryResult> Queries = new List<QueryResult>();
    }
    public sealed class QueryResult
    {
        public string Workload;
        public int Operations;
        public double MedianNsPerOperation, MinNsPerOperation, MaxNsPerOperation;
        public long AllocatedBytes, Checksum;
    }
    public sealed class ScaleResult
    {
        public int RowsPerTable, TotalRows, BinaryBytes, SourceJsonUtf8Bytes, DtoJsonUtf8Bytes;
        public double OfflineCompileMs;
        public List<Measurement> Backends = new List<Measurement>();
    }
    static long sink;
    static int Main(string[] args) => RunAll(args);
    public static int RunAll(string[] args)
    {
        try {
            var results = new List<ScaleResult>();
            // Warm JSON reflection contracts, generator dependencies and binary reader before measuring.
            var warm = Input(1000);
            GC.KeepAlive(new FixtureConfig(CompactConfig.Load(warm.Binary))); GC.KeepAlive(TypedData.Load(warm.Dto, true));
            var empty = AllocationCounter.Start(); long emptyBytes = empty.Finish();
            var positive = AllocationCounter.Start(); GC.KeepAlive(new byte[4096]); long positiveBytes = positive.Finish();
            if (emptyBytes != 0 || positiveBytes < (CounterMeasuresBytes ? 4096 : 1)) throw new Exception("Allocation counter calibration failed");
            foreach (int count in new[] { 1000, 10000, 100000 }) results.Add(Run(count));
            string output = args.Length == 0 ? "Artifacts/benchmark-results.json" : args[0];
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            File.WriteAllText(output, JsonConvert.SerializeObject(new {
                TimestampUtc = DateTime.UtcNow, Runtime = RuntimeInformation.FrameworkDescription,
                OS = RuntimeInformation.OSDescription, Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                CPU = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
                ProcessorCount = Environment.ProcessorCount, StopwatchFrequency = Stopwatch.Frequency,
                DtoParser = ParserName, AllocationCounterUnit = CounterMeasuresBytes ? "bytes" : "events",
                EmptyControl = emptyBytes, PositiveControl = positiveBytes,
                LoadRounds = 5, QueryRounds = 7, TimingExcludesAllocationProbes = true,
                Scope = "In-process warm managed loading; input buffers preloaded/excluded; post-full-GC retained graph estimate, not process peak; identical values, DTO JSON shape differs; compact includes checksum, reference validation and generated binding.",
                Results = results
            }, Formatting.Indented));
            Console.WriteLine("BENCHMARK VERIFIED " + output); return 0;
        } catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    static ScaleResult Run(int count)
    {
        var input = Input(count); long start; byte[] binary = input.Binary;
        var result = new ScaleResult { RowsPerTable = count, TotalRows = count * 3, BinaryBytes = binary.Length,
            SourceJsonUtf8Bytes = Encoding.UTF8.GetByteCount(input.Source), DtoJsonUtf8Bytes = Encoding.UTF8.GetByteCount(input.Dto),
            OfflineCompileMs = input.CompileMs };
        Func<object>[] loaders = { () => new FixtureConfig(CompactConfig.Load(binary)), () => TypedData.Load(input.Dto, false), () => TypedData.Load(input.Dto, true),
            () => new FixtureConfig(CompactConfig.Load(binary, ConfigIndexPolicy.Compact)) };
        string[] names = { "CompactFastLookup", "TypedDtoDictionary", "TypedDtoDictionaryPooledStrings", "CompactSmallIndex" };
        var graphs = new object[loaders.Length];
        for (int i = 0; i < loaders.Length; i++) {
            long before = FullHeap(); graphs[i] = loaders[i](); long retained = FullHeap() - before;
            if (retained <= 0) throw new Exception("Retained graph measurement unavailable: " + names[i]);
            result.Backends.Add(new Measurement { Backend = names[i], RetainedManagedBytes = retained });
        }
        var loads = Lists<double>(loaders.Length);
        for (int round = 0; round < 5; round++) for (int j = 0; j < loaders.Length; j++) {
            int i = (round + j) % loaders.Length; FullHeap(); start = Stopwatch.GetTimestamp();
            object graph = loaders[i](); double elapsed = ElapsedMs(start);
            GC.KeepAlive(graph); loads[i].Add(elapsed);
        }
        for (int i = 0; i < loaders.Length; i++) {
            FullHeap(); var allocation = AllocationCounter.Start(); var graph = loaders[i](); long allocated = allocation.Finish(); GC.KeepAlive(graph);
            loads[i].Sort(); var m = result.Backends[i];
            m.LoadMedianMs = loads[i][2]; m.LoadMinMs = loads[i][0]; m.LoadMaxMs = loads[i][4];
            m.LoadAllocatedBytes = CounterMeasuresBytes ? allocated : -1;
            m.LoadAllocationEvents = CounterMeasuresBytes ? -1 : allocated;
        }
        var ids = new int[200000]; uint state = 20261009;
        for (int i = 0; i < ids.Length; i++) { state = unchecked(state * 1664525 + 1013904223); ids[i] = (int)(state % (uint)count) + 1; }
        var compact = (FixtureConfig)graphs[0]; var dto = (TypedData)graphs[1]; var pooled = (TypedData)graphs[2]; var small = (FixtureConfig)graphs[3];
        AddQueries(result, "DenseMixed", ids.Length, new Func<long>[] { () => Dense(compact, ids), () => Dense(dto, ids), () => Dense(pooled, ids), () => Dense(small, ids) });
        AddQueries(result, "SparseHit", ids.Length, new Func<long>[] { () => SparseHit(compact, ids), () => SparseHit(dto, ids), () => SparseHit(pooled, ids), () => SparseHit(small, ids) });
        AddQueries(result, "MissingId", ids.Length, new Func<long>[] { () => Missing(compact, ids), () => Missing(dto, ids), () => Missing(pooled, ids), () => Missing(small, ids) });
        AddQueries(result, "Sequential", count * 10, new Func<long>[] { () => Scan(compact), () => Scan(dto), () => Scan(pooled), () => Scan(small) });
        AddQueries(result, "ColumnScan", count * 10, new Func<long>[] { () => ColumnScan(compact), () => Scan(dto), () => Scan(pooled), () => ColumnScan(small) });
        foreach (var m in result.Backends) {
            Console.WriteLine(count + " " + m.Backend + " retained=" + m.RetainedManagedBytes + " load_ms=" + m.LoadMedianMs.ToString("F2"));
            foreach (var q in m.Queries) Console.WriteLine("  " + q.Workload + " ns/op=" + q.MedianNsPerOperation.ToString("F2") + " alloc=" + q.AllocatedBytes);
        }
        GC.KeepAlive(graphs); GC.KeepAlive(input); GC.KeepAlive(binary); return result;
    }
    static void AddQueries(ScaleResult result, string workload, int operations, Func<long>[] actions)
    {
        long expected = actions[0]();
        foreach (var action in actions) for (int i = 0; i < 3; i++) if (action() != expected) throw new Exception(workload + " checksum mismatch");
        var times = Lists<double>(actions.Length);
        var allocated = new long[actions.Length];
        for (int round = 0; round < 7; round++) for (int j = 0; j < actions.Length; j++) {
            int i = (round + j) % actions.Length; long start = Stopwatch.GetTimestamp();
            long sum = actions[i](); long ticks = Stopwatch.GetTimestamp() - start;
            if (sum != expected) throw new Exception(workload + " correctness failed");
            sink = sum; times[i].Add(ticks * 1e9 / Stopwatch.Frequency / operations);
        }
        for (int i = 0; i < actions.Length; i++) {
            var allocation = AllocationCounter.Start(); long sum = actions[i](); long bytes = allocation.Finish();
            if (sum != expected || bytes != 0) throw new Exception(workload + " correctness/allocation failed: " + bytes);
            allocated[i] = bytes;
        }
        for (int i = 0; i < actions.Length; i++) { times[i].Sort(); result.Backends[i].Queries.Add(new QueryResult {
            Workload = workload, Operations = operations, MedianNsPerOperation = times[i][3], MinNsPerOperation = times[i][0],
            MaxNsPerOperation = times[i][6], AllocatedBytes = allocated[i], Checksum = expected }); }
    }
    [MethodImpl(MethodImplOptions.NoInlining)] static long Dense(FixtureConfig c, int[] ids)
    { long s = 0; foreach (int id in ids) { var r = c.Tile.Get(id).RewardRecord; s += r.Coins + r.Name.Length + r.Tags[0] + (r.Enabled ? r.Price : 0) + (long)(r.Rate * 4); } return s; }
    [MethodImpl(MethodImplOptions.NoInlining)] static long Dense(TypedData c, int[] ids)
    { long s = 0; foreach (int id in ids) { var r = c.Rewards[c.Tiles[id].Reward]; s += r.Coins + r.Name.Length + r.Tags[0] + (r.Enabled ? r.Price : 0) + (long)(r.Rate * 4); } return s; }
    [MethodImpl(MethodImplOptions.NoInlining)] static long SparseHit(FixtureConfig c, int[] ids)
    { long s = 0; foreach (int id in ids) s += c.Sparse.Get(id * 7919 + 1).Value; return s; }
    [MethodImpl(MethodImplOptions.NoInlining)] static long SparseHit(TypedData c, int[] ids)
    { long s = 0; foreach (int id in ids) s += c.Sparse[id * 7919 + 1].Value; return s; }
    [MethodImpl(MethodImplOptions.NoInlining)] static long Missing(FixtureConfig c, int[] ids)
    { long s = 0; foreach (int id in ids) if (!c.Sparse.TryGet(id * 7919 + 2, out _)) s++; return s; }
    [MethodImpl(MethodImplOptions.NoInlining)] static long Missing(TypedData c, int[] ids)
    { long s = 0; foreach (int id in ids) if (!c.Sparse.TryGetValue(id * 7919 + 2, out _)) s++; return s; }
    [MethodImpl(MethodImplOptions.NoInlining)] static long Scan(FixtureConfig c)
    { long s = 0; for (int n = 0; n < 10; n++) foreach (var r in c.Reward) s += r.Coins; return s; }
    [MethodImpl(MethodImplOptions.NoInlining)] static long Scan(TypedData c)
    { long s = 0; for (int n = 0; n < 10; n++) foreach (var r in c.Data.Rewards) s += r.Coins; return s; }
    [MethodImpl(MethodImplOptions.NoInlining)] static long ColumnScan(FixtureConfig c)
    {
        if (!c.Reward.CoinsColumn.TryGetSpan(out var column)) throw new Exception("Contiguous numeric column unavailable");
        long s = 0; for (int n = 0; n < 10; n++) for (int i = 0; i < column.Length; i++) s += column[i]; return s;
    }
    static long FullHeap() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); return GC.GetTotalMemory(true); }
    static List<T>[] Lists<T>(int count) { var lists = new List<T>[count]; for (int i = 0; i < count; i++) lists[i] = new List<T>(); return lists; }
    static double ElapsedMs(long start) => (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    public static (string Source, string Dto) Generate(int count, string fixture = "Tests/Fixtures/framework.json")
    {
        var source = JObject.Parse(File.ReadAllText(fixture));
        var rewards = new JArray(); var tiles = new JArray(); var sparse = new JArray();
        var dto = new Document { Version = 1, Rewards = new Reward[count], Tiles = new Tile[count], Sparse = new Sparse[count] };
        for (int i = 1; i <= count; i++) {
            string name = "district-" + (i % 32); int coins = i % 1000; int[] tags = { i % 7, i % 11, i % 13 };
            long price = 5000000000L + i; float rate = (i % 8) * 0.25f; bool enabled = (i & 1) == 0;
            int reward = count - i + 1; int[] bonus = { i, reward };
            rewards.Add(new JArray(i, coins, name, new JArray(tags), price, rate, enabled));
            tiles.Add(new JArray(i, reward, new JArray(bonus))); sparse.Add(new JArray(i * 7919 + 1, i));
            dto.Rewards[i - 1] = new Reward { Id = i, Coins = coins, Name = name, Tags = tags, Price = price, Rate = rate, Enabled = enabled };
            dto.Tiles[i - 1] = new Tile { Id = i, Reward = reward, Bonus = bonus }; dto.Sparse[i - 1] = new Sparse { Id = i * 7919 + 1, Value = i };
        }
        source["tables"][0]["rows"] = rewards; source["tables"][1]["rows"] = tiles; source["tables"][2]["rows"] = sparse;
        return (source.ToString(Formatting.None), JsonConvert.SerializeObject(dto));
    }
    static (string Source, string Dto, byte[] Binary, double CompileMs) Input(int count)
    {
#if UNITY_2022_3_OR_NEWER
        string prefix = "Bench" + count;
        return (Resources.Load<TextAsset>(prefix + "Source").text, Resources.Load<TextAsset>(prefix + "Dto").text,
            Resources.Load<TextAsset>(prefix + "Binary").bytes, -1);
#else
        var input = Generate(count); long start = Stopwatch.GetTimestamp(); var bytes = ConfigCompiler.Compile(input.Source);
        return (input.Source, input.Dto, bytes, ElapsedMs(start));
#endif
    }
    static string ParserName {
        get {
#if UNITY_2022_3_OR_NEWER
            return "Unity JsonUtility typed DTO; IL2CPP/Mono as reported by framework probe";
#else
            return "Newtonsoft.Json typed DTO; .NET tiered compilation disabled";
#endif
        }
    }
    struct AllocationCounter
    {
#if UNITY_2022_3_OR_NEWER
        ProfilerRecorder recorder;
        public static AllocationCounter Start() => new AllocationCounter { recorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", 1,
            ProfilerRecorderOptions.CollectOnlyOnCurrentThread | ProfilerRecorderOptions.SumAllSamplesInFrame) };
        public long Finish() {
            try { if (!recorder.Valid) throw new Exception("GC.Alloc unavailable"); recorder.Stop(); return recorder.Count == 0 ? 0 : recorder.GetSample(0).Count; }
            finally { recorder.Dispose(); }
        }
#else
        long before;
        public static AllocationCounter Start() => new AllocationCounter { before = GC.GetAllocatedBytesForCurrentThread() };
        public long Finish() => GC.GetAllocatedBytesForCurrentThread() - before;
#endif
    }
    static bool CounterMeasuresBytes {
        get {
#if UNITY_2022_3_OR_NEWER
            return false;
#else
            return true;
#endif
        }
    }
}
