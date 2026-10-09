using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameJson;
using GameJson.Editor;
using UnityEditor;
using UnityEngine;

namespace GameConfigSamples.Editor
{
    public static class SourceSampleBuild
    {
        [MenuItem("Tools/Game-Json/Verify source-independent sample")]
        public static void Verify()
        {
            foreach (string guid in AssetDatabase.FindAssets("SourceSampleBuild t:MonoScript")) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(path) != "SourceSampleBuild.cs") continue;
                Run(Path.GetDirectoryName(Path.GetDirectoryName(path)));
                AssetDatabase.Refresh();
                return;
            }
            throw new InvalidOperationException("Import the Source Independent sample first.");
        }
        public static void Run(string directory)
        {
            var rewards = new ConfigTableSchema("Reward", new[] { new ConfigField("id", FieldKind.Int),
                new ConfigField("coins", FieldKind.Int), new ConfigField("name", FieldKind.String) });
            var tiles = new ConfigTableSchema("Tile", new[] { new ConfigField("id", FieldKind.Int),
                new ConfigField("reward", FieldKind.Int, "Reward") });
            var schema = new ConfigSchema(new[] { rewards, tiles });
            var code = new ConfigDocument(1, new[] {
                new ConfigTableData(rewards, new[] {
                    new ConfigRow(new[] { ConfigValue.Int(1), ConfigValue.Int(20), ConfigValue.String("Welcome") }),
                    new ConfigRow(new[] { ConfigValue.Int(3), ConfigValue.Int(40), ConfigValue.String("Bonus") }) }),
                new ConfigTableData(tiles, new[] { new ConfigRow(new[] { ConfigValue.Int(5), ConfigValue.Int(1) }) }) });
            var csv = new CsvConfigSource(1, schema, new Dictionary<string, string> {
                ["Reward"] = File.ReadAllText(Path.Combine(directory, "Reward.csv")),
                ["Tile"] = File.ReadAllText(Path.Combine(directory, "Tile.csv")) }, directory);
            var json = new JsonConfigSource(File.ReadAllText(Path.Combine(directory, "source.json")));
            byte[] expected = ConfigBuild.Compile(code);
            string api = ConfigBuild.Generate(code, "GameConfigSamples", "SourceConfig");
            foreach (var document in new[] { csv.Read(), json.Read() }) {
                if (!expected.SequenceEqual(ConfigBuild.Compile(document)) ||
                    api != ConfigBuild.Generate(document, "GameConfigSamples", "SourceConfig"))
                    throw new InvalidOperationException("Sources are not equivalent.");
            }
            ConfigMenus.GenerateSource(csv, directory, "GameConfigSamples", "SourceConfig");
            Debug.Log("SOURCE SAMPLE PASSED: JSON == CSV == C#; " + expected.Length + " bytes.");
        }
    }
}
