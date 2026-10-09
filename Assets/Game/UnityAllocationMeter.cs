using System;
using Unity.Profiling;

namespace DiceTown
{
    public sealed class UnityAllocationMeter : IAllocationMeter
    {
        public bool Available { get; }
        public string Name => "Unity ProfilerRecorder / GC.Alloc event count / main thread; bytes inferred only for zero events";
        public long PositiveControlBytes { get; }
        public long EmptyControlBytes { get; }
        public long PositiveControlEvents { get; }
        public long EmptyControlEvents { get; }
        public UnityAllocationMeter()
        {
            Read(() => { }); Read(() => GC.KeepAlive(new byte[4096]));
            var empty = Read(() => { });
            var positive = Read(() => GC.KeepAlive(new byte[4096]));
            EmptyControlBytes = empty.Bytes; PositiveControlBytes = positive.Bytes;
            EmptyControlEvents = empty.Events; PositiveControlEvents = positive.Events;
            Available = empty.Events == 0 && positive.Events >= 1;
        }
        public AllocationSample Measure(Action action) => Available ? Read(action) : AllocationSample.Unavailable;
        static AllocationSample Read(Action action)
        {
            using (var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", 1,
                ProfilerRecorderOptions.CollectOnlyOnCurrentThread | ProfilerRecorderOptions.SumAllSamplesInFrame))
            {
                if (!recorder.Valid) return AllocationSample.Unavailable;
                int collections = GC.CollectionCount(0);
                action();
                recorder.Stop();
                var sample = recorder.Count == 0 ? default(ProfilerRecorderSample) : recorder.GetSample(0);
                // GC.Alloc is a timed marker. Value is nanoseconds, NOT bytes. Only zero events
                // proves zero allocated bytes; nonzero byte totals require a different profiler API.
                return new AllocationSample { Bytes = sample.Count == 0 ? 0 : -1, Events = sample.Count, Collections = GC.CollectionCount(0) - collections };
            }
        }
    }
}
