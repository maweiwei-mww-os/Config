using System;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

namespace DiceTown
{
    public static class AllocationDiagnostics
    {
        public static void Run()
        {
            for(int enabled=0;enabled<2;enabled++) {
                Profiler.enabled=enabled==1;
                foreach(var options in new[]{ProfilerRecorderOptions.CollectOnlyOnCurrentThread,
                    ProfilerRecorderOptions.CollectOnlyOnCurrentThread|ProfilerRecorderOptions.SumAllSamplesInFrame}) {
                    using(var r=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"GC.Alloc",100,options)) {
                        var bytes=new byte[4096];GC.KeepAlive(bytes);r.Stop();
                        Debug.Log("ALLOCATION DIAG enabled="+enabled+" options="+options+" valid="+r.Valid+" count="+r.Count+" current="+r.CurrentValue+" unit="+r.UnitType);
                        for(int i=0;i<r.Count;i++) Debug.Log("ALLOCATION SAMPLE value="+r.GetSample(i).Value+" count="+r.GetSample(i).Count);
                    }
                }
            }
        }
    }
}
