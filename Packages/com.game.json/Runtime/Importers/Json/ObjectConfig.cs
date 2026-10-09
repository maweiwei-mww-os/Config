using System;
using System.Collections.Generic;

namespace GameJson
{
    // Reference backend: shared field metadata, row dictionaries, boxed scalars and eager ID indexes.
    public sealed class ObjectConfig : IConfigData, IConfigSchema
    {
        readonly Dictionary<string, IConfigTable> tables = new Dictionary<string, IConfigTable>(StringComparer.Ordinal);
        public int Version { get; private set; }
        public bool HasReferenceMetadata => true;
        public string ReferenceTarget(string table, string field) => ((ObjectTable)Table(table)).ReferenceTarget(field);
        public IConfigTable Table(string name) => tables[name];
        public static ObjectConfig Load(string json)
        {
            var source = SourceDocument.Create(new JsonConfigSource(json).Read());
            var config = new ObjectConfig { Version = source.Version };
            foreach (var t in source.Tables) config.tables.Add(t.Name, new ObjectTable(t));
            return config;
        }
        sealed class ObjectTable : IConfigTable, IConfigPrimitives
        {
            readonly string[] names;
            readonly string[] references;
            readonly FieldKind[] kinds;
            readonly Dictionary<string, object>[] rows;
            readonly Dictionary<int, int> index = new Dictionary<int, int>();
            public int Count => rows.Length;
            internal ObjectTable(SourceTable source)
            {
                names = new string[source.Fields.Count]; kinds = new FieldKind[names.Length]; references = new string[names.Length];
                rows = new Dictionary<string, object>[source.Rows.Count];
                foreach (var f in source.Fields) { names[f.Index] = f.Name; kinds[f.Index] = f.Kind; references[f.Index] = f.Reference; }
                for (int r = 0; r < rows.Length; r++)
                {
                    var row = source.Rows[r]; var data = new Dictionary<string, object>(names.Length, StringComparer.Ordinal);
                    for (int f = 0; f < names.Length; f++)
                    {
                        object value;
                        if (kinds[f] == FieldKind.Int) value = (int)row[f];
                        else if (kinds[f] == FieldKind.Long) value = (long)row[f];
                        else if (kinds[f] == FieldKind.Float) value = (float)row[f];
                        else if (kinds[f] == FieldKind.Bool) value = (bool)row[f];
                        else if (kinds[f] == FieldKind.String) value = (string)row[f];
                        else { var list = row[f].AsInts(); var a = new int[list.Length]; for (int i = 0; i < a.Length; i++) a[i] = (int)list[i]; value = a; }
                        data.Add(names[f], value);
                    }
                    rows[r] = data; index.Add((int)row[0], r);
                }
            }
            public int Find(int id) => index.TryGetValue(id, out int r) ? r : -1;
            internal string ReferenceTarget(string field) { int f = Array.IndexOf(names, field); return f >= 0 ? references[f] : throw new KeyNotFoundException(field); }
            public int Bind(string field, FieldKind kind)
            { int f = Array.IndexOf(names, field); if (f < 0 || kinds[f] != kind) throw new ArgumentException("Missing/wrong field: " + field); return f; }
            public int Int(int row, int field) => (int)rows[row][names[field]];
            public string Text(int row, int field) => (string)rows[row][names[field]];
            public IntList List(int row, int field) { var a = (int[])rows[row][names[field]]; return new IntList(a, 0, a.Length); }
            public long Long(int row, int field) => (long)rows[row][names[field]];
            public float Float(int row, int field) => (float)rows[row][names[field]];
            public bool Bool(int row, int field) => (bool)rows[row][names[field]];
        }
    }
}
