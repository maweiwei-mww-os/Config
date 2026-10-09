using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameJson
{
    public sealed class JsonConfigSource : IConfigSource
    {
        readonly string json, sourceName;
        public JsonConfigSource(string json, string sourceName = "json")
        { this.json = json; this.sourceName = sourceName; }
        JObject Root()
        {
            if (string.IsNullOrEmpty(json) || json.Length > 32 * 1024 * 1024) throw new InvalidDataException(sourceName + ": JSON is empty or exceeds 32M characters.");
            try {
                using var input = new StringReader(json);
                using var reader = new JsonTextReader(input) { DateParseHandling = DateParseHandling.None };
                var root = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                while (reader.Read()) if (reader.TokenType != JsonToken.Comment) throw new InvalidDataException(sourceName + ": unexpected content after root.");
                return root;
            }
            catch (JsonException e) { throw new InvalidDataException(sourceName + ": " + e.Message, e); }
        }
        public ConfigSchema ReadSchema()
        {
            var tables = new List<ConfigTableSchema>();
            foreach (var token in Tables(Root())) tables.Add(Schema(token));
            return new ConfigSchema(tables);
        }
        public ConfigDocument Read()
        {
            var root = Root(); int version = (int)Cell(root["version"], FieldKind.Int, sourceName + ".version");
            var tables = new List<ConfigTableData>();
            foreach (var token in Tables(root)) {
                var schema = Schema(token); var rows = new List<ConfigRow>();
                var inputRows = token["rows"] as JArray ?? throw new InvalidDataException(sourceName + " " + schema.Name + ": missing rows.");
                foreach (var rowToken in inputRows) {
                    string at = sourceName + ":" + ((IJsonLineInfo)rowToken).LineNumber + " " + schema.Name;
                    var row = rowToken as JArray ?? throw new InvalidDataException(at + ": row must be an array.");
                    if (row.Count > schema.Fields.Count) throw new InvalidDataException(at + ": row width mismatch.");
                    var values = new ConfigValue[row.Count];
                    for (int i = 0; i < row.Count; i++) values[i] = Cell(row[i], schema.Fields[i].Kind, at + "." + schema.Fields[i].Name);
                    rows.Add(new ConfigRow(values, at));
                }
                tables.Add(new ConfigTableData(schema, rows));
            }
            return new ConfigDocument(version, tables);
        }
        JArray Tables(JObject root) => root["tables"] as JArray ?? throw new InvalidDataException(sourceName + ": tables must be an array.");
        ConfigTableSchema Schema(JToken token)
        {
            if (!(token is JObject)) throw new InvalidDataException(sourceName + ": table must be an object.");
            string name = Text(token["name"], sourceName + " table name");
            var fields = token["fields"] as JArray ?? throw new InvalidDataException(sourceName + " " + name + ": missing fields.");
            var result = new List<ConfigField>();
            foreach (var f in fields) {
                if (!(f is JObject)) throw new InvalidDataException(sourceName + " " + name + ": field must be an object.");
                string fieldName = Text(f["name"], name + " field name"), location = sourceName + " " + name + "." + fieldName;
                string type = Text(f["type"], location + " type");
                FieldKind kind;
                switch (type) {
                    case "int": kind = FieldKind.Int; break; case "long": kind = FieldKind.Long; break;
                    case "float": kind = FieldKind.Float; break; case "bool": kind = FieldKind.Bool; break;
                    case "string": kind = FieldKind.String; break; case "int[]": kind = FieldKind.IntList; break;
                    default: throw new InvalidDataException(location + ": unsupported type " + type);
                }
                string reference = f["ref"] == null ? null : Text(f["ref"], location + " reference");
                ConfigValue? value = f["default"] == null ? (ConfigValue?)null : Cell(f["default"], kind, location + " default");
                result.Add(new ConfigField(fieldName, kind, reference, value));
            }
            return new ConfigTableSchema(name, result);
        }
        static string Text(JToken token, string at)
        { if (token?.Type != JTokenType.String) throw new InvalidDataException(at + ": expected string."); return (string)token; }
        static ConfigValue Cell(JToken token, FieldKind kind, string at)
        {
            try {
                switch (kind) {
                    case FieldKind.Int:
                        if (token?.Type != JTokenType.Integer) break;
                        return ConfigValue.Int(checked((int)(long)token));
                    case FieldKind.Long:
                        if (token?.Type != JTokenType.Integer) break;
                        return ConfigValue.Long((long)token);
                    case FieldKind.Float:
                        if (token?.Type != JTokenType.Integer && token?.Type != JTokenType.Float) break;
                        return ConfigValue.Float((float)(double)token);
                    case FieldKind.Bool:
                        if (token?.Type != JTokenType.Boolean) break;
                        return ConfigValue.Bool((bool)token);
                    case FieldKind.String:
                        return ConfigValue.String(Text(token, at));
                    case FieldKind.IntList:
                        if (!(token is JArray array)) break;
                        var items = new int[array.Count];
                        for (int i = 0; i < items.Length; i++) items[i] = (int)Cell(array[i], FieldKind.Int, at + "[" + i + "]");
                        return ConfigValue.Ints(items);
                }
            } catch (OverflowException e) { throw new InvalidDataException(at + ": numeric overflow for " + kind, e); }
            throw new InvalidDataException(at + ": expected " + kind + ".");
        }
    }
    // Source-compatible 0.1-0.3 entry points. New build integrations use ConfigBuild.
    public static class ConfigCompiler
    {
        public static byte[] Compile(string json) => ConfigBuild.Compile(new JsonConfigSource(json));
    }
    public static class ConfigCodeGenerator
    {
        public static string Generate(string json, string namespaceName, string className) =>
            ConfigBuild.Generate(new JsonConfigSource(json).Read(), namespaceName, className);
    }
}
