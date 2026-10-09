using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using GameJson;
using UnityEngine;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using Newtonsoft.Json.Linq;

public static class PackageSmoke
{
    public static void Run()
    {
        const string json="{\"version\":1,\"tables\":[{\"name\":\"Reward\",\"fields\":[{\"name\":\"id\",\"type\":\"int\"},{\"name\":\"coins\",\"type\":\"int\"},{\"name\":\"tags\",\"type\":\"int[]\"}],\"rows\":[[1,50,[2,3]],[9,100,[4]]]}]}";
        byte[] bytes=ConfigCompiler.Compile(json);
        var table=CompactConfig.Load(bytes).Table("Reward");
        if(table.Int(table.Find(9),table.Bind("coins",FieldKind.Int))!=100)throw new Exception("Lookup failed.");
        if(table.List(table.Find(1),table.Bind("tags",FieldKind.IntList))[1]!=3)throw new Exception("List failed.");
        bytes[bytes.Length-1]^=1;bool rejected=false;
        try{CompactConfig.Load(bytes);}catch(InvalidDataException){rejected=true;}
        if(!rejected)throw new Exception("Corruption was accepted.");
        File.WriteAllText("package-smoke-result.json","{\"passed\":true,\"source\":\"installed tgz in empty Unity project\",\"tests\":[\"compile\",\"lookup\",\"list\",\"corruption\"]}");
        Debug.Log("PACKAGE SMOKE PASSED");
    }
    public static void VerifyFramework()
    {
        Run();PrepareResources();
        File.WriteAllText("framework-editor-result.json",JsonUtility.ToJson(FrameworkProbe.RunChecks(),true));
        Debug.Log("FRAMEWORK EDITOR PASSED");
    }
    public static void BuildMono(){Build(false);}
    public static void BuildIL2CPP(){Build(true);}
    static void PrepareResources()
    {
        GameConfigSamples.Editor.SourceSampleBuild.Run("Assets/SourceIndependent");
        foreach(var assembly in new[]{typeof(CompactConfig).Assembly,typeof(ConfigBuild).Assembly})
            if(assembly.GetReferencedAssemblies().Any(a=>a.Name.Contains("Newtonsoft")||a.Name=="Game.Config.Json"))throw new Exception("Core depends on JSON.");
        var jsonSource=new JsonConfigSource(File.ReadAllText("../../Tests/Fixtures/framework.json"));
        var csv=new CsvConfigSource(1,jsonSource.ReadSchema(),new Dictionary<string,string>{
            ["Reward"]="name,id,coins,tags,price,rate,enabled\nshared,3,40,,0,1,false\nshared,1,20,2;3,5000000000,0.25,true\n",
            ["Tile"]="id,reward,bonus\n5,1,1;3\n",["Sparse"]="id,value\n1000000,8\n1,-5\n"});
        byte[] csvBytes=ConfigBuild.Compile(csv);
        if(!csvBytes.SequenceEqual(ConfigBuild.Compile(jsonSource)))throw new Exception("CSV differs from JSON.");
        File.WriteAllBytes("Assets/Resources/FixtureConfigCsv.bytes",csvBytes);
        var source=JObject.Parse(File.ReadAllText("../../Tests/Fixtures/framework.json"));
        source["version"]=2;source["tables"][0]["rows"][0][1]=80;
        File.WriteAllBytes("Assets/Resources/FixtureConfigUpdate.bytes",ConfigCompiler.Compile(source.ToString()));
        foreach(int count in new[]{1000,10000,100000}) {
            var input=BenchmarkProgram.Generate(count,"../../Tests/Fixtures/framework.json");
            string prefix="Assets/Resources/Bench"+count;
            File.WriteAllText(prefix+"Source.json",input.Source);File.WriteAllText(prefix+"Dto.json",input.Dto);
            File.WriteAllBytes(prefix+"Binary.bytes",ConfigCompiler.Compile(input.Source));
        }
        AssetDatabase.Refresh();
    }
    static void Build(bool il2cpp)
    {
        PrepareResources();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        new GameObject("Framework verification").AddComponent<FrameworkProbe>();EditorSceneManager.SaveScene(scene,"Assets/FrameworkVerification.unity");
        PlayerSettings.companyName="GameJson";PlayerSettings.productName="FrameworkVerification";
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone,il2cpp?ScriptingImplementation.IL2CPP:ScriptingImplementation.Mono2x);
        PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Standalone,ManagedStrippingLevel.High);
        string folder=il2cpp?"Builds/IL2CPP":"Builds/Mono";Directory.CreateDirectory(folder);
        var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/FrameworkVerification.unity"},locationPathName=folder+"/FrameworkProbe.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});
        if(result.summary.result!=BuildResult.Succeeded)throw new Exception("Framework build failed: "+result.summary.result);
        Debug.Log("FRAMEWORK BUILD PASSED "+folder);
    }
}
