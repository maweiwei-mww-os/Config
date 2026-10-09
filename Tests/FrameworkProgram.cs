using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GameJson;
using Newtonsoft.Json.Linq;
using FrameworkFixture;

internal static class FrameworkProgram
{
    static int passed, failed;
    static string source;
    static byte[] bytes;
    static int Main()
    {
        source=File.ReadAllText("Tests/Fixtures/framework.json");bytes=ConfigCompiler.Compile(source);
        Test("generated source is deterministic",()=>Check(ConfigCodeGenerator.Generate(source,"FrameworkFixture","FixtureConfig")==ConfigCodeGenerator.Generate(source,"FrameworkFixture","FixtureConfig")));
        Test("generator rejects invalid identifiers and symbol collisions",()=>{
            Throws(()=>ConfigCodeGenerator.Generate(source,"Bad-Namespace","FixtureConfig"));
            Throws(()=>ConfigCodeGenerator.Generate(source,"N","Version"));
            var doc=JObject.Parse(source);doc["tables"][0]["fields"][1]["name"]="Id";
            Throws(()=>ConfigCodeGenerator.Generate(doc.ToString(),"N","C"));
        });
        Test("generated columns and scalar/list references",()=>{
            var c=Load();Check(c.Reward.Get(1).Coins==20&&c.Tile.Get(5).RewardRecord.Coins==20);
            Check(c.Tile.Get(5).ResolveBonus(1).Coins==40&&c.Sparse.Get(1000000).Value==8);
            Check(c.Reward.Get(1).Name==c.Reward.Get(3).Name);
        });
        Test("generated long, float and bit-packed bool columns",()=>{
            var c=Load();Check(c.Reward.Get(1).Price==5000000000L&&c.Reward.Get(1).Rate==0.25f&&c.Reward.Get(1).Enabled);
            Check(!c.Reward.Get(3).Enabled);var other=new FixtureConfig(ObjectConfig.Load(source));
            Check(other.Reward.Get(1).Price==c.Reward.Get(1).Price&&other.Reward.Get(1).Rate==c.Reward.Get(1).Rate&&other.Reward.Get(1).Enabled);
        });
        Test("reject non-finite float, numeric bool and scalar references",()=>{
            var doc=JObject.Parse(source);doc["tables"][0]["rows"][0][5]=double.PositiveInfinity;Throws(()=>ConfigCompiler.Compile(doc.ToString()));
            doc=JObject.Parse(source);doc["tables"][0]["rows"][0][6]=1;Throws(()=>ConfigCompiler.Compile(doc.ToString()));
            doc=JObject.Parse(source);doc["tables"][0]["fields"][4]["ref"]="Reward";Throws(()=>ConfigCompiler.Compile(doc.ToString()));
        });
        Test("scalar boundaries, boolean byte boundaries and empty columns",()=>{
            var doc=JObject.Parse(source);var rows=(JArray)doc["tables"][0]["rows"];rows.Clear();
            for(int i=0;i<17;i++)rows.Add(new JArray(i,i,"value",new JArray(),i==0?long.MinValue:long.MaxValue,i%2==0?-0.5f:float.MaxValue,i%3==0));
            var c=new FixtureConfig(CompactConfig.Load(ConfigCompiler.Compile(doc.ToString())));
            for(int i=0;i<17;i++)Check(c.Reward.Get(i).Enabled==(i%3==0));
            Check(c.Reward.Get(0).Price==long.MinValue&&c.Reward.Get(16).Price==long.MaxValue&&c.Reward.Get(1).Rate==float.MaxValue);
            foreach(var table in doc["tables"])((JArray)table["rows"]).Clear();
            Check(new FixtureConfig(CompactConfig.Load(ConfigCompiler.Compile(doc.ToString()))).Reward.Count==0);
            doc=JObject.Parse(source);doc["tables"][0]["rows"][0][4]=JToken.Parse("9223372036854775808");Throws(()=>ConfigCompiler.Compile(doc.ToString()));
        });
        Test("binary scalar validation rejects padding and nonfinite values",()=>{
            Throws(()=>CompactConfig.Load(ScalarPacket(FieldKind.Bool,w=>w.Write((byte)0x80))));
            Throws(()=>CompactConfig.Load(ScalarPacket(FieldKind.Float,w=>w.Write(float.NaN))));
            Throws(()=>CompactConfig.Load(ScalarPacket(FieldKind.Float,w=>w.Write(float.PositiveInfinity))));
            Throws(()=>CompactConfig.Load(ScalarPacket(FieldKind.Long,w=>w.Write(42))));
        });
        Test("missing IDs are explicit, default rows cannot be read",()=>{
            var c=Load();Check(!c.Reward.TryGet(2,out var missing)&&!missing.IsValid);
            Throws(()=>{var n=missing.Coins;});Throws(()=>c.Reward.Get(2));Throws(()=>c.Reward.At(-1));
        });
        Test("index policies agree across contiguous, dense gaps and sparse IDs",()=>{
            foreach(var ids in new[]{new[]{4,5,6},new[]{0,2,3},new[]{0,1000000,int.MaxValue}}) {
                var rows=new JArray();foreach(int id in ids)rows.Add(new JArray(id));
                string json=new JObject{["version"]=1,["tables"]=new JArray(new JObject{["name"]="A",["fields"]=new JArray(new JObject{["name"]="id",["type"]="int"}),["rows"]=rows})}.ToString();
                byte[] packet=ConfigCompiler.Compile(json);
                foreach(ConfigIndexPolicy policy in Enum.GetValues(typeof(ConfigIndexPolicy))) {
                    var table=CompactConfig.Load(packet,policy).Table("A");for(int i=0;i<ids.Length;i++)Check(table.Find(ids[i])==i);
                    Check(table.Find(-1)==-1&&table.Find(int.MinValue)==-1&&table.Find(7)==-1);
                }
            }
            Throws(()=>CompactConfig.Load(bytes,(ConfigIndexPolicy)999));
        });
        Test("generated row and list foreach does not allocate",()=>{
            var c=Load();Read(c,10000);long before=GC.GetAllocatedBytesForCurrentThread();long total=Read(c,200000);
            long allocated=GC.GetAllocatedBytesForCurrentThread()-before;Check(total>0&&allocated==0,"bytes="+allocated);
        });
        Test("generated primitive columns expose allocation-free read-only spans",()=>{
            var c=Load();Check(c.Reward.CoinsColumn.TryGetSpan(out var coins)&&coins.Length==2&&coins[1]==40);
            Check(c.Reward.PriceColumn.TryGetSpan(out var prices)&&prices[0]==5000000000L);
            Check(c.Reward.RateColumn.TryGetSpan(out var rates)&&rates[0]==0.25f);
            var fallback=new FixtureConfig(ObjectConfig.Load(source));Check(!fallback.Reward.CoinsColumn.TryGetSpan(out var absent)&&absent.IsEmpty);
            long before=GC.GetAllocatedBytesForCurrentThread();long sum=0;
            for(int i=0;i<200000;i++)foreach(int value in coins)sum+=value;
            Check(GC.GetAllocatedBytesForCurrentThread()-before==0&&sum==12000000);
        });
        Test("generated access also supports reference backend",()=>{
            var c=new FixtureConfig(ObjectConfig.Load(source));Check(c.Tile.Get(5).RewardRecord.Coins==20&&Read(c,10)==Read(Load(),10));
        });
        Test("additive fields and column reorder remain compatible",()=>{
            var doc=JObject.Parse(source);var t=doc["tables"][0];var fields=(JArray)t["fields"];
            fields.Add(JObject.Parse("{\"name\":\"extra\",\"type\":\"int\"}"));
            foreach(JArray row in t["rows"])row.Add(99);
            var f=fields[1];fields.RemoveAt(1);fields.Add(f);
            foreach(JArray row in t["rows"]){var v=row[1];row.RemoveAt(1);row.Add(v);}
            Check(new FixtureConfig(CompactConfig.Load(ConfigCompiler.Compile(doc.ToString()))).Reward.Get(1).Coins==20);
        });
        Test("deleted or changed required fields fail during binding",()=>{
            var doc=JObject.Parse(source);doc["tables"][0]["fields"][1]["name"]="other";
            Throws(()=>new FixtureConfig(CompactConfig.Load(ConfigCompiler.Compile(doc.ToString()))));
            doc=JObject.Parse(source);doc["tables"][0]["fields"][1]["type"]="string";
            foreach(JArray row in doc["tables"][0]["rows"])row[1]="20";
            Throws(()=>new FixtureConfig(CompactConfig.Load(ConfigCompiler.Compile(doc.ToString()))));
        });
        Test("generated contract checks references even when source drops ref metadata",()=>{
            var doc=JObject.Parse(source);((JObject)doc["tables"][1]["fields"][1]).Remove("ref");doc["tables"][1]["rows"][0][1]=999;
            Throws(()=>new FixtureConfig(CompactConfig.Load(ConfigCompiler.Compile(doc.ToString()))));
        });
        Test("reference retargeting is a breaking change even with matching IDs",()=>{
            var doc=JObject.Parse(source);doc["tables"][1]["fields"][1]["ref"]="Sparse";
            Throws(()=>new FixtureConfig(CompactConfig.Load(ConfigCompiler.Compile(doc.ToString()))));
        });
        Test("legacy format remains readable by dynamic API",()=>{
            var data=CompactConfig.Load(Packet(false,null));Check(!data.HasReferenceMetadata&&data.Table("A").Find(1)==0);
        });
        Test("binary loader rejects unresolved references without a generated facade",()=>Throws(()=>CompactConfig.Load(Packet(true,"Missing"))));
        Test("prepare is invisible until commit and old rows retain their snapshot",()=>{
            var s=Store();Check(s.TryPublish(CompactConfig.Load(bytes),out _));var old=s.Capture();var row=old.Value.Reward.Get(1);
            Check(s.TryPrepare(()=>Data(2,80),out var ticket,out _));Check(s.Current.Reward.Get(1).Coins==20);
            Check(s.TryCommit(ticket,out _)&&s.Capture().Version==2&&s.Current.Reward.Get(1).Coins==80);
            Check(row.Coins==20&&old.Value.Tile.Get(5).RewardRecord.Coins==20);
        });
        Test("failed parsing or binding does not publish",()=>{
            var s=Store();s.TryPublish(CompactConfig.Load(bytes),out _);var old=s.Capture();
            Check(!s.TryPrepare(()=>CompactConfig.Load(new byte[60]),out _,out var error)&&error.Code==ConfigFailureCode.PreparationFailed);
            Check(ReferenceEquals(old,s.Capture()));
            Check(!s.TryPrepare(()=>null,out _,out _)&&ReferenceEquals(old,s.Capture()));
        });
        Test("stale, foreign, reused and downgraded candidates are rejected",()=>{
            var s=Store();s.TryPublish(Data(1,20),out _);
            s.TryPrepare(()=>Data(2,30),out var a,out _);s.TryPrepare(()=>Data(3,40),out var b,out _);
            Check(s.TryCommit(a,out _));Check(!s.TryCommit(a,out var reused)&&reused.Code==ConfigFailureCode.AlreadyCommitted);
            Check(!s.TryCommit(b,out var stale)&&stale.Code==ConfigFailureCode.StaleCandidate);
            var other=Store();Check(!other.TryCommit(b,out var foreign)&&foreign.Code==ConfigFailureCode.WrongStore);
            Check(!s.TryPrepare(()=>Data(1,10),out _,out var old)&&old.Code==ConfigFailureCode.VersionRejected);
        });
        Test("discarded candidates cannot publish and release their graph",()=>{
            var result=DiscardCandidate();GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            Check(!result.Item3.IsAlive);Check(!result.Item1.TryCommit(result.Item2,out var failure)&&failure.Code==ConfigFailureCode.Discarded);
            Check(result.Item1.Current.Version==1);result.Item2.Dispose();GC.KeepAlive(result);
        });
        Test("commit and discard are serialized",()=>{
            for(int i=0;i<100;i++) {
                var s=Store();s.TryPublish(Data(1,20),out _);s.TryPrepare(()=>Data(2,80),out var candidate,out _);
                bool committed=false;Parallel.Invoke(()=>committed=s.TryCommit(candidate,out _),()=>candidate.Dispose());
                Check(s.Current.Version==(committed?2:1));
            }
        });
        Test("concurrent candidates have exactly one winner",()=>{
            var s=Store();s.TryPublish(Data(1,20),out _);
            s.TryPrepare(()=>Data(2,30),out var a,out _);s.TryPrepare(()=>Data(3,40),out var b,out _);
            int wins=0;Parallel.Invoke(()=>{if(s.TryCommit(a,out _))Interlocked.Increment(ref wins);},()=>{if(s.TryCommit(b,out _))Interlocked.Increment(ref wins);});
            Check(wins==1&&s.Capture().Generation==2);
        });
        Test("preparation detects a publication during loading",()=>{
            var s=Store();s.TryPublish(Data(1,20),out _);
            Check(s.TryPrepare(()=>{Check(s.TryPublish(Data(3,90),out _));return Data(2,80);},out var candidate,out _));
            Check(!s.TryCommit(candidate,out var failure)&&failure.Code==ConfigFailureCode.StaleCandidate);
            candidate.Dispose();Check(s.Current.Version==3&&s.Current.Reward.Get(1).Coins==90);
        });
        Test("retaining committed ticket does not retain retired candidate",()=>{
            var result=RetainTicket();GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();Check(!result.Item3.IsAlive);
            Check(!result.Item1.TryCommit(result.Item2,out var failure)&&failure.Code==ConfigFailureCode.AlreadyCommitted);GC.KeepAlive(result);
        });
        Test("snapshot reads are allocation-free",()=>{
            var s=Store();s.TryPublish(Data(1,20),out _);ReadStore(s,10000);
            long before=GC.GetAllocatedBytesForCurrentThread();long sum=ReadStore(s,200000);long delta=GC.GetAllocatedBytesForCurrentThread()-before;
            Check(sum==4000000&&delta==0,"bytes="+delta);
        });
        Test("concurrent readers never mix snapshot versions",()=>{
            var s=Store();s.TryPublish(Data(1,10),out _);int stop=0;long reads=0;
            using var ready=new CountdownEvent(4);var readers=new Task[4];
            for(int i=0;i<readers.Length;i++)readers[i]=Task.Run(()=>{ready.Signal();long local=0;while(Volatile.Read(ref stop)==0){var snap=s.Capture();Check(snap.Version==snap.Value.Version);Check(snap.Value.Tile.Get(5).RewardRecord.Coins==snap.Version*10);local++;}Interlocked.Add(ref reads,local);});
            ready.Wait();
            try {for(int v=2;v<=100;v++)Check(s.TryPublish(Data(v,v*10),out _));}
            finally {Volatile.Write(ref stop,1);Task.WaitAll(readers);}
            Check(reads>0,"Readers did not execute");
        });
        Test("retired snapshots are reclaimable",()=>{var weak=Retire();GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();Check(!weak.IsAlive);});
        Test("1000 updates preserve held snapshot and release retired graphs",()=>{
            var result=StressUpdates();GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            foreach(var weak in result.Item2)Check(!weak.IsAlive,"retired graph retained");
            Check(result.Item1.Current.Version==1001&&result.Item1.Current.Reward.Get(1).Coins==10010);GC.KeepAlive(result.Item1);
        });
        Console.WriteLine("FRAMEWORK RESULT "+passed+" passed / "+failed+" failed");return failed==0?0:1;
    }
    static FixtureConfig Load()=>new FixtureConfig(CompactConfig.Load(bytes));
    static ConfigStore<FixtureConfig> Store()=>new ConfigStore<FixtureConfig>(d=>new FixtureConfig(d),ConfigVersionPolicy.Increasing);
    static IConfigData Data(int version,int coins){var doc=JObject.Parse(source);doc["version"]=version;doc["tables"][0]["rows"][0][1]=coins;return CompactConfig.Load(ConfigCompiler.Compile(doc.ToString()));}
    static long Read(FixtureConfig c,int n){long sum=0;for(int i=0;i<n;i++){foreach(var r in c.Reward){sum+=r.Coins+r.Name.Length;sum+=r.Enabled?r.Price:0;sum+=(long)(r.Rate*4);foreach(int tag in r.Tags)sum+=tag;}sum+=c.Tile.Get(5).RewardRecord.Coins;}return sum;}
    static long ReadStore(ConfigStore<FixtureConfig> s,int n){long sum=0;for(int i=0;i<n;i++)sum+=s.Capture().Value.Reward.Get(1).Coins;return sum;}
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] static WeakReference Retire(){var s=Store();s.TryPublish(Data(1,20),out _);var old=new WeakReference(s.Capture().Value);s.TryPublish(Data(2,30),out _);GC.KeepAlive(s);return old;}
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] static Tuple<ConfigStore<FixtureConfig>,PreparedConfig<FixtureConfig>,WeakReference> DiscardCandidate()
    {
        WeakReference weak=null;var s=new ConfigStore<FixtureConfig>(d=>{var c=new FixtureConfig(d);if(d.Version==2)weak=new WeakReference(c);return c;});
        s.TryPublish(Data(1,20),out _);s.TryPrepare(()=>Data(2,80),out var candidate,out _);candidate.Dispose();return Tuple.Create(s,candidate,weak);
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] static Tuple<ConfigStore<FixtureConfig>,PreparedConfig<FixtureConfig>,WeakReference> RetainTicket()
    {
        var s=Store();s.TryPublish(Data(1,20),out _);s.TryPrepare(()=>Data(2,80),out var candidate,out _);Check(s.TryCommit(candidate,out _));
        var weak=new WeakReference(s.Current);s.TryPublish(Data(3,90),out _);return Tuple.Create(s,candidate,weak);
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] static Tuple<ConfigStore<FixtureConfig>,WeakReference[]> StressUpdates()
    {
        var s=Store();s.TryPublish(Data(1,10),out _);var held=s.Capture();var weak=new WeakReference[1000];
        for(int i=0;i<1000;i++) {
            weak[i]=new WeakReference(s.Current);Check(s.TryPublish(Data(i+2,(i+2)*10),out _));
            Check(held.Value.Tile.Get(5).RewardRecord.Coins==10);
            if(i%25==0){var previous=s.Capture();Check(!s.TryPrepare(()=>CompactConfig.Load(new byte[48]),out _,out _));Check(ReferenceEquals(previous,s.Capture()));}
        }
        return Tuple.Create(s,weak);
    }
    static byte[] ScalarPacket(FieldKind kind,Action<BinaryWriter> write)
    {
        using var body=new MemoryStream();using(var w=new BinaryWriter(body,System.Text.Encoding.UTF8,true)) {
            w.Write(1);w.Write(1);w.Write("A");w.Write(2);w.Write(1);
            w.Write("id");w.Write((byte)FieldKind.Int);w.Write("");w.Write(1);
            w.Write("value");w.Write((byte)kind);w.Write("");write(w);
        }
        return Envelope(body.ToArray(),true);
    }
    static byte[] Packet(bool modern,string reference)
    {
        using var body=new MemoryStream();using(var w=new BinaryWriter(body,System.Text.Encoding.UTF8,true)) {
            w.Write(1);w.Write(1);w.Write("A");w.Write(2);w.Write(1);
            w.Write("id");w.Write((byte)FieldKind.Int);if(modern)w.Write("");w.Write(1);
            w.Write("target");w.Write((byte)FieldKind.Int);if(modern)w.Write(reference??"");w.Write(1);
        }
        return Envelope(body.ToArray(),modern);
    }
    static byte[] Envelope(byte[] payload,bool modern)
    {
        using var sha=System.Security.Cryptography.SHA256.Create();using var output=new MemoryStream();using var writer=new BinaryWriter(output);
        writer.Write(modern?0x32434a47:0x31434a47);writer.Write(payload.Length);writer.Write(sha.ComputeHash(payload));writer.Write(payload);return output.ToArray();
    }
    static void Throws(Action a){try{a();}catch{return;}throw new Exception("Expected rejection");}
    static void Check(bool b,string message="Assertion failed"){if(!b)throw new Exception(message);}
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL "+name+": "+e);}}
}
