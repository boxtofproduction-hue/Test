using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GestionGym
{
    /// <summary>Lecture / écriture CSV (séparateur point-virgule, UTF-8 avec BOM, lisible directement dans Excel).</summary>
    public static class Csv
    {
        public const char Sep = ';';

        public static List<string[]> Parse(string text)
        {
            var rows = new List<string[]>();
            if (string.IsNullOrEmpty(text)) return rows;
            if (text[0] == '﻿') text = text.Substring(1);
            char sep = DetectSeparator(text);
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool inQ = false;
            int i = 0, n = text.Length;
            while (i < n)
            {
                char c = text[i];
                if (inQ)
                {
                    if (c == '"')
                    {
                        if (i + 1 < n && text[i + 1] == '"') { sb.Append('"'); i += 2; continue; }
                        inQ = false; i++; continue;
                    }
                    sb.Append(c); i++; continue;
                }
                if (c == '"' && sb.Length == 0) { inQ = true; i++; continue; }
                if (c == sep) { fields.Add(sb.ToString()); sb.Clear(); i++; continue; }
                if (c == '\r') { i++; continue; }
                if (c == '\n')
                {
                    fields.Add(sb.ToString()); sb.Clear();
                    AddRow(rows, fields);
                    fields = new List<string>();
                    i++; continue;
                }
                sb.Append(c); i++;
            }
            if (sb.Length > 0 || fields.Count > 0)
            {
                fields.Add(sb.ToString());
                AddRow(rows, fields);
            }
            return rows;
        }

        static void AddRow(List<string[]> rows, List<string> f)
        {
            if (f.Count == 1 && f[0].Trim().Length == 0) return; // ligne vide
            rows.Add(f.ToArray());
        }

        static char DetectSeparator(string text)
        {
            int end = text.IndexOf('\n');
            if (end < 0) end = text.Length;
            int semi = 0, comma = 0, tab = 0;
            bool inQ = false;
            for (int i = 0; i < end; i++)
            {
                char c = text[i];
                if (c == '"') inQ = !inQ;
                else if (!inQ)
                {
                    if (c == ';') semi++;
                    else if (c == ',') comma++;
                    else if (c == '\t') tab++;
                }
            }
            if (semi >= comma && semi >= tab) return ';';
            if (tab >= comma) return '\t';
            return ',';
        }

        public static string Escape(string s)
        {
            if (s == null) return "";
            bool need = s.IndexOfAny(new[] { ';', '"', ',', '\n', '\r', '\t' }) >= 0 || s.StartsWith(" ") || s.EndsWith(" ");
            if (!need) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        public static string Line(IEnumerable<string> fields)
        {
            var sb = new StringBuilder();
            bool first = true;
            foreach (string f in fields)
            {
                if (!first) sb.Append(Sep);
                sb.Append(Escape(f));
                first = false;
            }
            return sb.ToString();
        }

        public static string Decode(byte[] bytes)
        {
            try { return new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException) { return Encoding.GetEncoding(1252).GetString(bytes); }
        }

        public static List<string[]> Read(string path)
        {
            if (!File.Exists(path)) return new List<string[]>();
            return Parse(Decode(File.ReadAllBytes(path)));
        }

        /// <summary>Écriture atomique : fichier temporaire puis remplacement, pour ne jamais laisser un fichier à moitié écrit.</summary>
        public static void Write(string path, IEnumerable<string[]> rows)
        {
            string tmp = path + ".tmp";
            using (var sw = new StreamWriter(tmp, false, new UTF8Encoding(true)))
            {
                foreach (string[] r in rows)
                {
                    sw.Write(Line(r));
                    sw.Write("\r\n");
                }
            }
            try
            {
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch (IOException)
            {
                File.Copy(tmp, path, true);
                File.Delete(tmp);
            }
        }
    }

    /// <summary>Table CSV avec en-tête : accès aux colonnes par leur nom, insensible à la casse et aux accents.</summary>
    public class CsvTable
    {
        readonly Dictionary<string, int> idx = new Dictionary<string, int>();
        public List<string[]> Rows = new List<string[]>();
        public bool HasHeader;

        public CsvTable(List<string[]> all)
        {
            if (all.Count == 0) return;
            string[] h = all[0];
            for (int i = 0; i < h.Length; i++)
            {
                string k = Fmt.Norm(h[i]).Trim();
                if (k.Length > 0 && !idx.ContainsKey(k)) idx[k] = i;
            }
            HasHeader = idx.Count > 0;
            for (int i = 1; i < all.Count; i++) Rows.Add(all[i]);
        }

        public bool Has(string col) { return idx.ContainsKey(Fmt.Norm(col)); }

        public string Get(string[] row, string col)
        {
            int i;
            if (!idx.TryGetValue(Fmt.Norm(col), out i) || i >= row.Length) return "";
            return row[i].Trim();
        }
    }
}
