using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace GameJson
{
    public sealed class CsvConfigSource : IConfigSource
    {
        readonly int version;
        readonly ConfigSchema schema;
        readonly Dictionary<string, string> tables;
        readonly string sourceName;
        public CsvConfigSource(int version, ConfigSchema schema, IDictionary<string, string> tables, string sourceName = "csv")
        {
            this.version = version; this.schema = schema ?? throw new ArgumentNullException(nameof(schema));
            this.tables = new Dictionary<string, string>(tables ?? throw new ArgumentNullException(nameof(tables)), StringComparer.Ordinal);
            this.sourceName = sourceName;
        }
        public ConfigDocument Read()
        {
            var result = new List<ConfigTableData>(); var consumed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var table in schema.Tables) {
                if (table == null || table.Name == null) throw new InvalidDataException(sourceName + ": invalid table schema.");
                string path = sourceName + "/" + table.Name + ".csv";
                if (!tables.TryGetValue(table.Name, out string text)) throw new InvalidDataException(path + ": missing table input.");
                consumed.Add(table.Name); var records = ReadRecords(text, path);
                if (records.Count == 0) throw new InvalidDataException(path + ": missing header.");
                var header = records[0].Values; var map = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < header.Count; i++) {
                    if (string.IsNullOrEmpty(header[i]) || map.ContainsKey(header[i])) throw new InvalidDataException(path + ": empty or duplicate header " + header[i]);
                    bool known = false; foreach (var f in table.Fields) if (f != null && f.Name == header[i]) known = true;
                    if (!known) throw new InvalidDataException(path + ": unknown header " + header[i]); map.Add(header[i], i);
                }
                foreach (var field in table.Fields) {
                    if (field == null) throw new InvalidDataException(path + ": null field.");
                    if (!map.ContainsKey(field.Name) && !field.DefaultValue.HasValue) throw new InvalidDataException(path + ": missing required header " + field.Name);
                }
                var rows = new List<ConfigRow>();
                for (int r = 1; r < records.Count; r++) {
                    var record = records[r]; string at = path + ":" + record.Line;
                    if (record.Values.Count != header.Count) throw new InvalidDataException(at + ": row width mismatch.");
                    var values = new ConfigValue[table.Fields.Count];
                    for (int f = 0; f < values.Length; f++) {
                        var field = table.Fields[f];
                        if (map.TryGetValue(field.Name, out int index)) values[f] = Value(record.Values[index], field.Kind, at + "." + field.Name);
                    }
                    rows.Add(new ConfigRow(values, at));
                }
                result.Add(new ConfigTableData(table, rows));
            }
            if (consumed.Count != tables.Count) throw new InvalidDataException(sourceName + ": unexpected table input.");
            return new ConfigDocument(version, result);
        }
        static ConfigValue Value(string text, FieldKind kind, string at)
        {
            switch (kind) {
                case FieldKind.Int: if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) return ConfigValue.Int(i); break;
                case FieldKind.Long: if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l)) return ConfigValue.Long(l); break;
                case FieldKind.Float: if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) return ConfigValue.Float(f); break;
                case FieldKind.Bool: if (bool.TryParse(text, out bool b)) return ConfigValue.Bool(b); break;
                case FieldKind.String: return ConfigValue.String(text);
                case FieldKind.IntList:
                    if (text.Length == 0) return ConfigValue.Ints(Array.Empty<int>());
                    var parts = text.Split(';'); var values = new int[parts.Length];
                    for (int n = 0; n < parts.Length; n++) {
                        if (!int.TryParse(parts[n], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[n])) throw new InvalidDataException(at + ": expected semicolon-separated int32 list.");
                    }
                    return ConfigValue.Ints(values);
            }
            throw new InvalidDataException(at + ": expected " + kind + ".");
        }
        sealed class Record { internal List<string> Values; internal int Line; }
        static List<Record> ReadRecords(string text, string path)
        {
            if (text == null || text.Length > 32 * 1024 * 1024) throw new InvalidDataException(path + ": invalid CSV length.");
            var records = new List<Record>(); int p = text.Length > 0 && text[0] == '\uFEFF' ? 1 : 0, line = 1;
            while (p < text.Length) {
                int startLine = line; var cells = new List<string>(); bool end = false;
                while (!end) {
                    var cell = new StringBuilder(); bool quoted = p < text.Length && text[p] == '"';
                    if (quoted) {
                        p++; bool closed = false;
                        while (p < text.Length) {
                            char c = text[p++];
                            if (c == '"') {
                                if (p < text.Length && text[p] == '"') { cell.Append('"'); p++; }
                                else { closed = true; break; }
                            } else { cell.Append(c); if (c == '\n' || c == '\r' && (p >= text.Length || text[p] != '\n')) line++; }
                        }
                        if (!closed) throw new InvalidDataException(path + ":" + startLine + ": unterminated quoted field.");
                        if (p < text.Length && text[p] != ',' && text[p] != '\r' && text[p] != '\n') throw new InvalidDataException(path + ":" + line + ": unexpected character after closing quote.");
                    } else {
                        while (p < text.Length && text[p] != ',' && text[p] != '\r' && text[p] != '\n') {
                            if (text[p] == '"') throw new InvalidDataException(path + ":" + line + ": quote in unquoted field.");
                            cell.Append(text[p++]);
                        }
                    }
                    cells.Add(cell.ToString());
                    if (p >= text.Length) end = true;
                    else if (text[p] == ',') p++;
                    else { char c = text[p++]; if (c == '\r' && p < text.Length && text[p] == '\n') p++; line++; end = true; }
                }
                records.Add(new Record { Values = cells, Line = startLine });
            }
            return records;
        }
    }
}
