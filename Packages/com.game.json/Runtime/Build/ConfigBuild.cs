using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GameJson
{
    public static class ConfigBuild
    {
        public static byte[] Compile(IConfigSource source) => Compile((source ?? throw new ArgumentNullException(nameof(source))).Read());
        public static string Generate(ConfigDocument document, string namespaceName, string className) => ConfigCodeWriter.Generate(document, namespaceName, className);
        public static byte[] Compile(ConfigDocument document)
        {
            var source = SourceDocument.Create(document);
            using var payload = new MemoryStream();
            using (var writer = new BinaryWriter(payload, Encoding.UTF8, true))
            {
                writer.Write(source.Version);
                writer.Write(source.Tables.Count);
                foreach (var table in source.Tables)
                {
                    writer.Write(table.Name);
                    writer.Write(table.Fields.Count);
                    writer.Write(table.Rows.Count);
                    foreach (var field in table.Fields)
                    {
                        writer.Write(field.Name);
                        writer.Write((byte)field.Kind);
                        writer.Write(field.Reference ?? "");
                        if (field.Kind == FieldKind.Bool) {
                            var bits = new byte[(table.Rows.Count + 7) / 8];
                            for (int i = 0; i < table.Rows.Count; i++) if ((bool)table.Rows[i][field.Index]) bits[i >> 3] |= (byte)(1 << (i & 7));
                            writer.Write(bits); continue;
                        }
                        var strings = new Dictionary<string, int>(StringComparer.Ordinal);
                        var pool = new List<string>();
                        var values = new List<int>();
                        foreach (var row in table.Rows)
                        {
                            var cell = row[field.Index];
                            if (field.Kind == FieldKind.Int) writer.Write((int)cell);
                            else if (field.Kind == FieldKind.Long) writer.Write((long)cell);
                            else if (field.Kind == FieldKind.Float) writer.Write((float)cell);
                            else if (field.Kind == FieldKind.String)
                            {
                                string s = (string)cell;
                                if (!strings.TryGetValue(s, out int id))
                                { id = pool.Count; strings.Add(s, id); pool.Add(s); }
                                writer.Write(id);
                            }
                            else
                            {
                                var arr = cell.AsInts();
                                writer.Write(values.Count); writer.Write(arr.Length);
                                if ((long)values.Count + arr.Length > 8000000) throw new InvalidDataException(table.Name + "." + field.Name + ": list storage exceeds limit.");
                                foreach (int value in arr) values.Add(value);
                            }
                        }
                        if (field.Kind == FieldKind.String)
                        { writer.Write(pool.Count); foreach (string value in pool) writer.Write(value); }
                        if (field.Kind == FieldKind.IntList)
                        { writer.Write(values.Count); foreach (int value in values) writer.Write(value); }
                    }
                }
            }
            byte[] body = payload.ToArray();
            using var sha = SHA256.Create();
            using var output = new MemoryStream();
            using var envelope = new BinaryWriter(output);
            if (body.Length > ConfigFormat.MaxBinaryBytes - 40) throw new InvalidDataException("Compiled configuration exceeds 64 MiB.");
            envelope.Write(ConfigFormat.Magic); envelope.Write(body.Length); envelope.Write(sha.ComputeHash(body)); envelope.Write(body);
            return output.ToArray();
        }
    }
}
