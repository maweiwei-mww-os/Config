using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GameJson.Editor
{
    public static class ConfigMenus
    {
        [MenuItem("Tools/Game-Json/Compile and generate typed API")]
        public static void GenerateSelected()
        {
            string path=AssetDatabase.GetAssetPath(Selection.activeObject);
            if(!path.EndsWith(".json",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Select a JSON configuration asset.");
            string stem=Path.GetFileNameWithoutExtension(path);
            string name=char.ToUpperInvariant(stem[0])+stem.Substring(1)+"Config";
            GenerateFile(path,"GameJson.Generated",name);AssetDatabase.Refresh();
        }
        public static string GenerateFile(string path,string namespaceName,string className)
        {
            return GenerateSource(new JsonConfigSource(File.ReadAllText(path),path),Path.GetDirectoryName(path),namespaceName,className);
        }
        public static string GenerateSource(IConfigSource source,string directory,string namespaceName,string className)
        {
            var document=source.Read();
            string code=ConfigBuild.Generate(document,namespaceName,className);
            byte[] bytes=ConfigBuild.Compile(document);CompactConfig.Load(bytes);
            Directory.CreateDirectory(directory);
            Write(Path.Combine(directory,className+".g.cs"),System.Text.Encoding.UTF8.GetBytes(code));
            string output=Path.Combine(directory,className+".bytes");Write(output,bytes);
            Debug.Log("Game-Json generated "+className+" and "+output);return output;
        }
        [MenuItem("Tools/Game-Json/Compile selected JSON")]
        public static void CompileSelected()
        {
            string path=AssetDatabase.GetAssetPath(Selection.activeObject);
            if(!path.EndsWith(".json",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Select a JSON configuration asset.");
            CompileFile(path);AssetDatabase.Refresh();
        }
        public static string CompileFile(string path)
        {
            byte[] bytes=ConfigCompiler.Compile(File.ReadAllText(path));
            CompactConfig.Load(bytes);
            string output=Path.Combine(Path.GetDirectoryName(path),Path.GetFileNameWithoutExtension(path)+"_packed.bytes");
            Write(output,bytes);
            Debug.Log("Game-Json compiled "+path+" -> "+output+" ("+bytes.Length+" bytes)");return output;
        }
        static void Write(string path,byte[] bytes)
        {
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllBytes(temp,bytes);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
    }
}
