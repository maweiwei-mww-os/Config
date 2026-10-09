using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GameJson
{
    public enum ConfigIndexPolicy { FastLookup, Compact }
    public enum FieldKind : byte { Int = 1, String = 2, IntList = 3, Long = 4, Float = 5, Bool = 6 }
    public readonly struct IntList
    {
        readonly int[] data;
        readonly int offset;
        public int Count { get; }
        internal IntList(int[] data, int offset, int count) { this.data = data; this.offset = offset; Count = count; }
        public int this[int index] => (uint)index < (uint)Count ? data[offset + index] : throw new ArgumentOutOfRangeException(nameof(index));
        public Enumerator GetEnumerator() => new Enumerator(this);
        public struct Enumerator
        {
            readonly IntList list;
            int index;
            internal Enumerator(IntList list) { this.list = list; index = -1; }
            public int Current => list[index];
            public bool MoveNext() { if (index < list.Count) index++; return index < list.Count; }
        }
    }
    public interface IConfigTable
    {
        int Count { get; }
        int Find(int id);
        int Bind(string field, FieldKind kind);
        int Int(int row, int field);
        string Text(int row, int field);
        IntList List(int row, int field);
    }
    public interface IConfigData
    {
        int Version { get; }
        IConfigTable Table(string name);
    }
    public interface IConfigPrimitives
    {
        long Long(int row, int field);
        float Float(int row, int field);
        bool Bool(int row, int field);
    }
    public interface IConfigSchema
    {
        bool HasReferenceMetadata { get; }
        string ReferenceTarget(string table, string field);
    }
    public sealed class CompactConfig : IConfigData, IConfigSchema
    {
        readonly Dictionary<string, IConfigTable> tables = new Dictionary<string, IConfigTable>(StringComparer.Ordinal);
        public int Version { get; private set; }
        public bool HasReferenceMetadata { get; private set; }
        public string ReferenceTarget(string table, string field) => ((CompactTable)Table(table)).ReferenceTarget(field);
        public IConfigTable Table(string name) => tables.TryGetValue(name, out var table) ? table : throw new KeyNotFoundException(name);
        public static CompactConfig Load(byte[] bytes) => Load(bytes, ConfigIndexPolicy.FastLookup);
        public static CompactConfig Load(byte[] bytes, ConfigIndexPolicy indexPolicy)
        {
            if (indexPolicy != ConfigIndexPolicy.FastLookup && indexPolicy != ConfigIndexPolicy.Compact) throw new ArgumentOutOfRangeException(nameof(indexPolicy));
            if (bytes == null || bytes.Length < 48 || bytes.Length > 64 * 1024 * 1024) throw new InvalidDataException("Invalid config length.");
            using var stream = new MemoryStream(bytes, false);
            using var reader = new BinaryReader(stream, new UTF8Encoding(false, true));
            int magic = reader.ReadInt32();
            if ((magic != ConfigFormat.Magic && magic != ConfigFormat.LegacyMagic) || reader.ReadInt32() != bytes.Length - 40) throw new InvalidDataException("Invalid config header.");
            byte[] expected = reader.ReadBytes(32);
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(bytes, 40, bytes.Length - 40);
            for (int i = 0; i < 32; i++) if (expected[i] != hash[i]) throw new InvalidDataException("Config checksum mismatch.");
            var result = new CompactConfig { Version = reader.ReadInt32(), HasReferenceMetadata = magic == ConfigFormat.Magic };
            if (result.Version < 1) throw new InvalidDataException("Invalid config version.");
            int count = Count(reader, 128, 1);
            for (int t = 0; t < count; t++)
            {
                string name = reader.ReadString();
                if (name.Length == 0 || name.Length > 128 || result.tables.ContainsKey(name)) throw new InvalidDataException("Invalid table name.");
                result.tables.Add(name, CompactTable.Read(reader, result.HasReferenceMetadata, indexPolicy));
            }
            if (stream.Position != stream.Length) throw new InvalidDataException("Trailing config data.");
            foreach (var entry in result.tables) ((CompactTable)entry.Value).ValidateReferences(result, entry.Key);
            return result;
        }
        internal static int Count(BinaryReader reader, int maximum, int minimum = 0)
        {
            int n = reader.ReadInt32();
            if (n < minimum || n > maximum) throw new InvalidDataException("Invalid element count.");
            return n;
        }
        internal static int[] Ints(BinaryReader reader, int count)
        {
            if (count < 0 || count > (reader.BaseStream.Length - reader.BaseStream.Position) / 4) throw new InvalidDataException("Truncated column.");
            var arr = new int[count];
            for (int i = 0; i < count; i++) arr[i] = reader.ReadInt32();
            return arr;
        }
    }
    internal sealed class CompactTable : IConfigTable, IConfigColumnSource, IConfigPrimitives, IConfigScalarColumnSource
    {
        internal sealed class Column
        {
            internal string Name, Reference;
            internal FieldKind Kind;
            internal int[] Values, Items;
            internal string[] Strings;
            internal long[] Longs;
            internal float[] Floats;
            internal byte[] Bits;
        }
        Column[] columns;
        int[] dense;
        Dictionary<int, int> sparse;
        bool contiguous;
        int minimum;
        public int Count { get; private set; }
        internal static CompactTable Read(BinaryReader reader, bool referenceMetadata, ConfigIndexPolicy indexPolicy)
        {
            int n = CompactConfig.Count(reader, 128, 1);
            var table = new CompactTable { Count = CompactConfig.Count(reader, 1000000), columns = new Column[n] };
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int f = 0; f < n; f++)
            {
                var col = new Column { Name = reader.ReadString(), Kind = (FieldKind)reader.ReadByte() };
                if (referenceMetadata) { string reference = reader.ReadString(); col.Reference = reference.Length == 0 ? null : reference; }
                if (col.Name.Length == 0 || col.Name.Length > 128 || !names.Add(col.Name) || col.Kind < FieldKind.Int || col.Kind > FieldKind.Bool)
                    throw new InvalidDataException("Invalid column descriptor.");
                if (col.Reference != null && (col.Reference.Length > 128 || (col.Kind != FieldKind.Int && col.Kind != FieldKind.IntList))) throw new InvalidDataException("Invalid reference descriptor.");
                if (col.Kind == FieldKind.Long) {
                    RequireBytes(reader, (long)table.Count * 8);
                    col.Longs = new long[table.Count]; for (int i = 0; i < table.Count; i++) col.Longs[i] = reader.ReadInt64();
                } else if (col.Kind == FieldKind.Float) {
                    RequireBytes(reader, (long)table.Count * 4);
                    col.Floats = new float[table.Count];
                    for (int i = 0; i < table.Count; i++) {
                        float value = reader.ReadSingle();
                        if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidDataException("Non-finite float column.");
                        col.Floats[i] = value;
                    }
                } else if (col.Kind == FieldKind.Bool) {
                    int length = (table.Count + 7) / 8; RequireBytes(reader, length); col.Bits = reader.ReadBytes(length);
                    int tail = table.Count & 7;
                    if (tail != 0 && (col.Bits[length - 1] & ~((1 << tail) - 1)) != 0) throw new InvalidDataException("Nonzero boolean padding.");
                } else col.Values = CompactConfig.Ints(reader, checked(table.Count * (col.Kind == FieldKind.IntList ? 2 : 1)));
                if (col.Kind == FieldKind.String)
                {
                    int size = CompactConfig.Count(reader, table.Count);
                    col.Strings = new string[size];
                    for (int i = 0; i < size; i++) col.Strings[i] = reader.ReadString();
                    foreach (int id in col.Values) if ((uint)id >= (uint)size) throw new InvalidDataException("Invalid string index.");
                }
                if (col.Kind == FieldKind.IntList)
                {
                    col.Items = CompactConfig.Ints(reader, CompactConfig.Count(reader, 8000000));
                    for (int i = 0; i < col.Values.Length; i += 2)
                        if (col.Values[i] < 0 || col.Values[i+1] < 0 || (long)col.Values[i] + col.Values[i+1] > col.Items.Length)
                            throw new InvalidDataException("Invalid list slice.");
                }
                table.columns[f] = col;
            }
            if (table.columns[0].Name != "id" || table.columns[0].Kind != FieldKind.Int) throw new InvalidDataException("Missing id column.");
            int[] ids = table.columns[0].Values;
            for (int i = 0; i < ids.Length; i++)
                if (ids[i] < 0 || (i > 0 && ids[i] <= ids[i-1])) throw new InvalidDataException("Unsorted or duplicate ids.");
            if (ids.Length > 0)
            {
                table.minimum = ids[0];
                long range = (long)ids[ids.Length - 1] - ids[0] + 1;
                if (range == ids.Length) table.contiguous = true;
                else if (range <= ids.Length * 2L && range <= 2000000)
                {
                    table.dense = new int[(int)range];
                    for (int i = 0; i < table.dense.Length; i++) table.dense[i] = -1;
                    for (int i = 0; i < ids.Length; i++) table.dense[ids[i] - ids[0]] = i;
                }
                else if (indexPolicy == ConfigIndexPolicy.FastLookup) {
                    table.sparse = new Dictionary<int, int>(ids.Length);
                    for (int i = 0; i < ids.Length; i++) table.sparse.Add(ids[i], i);
                }
            }
            return table;
        }
        static void RequireBytes(BinaryReader reader, long count)
        { if (count > reader.BaseStream.Length - reader.BaseStream.Position) throw new InvalidDataException("Truncated scalar column."); }
        internal string ReferenceTarget(string field)
        {
            foreach (var column in columns) if (column.Name == field) return column.Reference;
            throw new KeyNotFoundException(field);
        }
        internal void ValidateReferences(CompactConfig owner, string name)
        {
            foreach (var column in columns) {
                if (column.Reference == null) continue;
                IConfigTable target;
                try { target = owner.Table(column.Reference); }
                catch (KeyNotFoundException e) { throw new InvalidDataException(name + "." + column.Name + ": missing table " + column.Reference, e); }
                for (int row = 0; row < Count; row++) {
                    if (column.Kind == FieldKind.Int) Require(target, column.Values[row], name, columns[0].Values[row], column);
                    else for (int i = 0; i < column.Values[row * 2 + 1]; i++) Require(target, column.Items[column.Values[row * 2] + i], name, columns[0].Values[row], column);
                }
            }
        }
        static void Require(IConfigTable target, int id, string table, int row, Column column)
        { if (target.Find(id) < 0) throw new InvalidDataException(table + "[" + row + "]." + column.Name + ": missing " + column.Reference + "[" + id + "]"); }
        public int Find(int id)
        {
            if (contiguous) { long p = (long)id - minimum; return p >= 0 && p < Count ? (int)p : -1; }
            if (dense != null) { long p = (long)id - minimum; return p >= 0 && p < dense.Length ? dense[(int)p] : -1; }
            if (sparse != null) return sparse.TryGetValue(id, out int row) ? row : -1;
            int i = Array.BinarySearch(columns[0].Values, id); return i >= 0 ? i : -1;
        }
        public int Bind(string field, FieldKind kind)
        {
            for (int f = 0; f < columns.Length; f++)
                if (columns[f].Name == field)
                { if (columns[f].Kind != kind) throw new InvalidDataException("Field type mismatch: " + field); return f; }
            throw new KeyNotFoundException(field);
        }
        Column Get(int row, int field, FieldKind kind)
        {
            if ((uint)row >= (uint)Count || (uint)field >= (uint)columns.Length) throw new ArgumentOutOfRangeException();
            var c = columns[field]; if (c.Kind != kind) throw new InvalidDataException("Wrong column type."); return c;
        }
        public int Int(int row, int field) => Get(row, field, FieldKind.Int).Values[row];
        public string Text(int row, int field) { var c = Get(row, field, FieldKind.String); return c.Strings[c.Values[row]]; }
        public IntList List(int row, int field) { var c = Get(row, field, FieldKind.IntList); return new IntList(c.Items, c.Values[row*2], c.Values[row*2+1]); }
        public long Long(int row, int field) => Get(row, field, FieldKind.Long).Longs[row];
        public float Float(int row, int field) => Get(row, field, FieldKind.Float).Floats[row];
        public bool Bool(int row, int field) => (Get(row, field, FieldKind.Bool).Bits[row >> 3] & (1 << (row & 7))) != 0;
        public IntColumn BindInt(string name) => new IntColumn(columns[Bind(name, FieldKind.Int)].Values);
        public StringColumn BindString(string name)
        { var c = columns[Bind(name, FieldKind.String)]; return new StringColumn(c.Values, c.Strings); }
        public IntListColumn BindIntList(string name)
        { var c = columns[Bind(name, FieldKind.IntList)]; return new IntListColumn(c.Values, c.Items); }
        public LongColumn BindLong(string name) => new LongColumn(columns[Bind(name, FieldKind.Long)].Longs);
        public FloatColumn BindFloat(string name) => new FloatColumn(columns[Bind(name, FieldKind.Float)].Floats);
        public BoolColumn BindBool(string name) => new BoolColumn(columns[Bind(name, FieldKind.Bool)].Bits, Count);
    }
}
