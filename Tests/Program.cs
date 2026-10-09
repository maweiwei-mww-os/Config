using System;
using System.IO;
using System.Linq;
using GameJson;
using DiceTown;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class Program
{
    static int passed, failed;
    static string json;
    static byte[] binary;
    static TownRules rules;
    static int Main(string[] args)
    {
        json = TownContent.Create(1); binary = ConfigCompiler.Compile(json); rules = new TownRules(CompactConfig.Load(binary));
        Test("deterministic compilation", () => Check(binary.SequenceEqual(ConfigCompiler.Compile(json))));
        Test("all game values match both backends", () => {
            var a = new TownRules(ObjectConfig.Load(json));
            for (int town = 0; town < 120; town++) {
                for (int tile = 0; tile < 24; tile++) { var x = a.Reward(town,tile); var y = rules.Reward(town,tile); Check(x.Coins==y.Coins && x.Dice==y.Dice && x.Kind==y.Kind && x.Name==y.Name); }
                for(int b=0;b<3;b++) for(int l=1;l<=5;l++) Check(a.Cost(town,b,l)==rules.Cost(town,b,l));
            }
        });
        Test("reject corrupt payload", () => { var b=(byte[])binary.Clone(); b[b.Length-1]^=1; Throws(()=>CompactConfig.Load(b)); });
        Test("reject truncated binary", () => Throws(()=>CompactConfig.Load(binary.Take(60).ToArray())));
        Test("reject duplicate id", () => MutateReject(root => { var rows=(JArray)root["tables"][0]["rows"]; rows.Add(rows[0].DeepClone()); }));
        Test("reject broken cross-table reference", () => MutateReject(root => root["tables"][0]["rows"][0][3] = 999999));
        Test("reject wrong field type", () => MutateReject(root => root["tables"][1]["rows"][0][1] = "bad"));
        Test("reject integer overflow", () => MutateReject(root => root["tables"][1]["rows"][0][1] = (long)int.MaxValue+1));
        Test("reject malformed row width", () => MutateReject(root => ((JArray)root["tables"][0]["rows"][0]).RemoveAt(0)));
        Test("dense missing key and typed binding", () => {var t=CompactConfig.Load(binary).Table("Reward"); Check(t.Find(-1)==-1&&t.Find(int.MaxValue)==-1); Throws(()=>t.Bind("coins",FieldKind.String));});
        Test("sparse lookup and negative scalar", () => {
            string s="{\"version\":1,\"tables\":[{\"name\":\"T\",\"fields\":[{\"name\":\"id\",\"type\":\"int\"},{\"name\":\"v\",\"type\":\"int\"}],\"rows\":[[1000000,-2],[1,3]]}]}";
            var t=CompactConfig.Load(ConfigCompiler.Compile(s)).Table("T"); Check(t.Find(2)==-1&&t.Int(t.Find(1000000),1)==-2);
        });
        Test("roll reserves once and settles once", () => { var g=new GameSession(); var p=g.BeginRoll(rules,3,3); Check(g.State.Dice==27&&g.State.Coins==180&&p.Destination==6); Check(g.BeginRoll(rules,1,1)==null); Check(g.CompleteRoll(p)&&!g.CompleteRoll(p)); Check(g.State.Coins==350); });
        Test("invalid dice cannot mutate state", () => {var g=new GameSession(); Throws(()=>g.BeginRoll(rules,0,1)); Check(g.State.Dice==28);});
        Test("activity update preserves in-flight reward", () => {
            var store=new ConfigStore<TownRules>(d=>new TownRules(d)); Check(store.TryPublish(CompactConfig.Load(binary),out _));
            var g=new GameSession(); var p=g.BeginRoll(store.Current,3,3); Check(store.TryPublish(CompactConfig.Load(ConfigCompiler.Compile(TownContent.Create(2))),out _));
            Check(p.Version==1&&p.Coins==170); g.CompleteRoll(p); var next=g.BeginRoll(store.Current,6,6); Check(next.Version==2&&next.Coins==340);
        });
        Test("invalid candidate retains active snapshot", () => {var store=new ConfigStore<TownRules>(d=>new TownRules(d)); store.TryPublish(CompactConfig.Load(binary),out _); var old=store.Current; Check(!store.TryPublish(null,out _)&&ReferenceEquals(old,store.Current));});
        Test("save recovers pending settlement exactly once", () => {
            var g=new GameSession(); g.BeginRoll(rules,3,3);
            var save=JsonConvert.DeserializeObject<TownSave>(JsonConvert.SerializeObject(g.State)); var recovered=new GameSession(save);
            Check(recovered.State.Coins==350&&recovered.State.Position==6&&recovered.State.Turns==1&&recovered.State.Pending==null);
            var again=new GameSession(JsonConvert.DeserializeObject<TownSave>(JsonConvert.SerializeObject(recovered.State))); Check(again.State.Coins==350);
        });
        Test("upgrade debits exact cost", () => {var g=new GameSession(); Check(g.Upgrade(rules,0)); Check(g.State.Coins==70&&g.State.Levels[0]==1); Check(!g.Upgrade(rules,0)&&g.State.Coins==70);});
        Test("no upgrade during turn", () => {var g=new GameSession();g.BeginRoll(rules,1,1);Check(!g.Upgrade(rules,0));});
        Test("completed town advances and resets buildings", () => {var g=new GameSession(new TownSave{Levels=new[]{5,5,5}}); Check(g.NextTown()&&g.State.Town==1&&g.State.Dice==56&&g.State.Levels.Sum()==0);});
        Test("incomplete town cannot advance", () => Check(!new GameSession().NextTown()));
        Test("zero dice can recover without purchase", () => {var g=new GameSession(new TownSave{Dice=0,Coins=0}); Check(g.BeginRoll(rules,1,1)==null&&g.Supply()&&g.State.Dice==12&&!g.Supply());});
        Test("shield blocks chance penalty", () => {var g=new GameSession(new TownSave{Position=9,Shields=1});var p=g.BeginRoll(rules,1,1);g.CompleteRoll(p);Check(g.State.Coins==180&&g.State.Shields==0);});
        Test("independent backend produces same 100-turn game", () => {
            var a=new GameSession();var b=new GameSession();var other=new TownRules(ObjectConfig.Load(json));
            for(int i=0;i<100;i++){if(a.State.Dice==0){a.Supply();b.Supply();} a.CompleteRoll(a.BeginRoll(rules,i%6+1,(i*3)%6+1));b.CompleteRoll(b.BeginRoll(other,i%6+1,(i*3)%6+1));a.Upgrade(rules,i%3);b.Upgrade(other,i%3);}
            Check(JsonConvert.SerializeObject(a.State)==JsonConvert.SerializeObject(b.State));
        });
        Test("warmed query loops allocate zero on both backends", () => {foreach(var d in new IConfigData[]{CompactConfig.Load(binary),ObjectConfig.Load(json)}) {var t=d.Table("Reward");ConfigComparison.Query(t,10000);long before=GC.GetAllocatedBytesForCurrentThread();long sum=ConfigComparison.Query(t,100000);long delta=GC.GetAllocatedBytesForCurrentThread()-before;Check(sum!=0&&delta==0,"allocated="+delta);}});
        Test("repeated snapshot replacement releases old managed graph", () => {var weak=RetiredSnapshot();GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();Check(!weak.IsAlive);});
        Test("performance ratios use measured scope and reject unavailable values", () => {
            var r=new ComparisonResult{ObjectJson=new BackendMeasurement{QueryMilliseconds=40,RetainedBytes=1000},Compact=new BackendMeasurement{QueryMilliseconds=10,RetainedBytes=250,Queries=200000}};
            Check(r.QuerySpeedup==4 && r.MemorySavingPercent==75 && r.Compact.NanosecondsPerQuery==50);
            r.Compact.QueryMilliseconds=0;Check(r.QuerySpeedup==0);
            r.ObjectJson.RetainedBytes=-1;Check(double.IsNaN(r.MemorySavingPercent));
        });
        Test("unavailable detector cannot certify zero allocation", () => {
            var r=new ComparisonResult{Compact=new BackendMeasurement{QueryAllocatedBytes=0}};
            Check(!r.ZeroAllocationVerified);r.AllocationCounterAvailable=true;Check(r.ZeroAllocationVerified);
            r.Compact.QueryAllocatedBytes=24;Check(!r.ZeroAllocationVerified);
            r.Compact.QueryAllocatedBytes=-1;Check(!r.ZeroAllocationVerified);
        });
        Test("all access workloads match and allocate zero after warmup", () => {
            var a=ObjectConfig.Load(json).Table("Reward");var b=CompactConfig.Load(binary).Table("Reward");
            foreach(QueryKind kind in Enum.GetValues(typeof(QueryKind))) {
                long x=ConfigComparison.Query(a,10000,kind),y=ConfigComparison.Query(b,10000,kind);Check(x==y);
                long before=GC.GetAllocatedBytesForCurrentThread();ConfigComparison.Query(b,200000,kind);
                long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
                Check(allocated==0,kind+" allocated="+allocated);
            }
        });
        if(args.Contains("--evidence")){
            var report=ConfigComparison.Run(json,binary);Directory.CreateDirectory("Artifacts");File.WriteAllText("Artifacts/core-comparison.json",JsonConvert.SerializeObject(report,Formatting.Indented));Console.WriteLine(JsonConvert.SerializeObject(report,Formatting.Indented));
        }
        Console.WriteLine($"RESULT {passed} passed / {failed} failed"); return failed==0?0:1;
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] static WeakReference RetiredSnapshot(){var store=new ConfigStore<TownRules>(d=>new TownRules(d));store.TryPublish(CompactConfig.Load(binary),out _);var weak=new WeakReference(store.Current);store.TryPublish(CompactConfig.Load(binary),out _);GC.KeepAlive(store);return weak;}
    static void MutateReject(Action<JObject> mutate){var root=JObject.Parse(json);mutate(root);Throws(()=>ConfigCompiler.Compile(root.ToString()));}
    static void Throws(Action a){try{a();}catch{return;}throw new Exception("Expected rejection.");}
    static void Check(bool condition,string message="Assertion failed"){if(!condition)throw new Exception(message);}
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL "+name+": "+e);}}
}
