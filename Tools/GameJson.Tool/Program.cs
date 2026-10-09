using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using GameJson;

internal static class Program
{
    static int Main(string[] args)
    {
        bool csv=args.Length==7 && args[0]=="--csv";
        if(!csv && (args.Length!=4 || args[0]=="--csv")){Console.Error.WriteLine("Usage: GameJson.Tool <source.json> <output-directory> <namespace> <class-name>\n   or: GameJson.Tool --csv <schema.json> <csv-directory> <version> <output-directory> <namespace> <class-name>");return 2;}
        try {
            IConfigSource source;string output,ns,name;
            if(csv) {
                var schema=new JsonConfigSource(File.ReadAllText(args[1]),args[1]).ReadSchema();
                var files=new Dictionary<string,string>(StringComparer.Ordinal);string directory=Path.GetFullPath(args[2]);
                foreach(var table in schema.Tables) {
                    if(table.Name!=Path.GetFileName(table.Name)||table.Name.IndexOfAny(Path.GetInvalidFileNameChars())>=0)throw new InvalidDataException("Table name is not a safe CSV filename: "+table.Name);
                    files.Add(table.Name,File.ReadAllText(Path.Combine(directory,table.Name+".csv")));
                }
                source=new CsvConfigSource(int.Parse(args[3],System.Globalization.CultureInfo.InvariantCulture),schema,files,directory);output=args[4];ns=args[5];name=args[6];
            } else {source=new JsonConfigSource(File.ReadAllText(args[0]),args[0]);output=args[1];ns=args[2];name=args[3];}
            var document=source.Read();
            string generated=ConfigBuild.Generate(document,ns,name);
            byte[] bytes=ConfigBuild.Compile(document);CompactConfig.Load(bytes);
            Directory.CreateDirectory(output);
            Write(Path.Combine(output,name+".g.cs"),new UTF8Encoding(false).GetBytes(generated));
            Write(Path.Combine(output,name+".bytes"),bytes);
            Console.WriteLine("Generated "+name+": "+bytes.Length+" bytes; "+Path.GetFullPath(output));return 0;
        } catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}
    }
    static void Write(string path,byte[] data)
    {
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {File.WriteAllBytes(temp,data);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
        finally {if(File.Exists(temp))File.Delete(temp);}
    }
}
