using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Game.Config.Json")]
namespace GameJson
{
    public interface IConfigSource { ConfigDocument Read(); }

    // Build-time values, not runtime row objects. Default(ConfigValue) means missing.
    public readonly struct ConfigValue
    {
        public FieldKind Kind { get; }
        readonly long integer;
        readonly float real;
        readonly object data;
        ConfigValue(FieldKind kind, long integer = 0, float real = 0, object data = null)
        { Kind = kind; this.integer = integer; this.real = real; this.data = data; }
        public static ConfigValue Int(int value) => new ConfigValue(FieldKind.Int, value);
        public static ConfigValue Long(long value) => new ConfigValue(FieldKind.Long, value);
        public static ConfigValue Float(float value) => new ConfigValue(FieldKind.Float, real: value);
        public static ConfigValue Bool(bool value) => new ConfigValue(FieldKind.Bool, value ? 1 : 0);
        public static ConfigValue String(string value) => new ConfigValue(FieldKind.String, data: value ?? throw new ArgumentNullException(nameof(value)));
        public static ConfigValue Ints(IEnumerable<int> values) => new ConfigValue(FieldKind.IntList,
            data: new List<int>(values ?? throw new ArgumentNullException(nameof(values))).ToArray());
        void Require(FieldKind kind) { if (Kind != kind) throw new InvalidOperationException("Expected " + kind + ", got " + Kind); }
        public int AsInt() { Require(FieldKind.Int); return (int)integer; }
        public long AsLong() { Require(FieldKind.Long); return integer; }
        public float AsFloat() { Require(FieldKind.Float); return real; }
        public bool AsBool() { Require(FieldKind.Bool); return integer != 0; }
        public string AsString() { Require(FieldKind.String); return (string)data; }
        public ReadOnlySpan<int> AsInts() { Require(FieldKind.IntList); return (int[])data; }
        public static explicit operator int(ConfigValue v) => v.AsInt();
        public static explicit operator long(ConfigValue v) => v.AsLong();
        public static explicit operator float(ConfigValue v) => v.AsFloat();
        public static explicit operator bool(ConfigValue v) => v.AsBool();
        public static explicit operator string(ConfigValue v) => v.AsString();
    }
    public sealed class ConfigField
    {
        public string Name { get; }
        public FieldKind Kind { get; }
        public string Reference { get; }
        public ConfigValue? DefaultValue { get; }
        public ConfigField(string name, FieldKind kind, string reference = null, ConfigValue? defaultValue = null)
        { Name = name; Kind = kind; Reference = reference; DefaultValue = defaultValue; }
    }
    public sealed class ConfigTableSchema
    {
        public string Name { get; }
        public IReadOnlyList<ConfigField> Fields { get; }
        public ConfigTableSchema(string name, IEnumerable<ConfigField> fields)
        { Name = name; Fields = ModelCopy.List(fields); }
    }
    public sealed class ConfigSchema
    {
        public IReadOnlyList<ConfigTableSchema> Tables { get; }
        public ConfigSchema(IEnumerable<ConfigTableSchema> tables) { Tables = ModelCopy.List(tables); }
    }
    public sealed class ConfigRow
    {
        public IReadOnlyList<ConfigValue> Values { get; }
        public string Location { get; }
        public ConfigRow(IEnumerable<ConfigValue> values, string location = null)
        { Values = ModelCopy.List(values); Location = location; }
    }
    public sealed class ConfigTableData
    {
        public ConfigTableSchema Schema { get; }
        public IReadOnlyList<ConfigRow> Rows { get; }
        public ConfigTableData(ConfigTableSchema schema, IEnumerable<ConfigRow> rows)
        { Schema = schema ?? throw new ArgumentNullException(nameof(schema)); Rows = ModelCopy.List(rows); }
    }
    public sealed class ConfigDocument
    {
        public int Version { get; }
        public IReadOnlyList<ConfigTableData> Tables { get; }
        public ConfigDocument(int version, IEnumerable<ConfigTableData> tables)
        { Version = version; Tables = ModelCopy.List(tables); }
    }
    static class ModelCopy
    {
        internal static ReadOnlyCollection<T> List<T>(IEnumerable<T> values) =>
            new List<T>(values ?? throw new ArgumentNullException(nameof(values))).AsReadOnly();
    }
}
