using System;
using System.IO;
using GameJson;
using FrameworkFixture;
using Unity.Profiling;
using UnityEngine;

public sealed class FrameworkProbe : MonoBehaviour
{
    [Serializable] public sealed class Report
    {
        public bool Passed;
        public bool SourceEquivalent;
        public string Unity, Backend;
        public int ReadIterations, ActiveVersion, CapturedVersion;
        public long PositiveControlEvents, EmptyControlEvents, ReadAllocationEvents, Checksum;
    }
    public static Report RunChecks()
    {
        byte[] initial=Resources.Load<TextAsset>("FixtureConfig").bytes;
        byte[] csv=Resources.Load<TextAsset>("FixtureConfigCsv").bytes;
        Require(initial.Length==csv.Length,"source byte length");
        for(int i=0;i<initial.Length;i++)Require(initial[i]==csv[i],"source byte equality");
        var csvConfig=new FixtureConfig(CompactConfig.Load(csv));
        Require(csvConfig.Tile.Get(5).RewardRecord.Coins==20&&csvConfig.Reward.Get(1).Price==5000000000L,"CSV generated access");
        byte[] update=Resources.Load<TextAsset>("FixtureConfigUpdate").bytes;
        var store=new ConfigStore<FixtureConfig>(d=>new FixtureConfig(d),ConfigVersionPolicy.Increasing);
        Require(store.TryPublish(CompactConfig.Load(initial),out _),"initial publication");
        var old=store.Capture();var oldRow=old.Value.Tile.Get(5);
        Require(store.TryPrepare(()=>CompactConfig.Load(update),out var candidate,out _),"prepare");
        Require(store.TryPrepare(()=>CompactConfig.Load(update),out var stale,out _),"prepare concurrent");
        Require(store.Current.Reward.Get(1).Coins==20,"prepare must not publish");
        Require(store.TryCommit(candidate,out _),"commit");
        Require(!store.TryCommit(stale,out var failure)&&failure.Code==ConfigFailureCode.StaleCandidate,"reject stale");
        Require(!store.TryCommit(candidate,out failure)&&failure.Code==ConfigFailureCode.AlreadyCommitted,"reject reused");
        Require(store.Capture().Version==2&&store.Current.Reward.Get(1).Coins==80&&oldRow.RewardRecord.Coins==20,"snapshot isolation");
        Require(!store.Current.Reward.TryGet(2,out var missing)&&!missing.IsValid,"missing id");
        Require(store.Current.Reward.Get(1).Price==5000000000L&&store.Current.Reward.Get(1).Rate==0.25f&&store.Current.Reward.Get(1).Enabled,"extended scalars");
        Probe(()=>{});Probe(()=>GC.KeepAlive(new byte[4096]));
        long empty=Probe(()=>{}),positive=Probe(()=>GC.KeepAlive(new byte[4096]));
        Require(empty==0&&positive>=1,"allocation detector calibration");
        Read(store,10000);long checksum=0;
        long events=Probe(()=>checksum=Read(store,200000));
        Require(events==0&&checksum==1000000046000000L,"generated reads/foreach/capture must be allocation-free");
        return new Report{Passed=true,SourceEquivalent=true,Unity=Application.unityVersion,Backend=BackendName,ReadIterations=200000,
            ActiveVersion=store.Capture().Version,CapturedVersion=old.Version,PositiveControlEvents=positive,
            EmptyControlEvents=empty,ReadAllocationEvents=events,Checksum=checksum};
    }
    static long Read(ConfigStore<FixtureConfig> store,int count)
    {
        long sum=0;
        for(int i=0;i<count;i++) {
            var c=store.Capture().Value;
            foreach(var row in c.Reward){sum+=row.Coins+row.Name.Length;sum+=row.Enabled?row.Price:0;sum+=(long)(row.Rate*4);foreach(int tag in row.Tags)sum+=tag;}
            sum+=c.Tile.Get(5).RewardRecord.Coins+c.Sparse.Get(1000000).Value;
        }
        return sum;
    }
    static long Probe(Action action)
    {
        using(var r=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"GC.Alloc",1,ProfilerRecorderOptions.CollectOnlyOnCurrentThread|ProfilerRecorderOptions.SumAllSamplesInFrame)) {
            if(!r.Valid)return -1;
            action();r.Stop();return r.Count==0?0:r.GetSample(0).Count;
        }
    }
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static string BackendName {
        get {
#if ENABLE_IL2CPP
            return "IL2CPP";
#else
            return "Mono";
#endif
        }
    }
    void Start()
    {
        try {
            var report=RunChecks();string path=Path.Combine(Application.persistentDataPath,"framework-result.json");
            var args=Environment.GetCommandLineArgs();for(int i=0;i<args.Length-1;i++)if(args[i]=="--result")path=args[i+1];
            Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(report,true));
            for(int i=0;i<args.Length-1;i++)if(args[i]=="--benchmark") {
                if(BenchmarkProgram.RunAll(new[]{args[i+1]})!=0)throw new Exception("Framework benchmark failed");
            }
            Debug.Log("FRAMEWORK PLAYER PASSED "+BackendName);Application.Quit(0);
        } catch(Exception e){Debug.LogException(e);Application.Quit(1);}
    }
}
