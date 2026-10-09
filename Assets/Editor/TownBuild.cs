using System;
using System.IO;
using GameJson;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DiceTown
{
    public static class TownBuild
    {
        [MenuItem("Town/Prepare game")]
        public static void Prepare()
        {
            Directory.CreateDirectory("Assets/Resources/Config");Directory.CreateDirectory("Assets/Scenes");
            const string materialPath="Assets/Resources/TownSurface.mat";
            if(AssetDatabase.LoadAssetAtPath<Material>(materialPath)==null)
            {
                var shader=Shader.Find("Standard");
                if(shader==null)throw new InvalidOperationException("Standard shader is unavailable.");
                var material=new Material(shader);material.SetFloat("_Glossiness",.15f);
                AssetDatabase.CreateAsset(material,materialPath);
            }
            for(int version=1;version<=2;version++)
            {
                string path="Assets/Resources/Config/town_v"+version+".json";
                if(!File.Exists(path))File.WriteAllText(path,TownContent.Create(version));
                byte[] bytes=ConfigCompiler.Compile(File.ReadAllText(path));new TownRules(CompactConfig.Load(bytes));
                GameJson.Editor.ConfigMenus.CompileFile(path);
            }
            AssetDatabase.Refresh();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);new GameObject("Dice Town").AddComponent<TownGame>();
            EditorSceneManager.SaveScene(scene,"Assets/Scenes/Town.unity");
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene("Assets/Scenes/Town.unity",true)};
            PlayerSettings.companyName="GameJson";PlayerSettings.productName="Dice Town";
            PlayerSettings.defaultScreenWidth=1280;PlayerSettings.defaultScreenHeight=800;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
            PlayerSettings.resizableWindow=true;PlayerSettings.runInBackground=true;PlayerSettings.colorSpace=ColorSpace.Linear;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone,ScriptingImplementation.Mono2x);
            QualitySettings.SetQualityLevel(2,true);QualitySettings.antiAliasing=4;
            AssetDatabase.SaveAssets();Debug.Log("TOWN PREPARED");
        }
        [MenuItem("Town/Build Windows game")]
        public static void Build()
        { BuildPlayer("Builds/Windows", BuildOptions.None); }
        [MenuItem("Town/Build performance verification game")]
        public static void BuildPerformance()
        { BuildPlayer("Builds/Performance", BuildOptions.Development); }
        static void BuildPlayer(string folder, BuildOptions options)
        {
            Prepare();Directory.CreateDirectory(folder);
            var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Scenes/Town.unity"},locationPathName=folder+"/DiceTown.exe",target=BuildTarget.StandaloneWindows64,options=options});
            if(result.summary.result!=BuildResult.Succeeded)throw new Exception("Windows build failed: "+result.summary.result);
            Debug.Log("TOWN BUILD SUCCEEDED "+result.summary.totalSize);
        }
    }
}
