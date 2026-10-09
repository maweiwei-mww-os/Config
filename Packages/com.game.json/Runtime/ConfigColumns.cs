using System;

namespace GameJson
{
    public static class ConfigContract
    {
        public static void RequireReference(IConfigData data, string table, string field, string expectedTarget)
        {
            if (!(data is IConfigSchema schema) || !schema.HasReferenceMetadata)
                throw new System.IO.InvalidDataException(table + "." + field + ": reference metadata unavailable; recompile with Game-Json 0.2 or later.");
            string actual = schema.ReferenceTarget(table, field);
            if (!string.Equals(actual, expectedTarget, StringComparison.Ordinal))
                throw new System.IO.InvalidDataException(table + "." + field + ": expected reference " + expectedTarget + ", received " + (actual ?? "none"));
        }
    }
    // Optional fast path: generated code can also bind any existing IConfigTable implementation.
    public interface IConfigColumnSource
    {
        IntColumn BindInt(string name);
        StringColumn BindString(string name);
        IntListColumn BindIntList(string name);
    }
    public interface IConfigScalarColumnSource
    {
        LongColumn BindLong(string name);
        FloatColumn BindFloat(string name);
        BoolColumn BindBool(string name);
    }
    public static class ConfigColumns
    {
        public static IntColumn Int(IConfigTable table, string name) => table is IConfigColumnSource columns
            ? columns.BindInt(name) : new IntColumn(table, table.Bind(name, FieldKind.Int));
        public static StringColumn String(IConfigTable table, string name) => table is IConfigColumnSource columns
            ? columns.BindString(name) : new StringColumn(table, table.Bind(name, FieldKind.String));
        public static IntListColumn List(IConfigTable table, string name) => table is IConfigColumnSource columns
            ? columns.BindIntList(name) : new IntListColumn(table, table.Bind(name, FieldKind.IntList));
        public static LongColumn Long(IConfigTable table, string name) => table is IConfigScalarColumnSource columns
            ? columns.BindLong(name) : new LongColumn(Primitives(table), table.Bind(name, FieldKind.Long));
        public static FloatColumn Float(IConfigTable table, string name) => table is IConfigScalarColumnSource columns
            ? columns.BindFloat(name) : new FloatColumn(Primitives(table), table.Bind(name, FieldKind.Float));
        public static BoolColumn Bool(IConfigTable table, string name) => table is IConfigScalarColumnSource columns
            ? columns.BindBool(name) : new BoolColumn(Primitives(table), table.Bind(name, FieldKind.Bool));
        static IConfigPrimitives Primitives(IConfigTable table) => table as IConfigPrimitives ?? throw new NotSupportedException("Backend does not implement extended scalars.");
    }
    public readonly struct IntColumn
    {
        readonly int[] values;
        readonly IConfigTable fallback;
        readonly int field;
        internal IntColumn(int[] values) { this.values = values; fallback = null; field = 0; }
        internal IntColumn(IConfigTable table, int field) { values = null; fallback = table; this.field = field; }
        public int this[int row] => values != null ? values[row] : fallback.Int(row, field);
        public bool TryGetSpan(out ReadOnlySpan<int> span) { span = values; return values != null; }
    }
    public readonly struct StringColumn
    {
        readonly int[] indices;
        readonly string[] strings;
        readonly IConfigTable fallback;
        readonly int field;
        internal StringColumn(int[] indices, string[] strings) { this.indices = indices; this.strings = strings; fallback = null; field = 0; }
        internal StringColumn(IConfigTable table, int field) { indices = null; strings = null; fallback = table; this.field = field; }
        public string this[int row] => indices != null ? strings[indices[row]] : fallback.Text(row, field);
    }
    public readonly struct IntListColumn
    {
        readonly int[] slices, items;
        readonly IConfigTable fallback;
        readonly int field;
        internal IntListColumn(int[] slices, int[] items) { this.slices = slices; this.items = items; fallback = null; field = 0; }
        internal IntListColumn(IConfigTable table, int field) { slices = null; items = null; fallback = table; this.field = field; }
        public IntList this[int row]
        {
            get {
                if (slices == null) return fallback.List(row, field);
                if ((uint)row >= (uint)(slices.Length / 2)) throw new ArgumentOutOfRangeException(nameof(row));
                return new IntList(items, slices[row * 2], slices[row * 2 + 1]);
            }
        }
    }
    public readonly struct LongColumn
    {
        readonly long[] values;
        readonly IConfigPrimitives fallback;
        readonly int field;
        internal LongColumn(long[] values) { this.values = values; fallback = null; field = 0; }
        internal LongColumn(IConfigPrimitives table, int field) { values = null; fallback = table; this.field = field; }
        public long this[int row] => values != null ? values[row] : fallback.Long(row, field);
        public bool TryGetSpan(out ReadOnlySpan<long> span) { span = values; return values != null; }
    }
    public readonly struct FloatColumn
    {
        readonly float[] values;
        readonly IConfigPrimitives fallback;
        readonly int field;
        internal FloatColumn(float[] values) { this.values = values; fallback = null; field = 0; }
        internal FloatColumn(IConfigPrimitives table, int field) { values = null; fallback = table; this.field = field; }
        public float this[int row] => values != null ? values[row] : fallback.Float(row, field);
        public bool TryGetSpan(out ReadOnlySpan<float> span) { span = values; return values != null; }
    }
    public readonly struct BoolColumn
    {
        readonly byte[] bits;
        readonly IConfigPrimitives fallback;
        readonly int count, field;
        internal BoolColumn(byte[] bits, int count) { this.bits = bits; this.count = count; fallback = null; field = 0; }
        internal BoolColumn(IConfigPrimitives table, int field) { bits = null; count = 0; fallback = table; this.field = field; }
        public bool this[int row] {
            get {
                if (bits == null) return fallback.Bool(row, field);
                if ((uint)row >= (uint)count) throw new ArgumentOutOfRangeException(nameof(row));
                return (bits[row >> 3] & (1 << (row & 7))) != 0;
            }
        }
    }
}
