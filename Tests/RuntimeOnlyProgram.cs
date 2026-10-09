using System;
using System.Collections.Generic;
using System.IO;
using GameJson;
static class RuntimeOnlyProgram
{
    static int Main()
    {
        var schema = new ConfigSchema(new[]{new ConfigTableSchema("Item",new[]{new ConfigField("id",FieldKind.Int),new ConfigField("price",FieldKind.Long)})});
        byte[] bytes = ConfigBuild.Compile(new CsvConfigSource(1,schema,new Dictionary<string,string>{{"Item","id,price\n1,5000000000\n"}}));
        var table = CompactConfig.Load(bytes).Table("Item");var price = ConfigColumns.Long(table,"price");
        if (price[table.Find(1)] != 5000000000) return 1;
        foreach(string file in Directory.GetFiles(AppContext.BaseDirectory,"*.dll"))
            if (Path.GetFileName(file).Contains("Newtonsoft") || Path.GetFileName(file)=="Game.Config.Json.dll") throw new Exception("Unexpected JSON dependency: "+file);
        for(int i=0;i<10000;i++)_ = price[0];long before=GC.GetAllocatedBytesForCurrentThread(),sum=0;
        for(int i=0;i<200000;i++)sum+=price[0];
        if(GC.GetAllocatedBytesForCurrentThread()!=before || sum!=1000000000000000L)return 1;
        Console.WriteLine("NO-JSON CONSUMER PASSED: CSV -> common compiler -> runtime; 200000 reads, 0 B");return 0;
    }
}
