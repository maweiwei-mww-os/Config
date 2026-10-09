using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using GameJson;
using FrameworkFixture;

static class SourceProgram
{
    static int passed, failed;
    static ConfigTableSchema Reward = new ConfigTableSchema("Reward", new[] {
        new ConfigField("id",FieldKind.Int),new ConfigField("coins",FieldKind.Int),new ConfigField("name",FieldKind.String),
        new ConfigField("tags",FieldKind.IntList),new ConfigField("price",FieldKind.Long),new ConfigField("rate",FieldKind.Float),new ConfigField("enabled",FieldKind.Bool) });
    static ConfigTableSchema Tile = new ConfigTableSchema("Tile",new[]{new ConfigField("id",FieldKind.Int),new ConfigField("reward",FieldKind.Int,"Reward"),new ConfigField("bonus",FieldKind.IntList,"Reward")});
    static ConfigTableSchema Sparse = new ConfigTableSchema("Sparse",new[]{new ConfigField("id",FieldKind.Int),new ConfigField("value",FieldKind.Int)});
    static ConfigSchema Schema = new ConfigSchema(new[]{Reward,Tile,Sparse});
    static Dictionary<string,string> Csv() => new Dictionary<string,string> {
        ["Reward"]="name,id,coins,tags,price,rate,enabled\r\nshared,3,40,,0,1,false\r\nshared,1,20,2;3,5000000000,0.25,true\r\n",
        ["Tile"]="id,reward,bonus\n5,1,1;3\n", ["Sparse"]="id,value\n1000000,8\n1,-5\n" };
    static ConfigDocument Code() => new ConfigDocument(1,new[] {
        new ConfigTableData(Reward,new[]{new ConfigRow(new[]{ConfigValue.Int(1),ConfigValue.Int(20),ConfigValue.String("shared"),ConfigValue.Ints(new[]{2,3}),ConfigValue.Long(5000000000),ConfigValue.Float(.25f),ConfigValue.Bool(true)}),
            new ConfigRow(new[]{ConfigValue.Int(3),ConfigValue.Int(40),ConfigValue.String("shared"),ConfigValue.Ints(Array.Empty<int>()),ConfigValue.Long(0),ConfigValue.Float(1),ConfigValue.Bool(false)})}),
        new ConfigTableData(Tile,new[]{new ConfigRow(new[]{ConfigValue.Int(5),ConfigValue.Int(1),ConfigValue.Ints(new[]{1,3})})}),
        new ConfigTableData(Sparse,new[]{new ConfigRow(new[]{ConfigValue.Int(1),ConfigValue.Int(-5)}),new ConfigRow(new[]{ConfigValue.Int(1000000),ConfigValue.Int(8)})}) });
    static int Main()
    {
        Test("runtime and build assemblies have no JSON dependency",()=>{
            foreach(var assembly in new[]{typeof(CompactConfig).Assembly,typeof(ConfigBuild).Assembly})
                Check(!assembly.GetReferencedAssemblies().Any(a=>a.Name.Contains("Newtonsoft")||a.Name=="Game.Config.Json"));
        });
        Test("0.3.1 release binary and generated API remain byte compatible",()=>{
            string json=File.ReadAllText("Tests/Fixtures/legacy031.json");
            Check(ConfigCompiler.Compile(json).SequenceEqual(File.ReadAllBytes("Tests/Fixtures/legacy031.bytes")));
            Check(ConfigCodeGenerator.Generate(json,"GameJson.Examples.Generated","ExampleConfig")==File.ReadAllText("Tests/Fixtures/legacy031.golden.txt"));
        });
        Test("JSON CSV and typed source produce identical binary and generated API",()=>{
            var json=new JsonConfigSource(File.ReadAllText("Tests/Fixtures/framework.json"),"fixture.json").Read();
            var csv=new CsvConfigSource(1,Schema,Csv()).Read();var code=Code();
            byte[] expected=ConfigCompiler.Compile(File.ReadAllText("Tests/Fixtures/framework.json"));
            foreach(var source in new[]{json,csv,code}) {
                Check(expected.SequenceEqual(ConfigBuild.Compile(source)));Check(new FixtureConfig(CompactConfig.Load(ConfigBuild.Compile(source))).Tile.Get(5).RewardRecord.Coins==20);
                Check(ConfigBuild.Generate(source,"FrameworkFixture","FixtureConfig")==ConfigCodeGenerator.Generate(File.ReadAllText("Tests/Fixtures/framework.json"),"FrameworkFixture","FixtureConfig"));
            }
        });
        Test("custom source needs only the public build contract",()=>Check(ConfigBuild.Compile(new CustomSource()).SequenceEqual(ConfigBuild.Compile(Code()))));
        Test("typed model owns input lists and rows",()=>{
            var list=new[]{1,3};var cells=new[]{ConfigValue.Int(5),ConfigValue.Int(1),ConfigValue.Ints(list)};
            var row=new ConfigRow(cells);list[0]=999;cells[1]=ConfigValue.Int(999);
            var d=Code();var table=new ConfigTableData(Tile,new[]{row});var doc=new ConfigDocument(1,new[]{d.Tables[0],table,d.Tables[2]});
            Check(new FixtureConfig(CompactConfig.Load(ConfigBuild.Compile(doc))).Tile.Get(5).ResolveBonus(0).Coins==20);
        });
        Test("CSV quoting commas quotes and multiline strings",()=>{
            var schema=new ConfigSchema(new[]{new ConfigTableSchema("A",new[]{new ConfigField("id",FieldKind.Int),new ConfigField("text",FieldKind.String)})});
            var data=new CsvConfigSource(1,schema,new Dictionary<string,string>{{"A","id,text\r\n1,\"a,b\"\r\n2,\"say \"\"hi\"\"\nnext\"\r\n"}}).Read();
            var t=CompactConfig.Load(ConfigBuild.Compile(data)).Table("A");int f=t.Bind("text",FieldKind.String);
            Check(t.Text(t.Find(1),f)=="a,b"&&t.Text(t.Find(2),f)=="say \"hi\"\nnext");
        });
        Test("CSV is culture independent",()=>{
            var old=CultureInfo.CurrentCulture;try{CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("fr-FR");Check(ConfigBuild.Compile(new CsvConfigSource(1,Schema,Csv())).SequenceEqual(ConfigBuild.Compile(Code())));}finally{CultureInfo.CurrentCulture=old;}
        });
        Test("defaults apply to omitted fields not explicit empty strings",()=>{
            var s=new ConfigSchema(new[]{new ConfigTableSchema("A",new[]{new ConfigField("id",FieldKind.Int),new ConfigField("text",FieldKind.String,null,ConfigValue.String("fallback"))})});
            var a=CompactConfig.Load(ConfigBuild.Compile(new CsvConfigSource(1,s,new Dictionary<string,string>{{"A","id\n1\n"}}))).Table("A");Check(a.Text(0,a.Bind("text",FieldKind.String))=="fallback");
            var b=CompactConfig.Load(ConfigBuild.Compile(new CsvConfigSource(1,s,new Dictionary<string,string>{{"A","id,text\n1,\n"}}))).Table("A");Check(b.Text(0,b.Bind("text",FieldKind.String))=="");
        });
        Test("common validator rejects missing required value wrong type duplicate id broken ref",()=>{
            var docs=new[]{new ConfigDocument(1,new[]{new ConfigTableData(Reward,new[]{new ConfigRow(new[]{ConfigValue.Int(1)},"code:7")})}),
                new ConfigDocument(1,new[]{new ConfigTableData(Sparse,new[]{new ConfigRow(new[]{ConfigValue.Int(1),ConfigValue.String("bad")},"code:9")})}),
                new ConfigDocument(1,new[]{new ConfigTableData(Sparse,new[]{new ConfigRow(new[]{ConfigValue.Int(1),ConfigValue.Int(1)}),new ConfigRow(new[]{ConfigValue.Int(1),ConfigValue.Int(2)})})}),
                new ConfigDocument(1,new[]{new ConfigTableData(Tile,new[]{new ConfigRow(new[]{ConfigValue.Int(1),ConfigValue.Int(999),ConfigValue.Ints(new[]{999})})})})};
            foreach(var d in docs)Reject(()=>ConfigBuild.Compile(d));
        });
        Test("CSV rejects duplicate unknown and missing headers malformed quoting width and scalar",()=>{
            foreach(var text in new[]{"id,id\n1,1", "id,other\n1,2", "id\n1", "id,value\n1,\"2", "id,value\n1,2,3", "id,value\n1,2.5", "id,value\n1,\"2\"x", "id,value\n1,2\""}) {
                var s=new ConfigSchema(new[]{Sparse});Reject(()=>ConfigBuild.Compile(new CsvConfigSource(1,s,new Dictionary<string,string>{{"Sparse",text}})));
            }
        });
        Test("diagnostics contain source table row and field",()=>{
            var map=Csv();map["Reward"]=map["Reward"].Replace("5000000000","oops");
            var e=Reject(()=>ConfigBuild.Compile(new CsvConfigSource(1,Schema,map)));Check(e.Message.Contains("Reward")&&e.Message.Contains("price")&&e.Message.Contains("3"));
        });
        Test("unknown types missing values and bad defaults fail even with no rows",()=>{
            Reject(()=>ConfigBuild.Compile(new ConfigDocument(1,new[]{new ConfigTableData(new ConfigTableSchema("A",new[]{new ConfigField("id",FieldKind.Int),new ConfigField("bad",(FieldKind)255)}),Array.Empty<ConfigRow>())})));
            Reject(()=>ConfigBuild.Compile(new ConfigDocument(1,new[]{new ConfigTableData(new ConfigTableSchema("A",new[]{new ConfigField("id",FieldKind.Int),new ConfigField("bad",FieldKind.Int,null,ConfigValue.String("x"))}),Array.Empty<ConfigRow>())})));
        });
        Test("JSON text preserves ISO dates as strings",()=>{
            const string input="{\"version\":1,\"tables\":[{\"name\":\"A\",\"fields\":[{\"name\":\"id\",\"type\":\"int\"},{\"name\":\"text\",\"type\":\"string\"}],\"rows\":[[1,\"2026-10-09T10:00:00Z\"]]}]}";
            var t=CompactConfig.Load(ConfigCompiler.Compile(input)).Table("A");Check(t.Text(0,t.Bind("text",FieldKind.String))=="2026-10-09T10:00:00Z");
        });
        Test("JSON defaults reject explicit null and duplicate properties",()=>{
            const string input="{\"version\":1,\"tables\":[{\"name\":\"A\",\"fields\":[{\"name\":\"id\",\"type\":\"int\"},{\"name\":\"coins\",\"type\":\"int\",\"default\":20}],\"rows\":[[1]]}]}";
            var t=CompactConfig.Load(ConfigCompiler.Compile(input)).Table("A");Check(t.Int(0,t.Bind("coins",FieldKind.Int))==20);
            Reject(()=>ConfigCompiler.Compile(input.Replace("[[1]]","[[1,null]]")));
            Reject(()=>ConfigCompiler.Compile(input.Replace("\"version\":1","\"version\":1,\"version\":2")));
        });
        Test("CSV rejects overflow nonfinite scalars and malformed lists",()=>{
            foreach(var pair in new[]{("price","5000000000","9223372036854775808"),("rate","0.25","NaN"),("rate","0.25","1e100"),("tags","2;3","2;"),("enabled","true","1")}) {
                var map=Csv();map["Reward"]=map["Reward"].Replace(pair.Item2,pair.Item3);var error=Reject(()=>ConfigBuild.Compile(new CsvConfigSource(1,Schema,map)));Check(error.Message.Contains(pair.Item1));
            }
        });
        Test("CSV handles BOM and empty final cells and rejects extra tables",()=>{
            var schema=new ConfigSchema(new[]{new ConfigTableSchema("A",new[]{new ConfigField("id",FieldKind.Int),new ConfigField("text",FieldKind.String)})});
            var map=new Dictionary<string,string>{{"A","\uFEFFid,text\r1,"}};var t=CompactConfig.Load(ConfigBuild.Compile(new CsvConfigSource(1,schema,map))).Table("A");Check(t.Text(0,t.Bind("text",FieldKind.String))=="");
            map["Extra"]="id\n1";Reject(()=>ConfigBuild.Compile(new CsvConfigSource(1,schema,map)));
        });
        Test("CSV multiline diagnostics track physical lines",()=>{
            var s=new ConfigSchema(new[]{new ConfigTableSchema("A",new[]{new ConfigField("id",FieldKind.Int),new ConfigField("text",FieldKind.String),new ConfigField("count",FieldKind.Int)})});
            var e=Reject(()=>ConfigBuild.Compile(new CsvConfigSource(1,s,new Dictionary<string,string>{{"A","id,text,count\n1,\"two\nlines\",2\n2,x,bad"}},"fixture")));
            Check(e.Message.Contains("fixture/A.csv:4.count"));
        });
        Test("common schema rejects duplicate fields and invalid revisions",()=>{
            Reject(()=>ConfigBuild.Compile(new ConfigDocument(0,Code().Tables)));
            var s=new ConfigTableSchema("A",new[]{new ConfigField("id",FieldKind.Int),new ConfigField("id",FieldKind.Int)});
            Reject(()=>ConfigBuild.Compile(new ConfigDocument(1,new[]{new ConfigTableData(s,Array.Empty<ConfigRow>())})));
        });
        Console.WriteLine($"SOURCE RESULT {passed} passed / {failed} failed");return failed==0?0:1;
    }
    sealed class CustomSource:IConfigSource{public ConfigDocument Read()=>Code();}
    static Exception Reject(Action action){try{action();}catch(Exception e){return e;}throw new Exception("Expected rejection");}
    static void Check(bool b){if(!b)throw new Exception("Assertion failed");}
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL "+name+": "+e);}}
}
