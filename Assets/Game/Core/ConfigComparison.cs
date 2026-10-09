using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using GameJson;

namespace DiceTown
{
    public enum QueryKind { Find, Integer, String, List, Mixed }
    [Serializable] public sealed class AccessMeasurement
    {
        public QueryKind Kind;
        public double Milliseconds;
        public long Checksum;
    }
    public struct AllocationSample
    {
        public long Bytes, Events;
        public int Collections;
        public static AllocationSample Unavailable => new AllocationSample { Bytes = -1, Events = -1, Collections = -1 };
    }
    public interface IAllocationMeter
    {
        bool Available { get; }
        string Name { get; }
        long PositiveControlBytes { get; }
        long EmptyControlBytes { get; }
        long PositiveControlEvents { get; }
        long EmptyControlEvents { get; }
        AllocationSample Measure(Action action);
    }
    [Serializable] public sealed class BackendMeasurement
    {
        public string Backend;
        public long RetainedBytes, LoadAllocatedBytes, LoadAllocationEvents, QueryAllocatedBytes, QueryAllocationEvents, Checksum;
        public double LoadMilliseconds, QueryMilliseconds;
        public int Queries, Rows, TotalRows, QueryCollections;
        public AccessMeasurement[] Access;
        public double NanosecondsPerQuery => Queries > 0 ? QueryMilliseconds * 1000000 / Queries : 0;
    }
    [Serializable] public sealed class ComparisonResult
    {
        public string Runtime, AllocationMethod, EnvironmentLabel, Processor;
        public BackendMeasurement ObjectJson, Compact;
        public int JsonBytes, CompactBytes;
        public bool SameResults;
        public bool AllocationCounterAvailable;
        public long PositiveControlBytes, EmptyControlBytes;
        public long PositiveControlEvents, EmptyControlEvents;
        public double QuerySpeedup => Compact.QueryMilliseconds > 0 ? ObjectJson.QueryMilliseconds / Compact.QueryMilliseconds : 0;
        public double MemorySavingPercent => ObjectJson.RetainedBytes > 0 && Compact.RetainedBytes >= 0
            ? 100.0 * (ObjectJson.RetainedBytes - Compact.RetainedBytes) / ObjectJson.RetainedBytes : double.NaN;
        public bool ZeroAllocationVerified => AllocationCounterAvailable && Compact.QueryAllocatedBytes == 0;
    }
    public static class ConfigComparison
    {
        public const int QueryCount = 200000;
        public static ComparisonResult Run(string json, byte[] bytes, IAllocationMeter meter = null)
        {
            meter = meter ?? new ManagedAllocationMeter();
            Warm(json, bytes);
            var result = new ComparisonResult {
                Runtime = Environment.Version.ToString(), JsonBytes = System.Text.Encoding.UTF8.GetByteCount(json), CompactBytes = bytes.Length,
                AllocationCounterAvailable = meter.Available, AllocationMethod = meter.Name,
                PositiveControlBytes = meter.PositiveControlBytes, EmptyControlBytes = meter.EmptyControlBytes,
                PositiveControlEvents = meter.PositiveControlEvents, EmptyControlEvents = meter.EmptyControlEvents
            };
            Func<IConfigData> objectLoader = () => ObjectConfig.Load(json), compactLoader = () => CompactConfig.Load(bytes);
            result.ObjectJson = Measure(objectLoader, "Object JSON", out var objectData);
            result.Compact = Measure(compactLoader, "Game-Json", out var compactData);
            RecordAllocations(result.ObjectJson, objectLoader, objectData, meter);
            RecordAllocations(result.Compact, compactLoader, compactData, meter);
            result.SameResults = result.ObjectJson.Rows == result.Compact.Rows;
            for (int i = 0; i < result.ObjectJson.Access.Length; i++)
                result.SameResults &= result.ObjectJson.Access[i].Checksum == result.Compact.Access[i].Checksum;
            // Keep both graphs rooted: Mono's conservative stack scanning can otherwise retain the
            // previous graph until the middle of the next measurement, producing a negative delta.
            GC.KeepAlive(objectData); GC.KeepAlive(compactData);
            return result;
        }
        [MethodImpl(MethodImplOptions.NoInlining)] static void Warm(string json, byte[] bytes)
        {
            var a = ObjectConfig.Load(json); var b = CompactConfig.Load(bytes);
            for (int i = 0; i < 5; i++) { Query(a.Table("Reward"), 1000, (QueryKind)i); Query(b.Table("Reward"), 1000, (QueryKind)i); }
        }
        [MethodImpl(MethodImplOptions.NoInlining)] static BackendMeasurement Measure(Func<IConfigData> load, string name, out IConfigData data)
        {
            // Measurement infrastructure and input buffers are excluded from the retained graph.
            var timer = new Stopwatch();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            long before = GC.GetTotalMemory(true);
            timer.Start(); data = load(); timer.Stop();
            double loadMilliseconds = timer.Elapsed.TotalMilliseconds;
            long retained = GC.GetTotalMemory(true) - before;
            var table = data.Table("Reward");
            var access = new AccessMeasurement[5];
            var durations = new double[5];
            for (int kind = 0; kind < access.Length; kind++)
            {
                Query(table, 1000, (QueryKind)kind);
                long checksum = 0;
                for (int run = 0; run < durations.Length; run++) {
                    timer.Restart(); checksum = Query(table, QueryCount, (QueryKind)kind); timer.Stop();
                    durations[run] = timer.Elapsed.TotalMilliseconds;
                }
                Array.Sort(durations);
                access[kind] = new AccessMeasurement { Kind = (QueryKind)kind, Milliseconds = durations[2], Checksum = checksum };
            }
            GC.KeepAlive(data);
            var mixed = access[(int)QueryKind.Mixed];
            return new BackendMeasurement { Backend = name, RetainedBytes = retained, LoadAllocatedBytes = -1,
                LoadMilliseconds = loadMilliseconds, QueryMilliseconds = mixed.Milliseconds, QueryAllocatedBytes = -1,
                QueryAllocationEvents = -1, QueryCollections = -1,
                Checksum = mixed.Checksum, Queries = QueryCount, Rows = table.Count, Access = access,
                TotalRows = data.Table("Tile").Count + table.Count + data.Table("Building").Count };
        }
        static void RecordAllocations(BackendMeasurement result, Func<IConfigData> load, IConfigData data, IAllocationMeter meter)
        {
            var table = data.Table("Reward");
            var loadSample = meter.Measure(() => GC.KeepAlive(load()));
            var querySample = meter.Measure(() => Query(table, QueryCount));
            result.LoadAllocatedBytes = loadSample.Bytes;
            result.LoadAllocationEvents = loadSample.Events;
            result.QueryAllocatedBytes = querySample.Bytes;
            result.QueryAllocationEvents = querySample.Events;
            result.QueryCollections = querySample.Collections;
        }
        public static long Query(IConfigTable table, int count, QueryKind kind = QueryKind.Mixed)
        {
            int coins = table.Bind("coins", FieldKind.Int), tags = table.Bind("tags", FieldKind.IntList), district = table.Bind("district", FieldKind.String);
            uint seed = 42; long sum = 0;
            // Scalar/string/list workloads include lookup, with identical IDs in both backends.
            switch (kind)
            {
                case QueryKind.Find:
                    for (int i = 0; i < count; i++) sum += table.Find(NextId(ref seed, table.Count));
                    break;
                case QueryKind.Integer:
                    for (int i = 0; i < count; i++) sum += table.Int(table.Find(NextId(ref seed, table.Count)), coins);
                    break;
                case QueryKind.String:
                    for (int i = 0; i < count; i++) sum += table.Text(table.Find(NextId(ref seed, table.Count)), district).Length;
                    break;
                case QueryKind.List:
                    for (int i = 0; i < count; i++) {
                        var list = table.List(table.Find(NextId(ref seed, table.Count)), tags);
                        for (int j = 0; j < list.Count; j++) sum += list[j];
                    }
                    break;
                default:
                    for (int i = 0; i < count; i++) {
                        int row = table.Find(NextId(ref seed, table.Count));
                        sum += table.Int(row, coins) + table.Text(row, district).Length;
                        var list = table.List(row, tags); for (int j = 0; j < list.Count; j++) sum += list[j];
                    }
                    break;
            }
            return sum;
        }
        static int NextId(ref uint seed, int count)
        { seed = unchecked(seed * 1664525u + 1013904223u); return (int)(seed % count) + 1; }
    }
    public sealed class ManagedAllocationMeter : IAllocationMeter
    {
        public bool Available { get; }
        public string Name => "GC.GetAllocatedBytesForCurrentThread";
        public long PositiveControlBytes { get; }
        public long EmptyControlBytes { get; }
        public long PositiveControlEvents => -1;
        public long EmptyControlEvents => -1;
        public ManagedAllocationMeter()
        {
            Read(() => { }); Read(() => GC.KeepAlive(new byte[4096]));
            EmptyControlBytes = Read(() => { }).Bytes;
            PositiveControlBytes = Read(() => GC.KeepAlive(new byte[4096])).Bytes;
            Available = EmptyControlBytes == 0 && PositiveControlBytes >= 4096;
        }
        public AllocationSample Measure(Action action) => Available ? Read(action) : AllocationSample.Unavailable;
        static AllocationSample Read(Action action)
        {
            int collections = GC.CollectionCount(0);
            long before = GC.GetAllocatedBytesForCurrentThread();
            action();
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            return new AllocationSample { Bytes = bytes, Events = -1, Collections = GC.CollectionCount(0) - collections };
        }
    }
}
