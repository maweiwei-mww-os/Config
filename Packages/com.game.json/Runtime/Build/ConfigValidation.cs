using System;
using System.Collections.Generic;
using System.IO;

namespace GameJson
{
    internal sealed class SourceDocument
    {
        internal int Version;
        internal readonly List<SourceTable> Tables = new List<SourceTable>();
        internal static SourceDocument Create(ConfigDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (document.Version < 1) throw new InvalidDataException("version must be positive.");
            if (document.Tables.Count < 1 || document.Tables.Count > 128) throw new InvalidDataException("Expected 1..128 tables.");
            var result = new SourceDocument { Version = document.Version };
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var input in document.Tables) {
                if (input == null) throw new InvalidDataException("Null table.");
                var schema = input.Schema; Name(schema.Name, "table");
                if (!names.Add(schema.Name)) throw new InvalidDataException("Duplicate table: " + schema.Name);
                if (schema.Fields.Count < 1 || schema.Fields.Count > 128) throw new InvalidDataException(schema.Name + ": expected 1..128 fields.");
                var table = new SourceTable { Name = schema.Name };
                var fields = new HashSet<string>(StringComparer.Ordinal);
                foreach (var field in schema.Fields) {
                    if (field == null) throw new InvalidDataException(schema.Name + ": null field.");
                    Name(field.Name, schema.Name);
                    string location = schema.Name + "." + field.Name;
                    if (!fields.Add(field.Name)) throw new InvalidDataException(location + ": duplicate field.");
                    if (field.Kind < FieldKind.Int || field.Kind > FieldKind.Bool) throw new InvalidDataException(location + ": unsupported kind.");
                    if (field.Reference != null) {
                        Name(field.Reference, location);
                        if (field.Kind != FieldKind.Int && field.Kind != FieldKind.IntList) throw new InvalidDataException(location + ": reference requires int or int[].");
                    }
                    if (field.DefaultValue.HasValue) Value(field.DefaultValue.Value, field.Kind, location + " default");
                    table.Fields.Add(new SourceField { Name = field.Name, Kind = field.Kind, Reference = field.Reference, Index = table.Fields.Count });
                }
                if (schema.Fields[0].Name != "id" || schema.Fields[0].Kind != FieldKind.Int || schema.Fields[0].DefaultValue.HasValue)
                    throw new InvalidDataException(schema.Name + ": first field must be explicit integer id without a default.");
                if (input.Rows.Count > 1000000) throw new InvalidDataException(schema.Name + ": too many rows.");
                for (int r = 0; r < input.Rows.Count; r++) {
                    var row = input.Rows[r]; if (row == null) throw new InvalidDataException(schema.Name + ": null row.");
                    string at = (row.Location ?? "source") + " " + schema.Name + " row " + (r + 1);
                    if (row.Values.Count > schema.Fields.Count) throw new InvalidDataException(at + ": row width mismatch.");
                    var values = new ConfigValue[schema.Fields.Count];
                    for (int i = 0; i < values.Length; i++) {
                        var v = i < row.Values.Count ? row.Values[i] : default;
                        if ((byte)v.Kind == 0 && schema.Fields[i].DefaultValue.HasValue) v = schema.Fields[i].DefaultValue.Value;
                        Value(v, schema.Fields[i].Kind, at + "." + schema.Fields[i].Name); values[i] = v;
                    }
                    int id = (int)values[0];
                    if (id < 0 || !table.Ids.Add(id)) throw new InvalidDataException(at + ".id: invalid/duplicate id " + id);
                    table.Rows.Add(values); table.Locations.Add(id, at);
                }
                table.Rows.Sort((a,b) => ((int)a[0]).CompareTo((int)b[0])); result.Tables.Add(table);
            }
            result.Tables.Sort((a,b) => StringComparer.Ordinal.Compare(a.Name,b.Name));
            foreach (var table in result.Tables) foreach (var field in table.Fields) {
                if (field.Reference == null) continue;
                var target = result.Tables.Find(t => t.Name == field.Reference);
                if (target == null) throw new InvalidDataException(table.Name + "." + field.Name + ": missing reference table " + field.Reference);
                foreach (var row in table.Rows) {
                    if (field.Kind == FieldKind.Int) Reference(target,(int)row[field.Index],table, (int)row[0],field);
                    else foreach (int id in row[field.Index].AsInts()) Reference(target,id,table,(int)row[0],field);
                }
            }
            return result;
        }
        static void Name(string name, string at)
        { if (string.IsNullOrWhiteSpace(name) || name.Length > 128) throw new InvalidDataException(at + ": invalid name."); }
        static void Value(ConfigValue value, FieldKind kind, string at)
        {
            if (value.Kind != kind) throw new InvalidDataException(at + ": expected " + kind + ", got " + ((byte)value.Kind == 0 ? "missing" : value.Kind.ToString()));
            if (kind == FieldKind.Float && (float.IsNaN(value.AsFloat()) || float.IsInfinity(value.AsFloat()))) throw new InvalidDataException(at + ": expected finite float32.");
        }
        static void Reference(SourceTable target, int id, SourceTable table, int row, SourceField field)
        { if (!target.Ids.Contains(id)) throw new InvalidDataException(table.Locations[row] + "." + field.Name + ": missing " + target.Name + "[" + id + "]"); }
    }
    internal sealed class SourceTable
    {
        internal string Name;
        internal readonly List<SourceField> Fields = new List<SourceField>();
        internal readonly List<ConfigValue[]> Rows = new List<ConfigValue[]>();
        internal readonly HashSet<int> Ids = new HashSet<int>();
        internal readonly Dictionary<int, string> Locations = new Dictionary<int, string>();
    }
    internal sealed class SourceField { internal string Name, Reference; internal FieldKind Kind; internal int Index; }
}
