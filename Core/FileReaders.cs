using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace GestionGym
{
    public class SheetData
    {
        public string Name = "";
        public List<string[]> Rows = new List<string[]>();
        public bool Truncated;
    }

    /// <summary>Lecture de classeurs Excel (.xlsx / .xlsm / .xltx), OpenDocument (.ods) et CSV, sans aucune dépendance.</summary>
    public static class FileReaders
    {
        public const int MaxRows = 50000, MaxCols = 200;

        public static List<SheetData> Read(string path)
        {
            if (!File.Exists(path)) throw new InvalidOperationException("Le fichier n'existe plus : " + path);
            string ext = Path.GetExtension(path).ToLowerInvariant();
            FileInfo fi = new FileInfo(path);
            if (fi.Length > 150L * 1024 * 1024) throw new InvalidOperationException("Fichier trop volumineux (plus de 150 Mo).");
            switch (ext)
            {
                case ".xlsx":
                case ".xlsm":
                case ".xltx":
                case ".xltm":
                    return ReadXlsx(path);
                case ".ods":
                    return ReadOds(path);
                case ".csv":
                case ".txt":
                case ".tsv":
                    return ReadCsv(path);
                case ".xls":
                    throw new InvalidOperationException("Le format ancien .xls (Excel 97-2003) n'est pas lu directement.\nOuvre le fichier dans Excel, puis « Enregistrer sous » au format .xlsx (ou .csv) et réessaie.");
                case ".xlsb":
                    throw new InvalidOperationException("Le format .xlsb (Excel binaire) n'est pas lu directement.\nOuvre le fichier dans Excel, puis « Enregistrer sous » au format .xlsx (ou .csv) et réessaie.");
                default:
                    // on tente de deviner : un zip = classeur, sinon CSV
                    byte[] head = new byte[4];
                    using (var fs = File.OpenRead(path)) fs.Read(head, 0, 4);
                    if (head[0] == 0x50 && head[1] == 0x4B) { try { return ReadXlsx(path); } catch { return ReadOds(path); } }
                    return ReadCsv(path);
            }
        }

        static List<SheetData> ReadCsv(string path)
        {
            var sd = new SheetData { Name = Path.GetFileNameWithoutExtension(path) };
            List<string[]> rows = Csv.Read(path);
            foreach (string[] r in rows)
            {
                if (sd.Rows.Count >= MaxRows) { sd.Truncated = true; break; }
                sd.Rows.Add(r);
            }
            return new List<SheetData> { sd };
        }

        // ================================================================= XLSX
        static string Attr(XElement e, string name)
        {
            XAttribute a = e.Attribute(name);
            return a == null ? "" : a.Value;
        }

        static int ColIndex(string cellRef)
        {
            int n = 0, i = 0;
            while (i < cellRef.Length && char.IsLetter(cellRef[i])) { n = n * 26 + (char.ToUpperInvariant(cellRef[i]) - 'A' + 1); i++; }
            return n - 1;
        }

        static XDocument LoadXml(ZipArchive z, string name)
        {
            ZipArchiveEntry e = z.Entries.FirstOrDefault(x => x.FullName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (e == null) return null;
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using (Stream s = e.Open())
            using (XmlReader xr = XmlReader.Create(s, settings))
                return XDocument.Load(xr);
        }

        static bool IsDateFormat(int id, string code)
        {
            if ((id >= 14 && id <= 22) || (id >= 27 && id <= 36) || (id >= 45 && id <= 47) || (id >= 50 && id <= 58)) return true;
            if (string.IsNullOrEmpty(code)) return false;
            var sb = new StringBuilder();
            bool quote = false, bracket = false;
            foreach (char ch in code)
            {
                if (ch == '"') { quote = !quote; continue; }
                if (quote) continue;
                if (ch == '[') { bracket = true; continue; }
                if (ch == ']') { bracket = false; continue; }
                if (bracket) continue;
                sb.Append(char.ToLowerInvariant(ch));
            }
            string t = sb.ToString();
            return t.IndexOf('d') >= 0 || t.IndexOf('y') >= 0 || (t.IndexOf('m') >= 0 && (t.IndexOf('h') >= 0 || t.IndexOf('s') >= 0 || t.IndexOf('/') >= 0));
        }

        static List<SheetData> ReadXlsx(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var z = new ZipArchive(fs, ZipArchiveMode.Read))
                    return ReadXlsxZip(z);
            }
            catch (InvalidDataException)
            {
                throw new InvalidOperationException("Ce fichier n'est pas un classeur Excel valide (ou il est protégé par mot de passe / corrompu).");
            }
        }

        static List<SheetData> ReadXlsxZip(ZipArchive z)
        {
            XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XDocument wb = LoadXml(z, "xl/workbook.xml");
            if (wb == null) throw new InvalidOperationException("Ce fichier ne ressemble pas à un classeur Excel (xl/workbook.xml absent).");
            bool date1904 = false;
            XElement wpr = wb.Root.Element(main + "workbookPr");
            if (wpr != null) { string d = Attr(wpr, "date1904"); date1904 = d == "1" || d.ToLowerInvariant() == "true"; }
            // relations
            var targets = new Dictionary<string, string>();
            XDocument rels = LoadXml(z, "xl/_rels/workbook.xml.rels");
            if (rels != null)
                foreach (XElement r in rels.Root.Elements())
                {
                    string t = Attr(r, "Target");
                    if (t.StartsWith("/")) t = t.TrimStart('/'); else t = "xl/" + t;
                    targets[Attr(r, "Id")] = t;
                }
            // chaînes partagées
            var shared = new List<string>();
            XDocument ss = LoadXml(z, "xl/sharedStrings.xml");
            if (ss != null)
                foreach (XElement si in ss.Root.Elements(main + "si"))
                    shared.Add(string.Concat(si.Descendants(main + "t").Where(t => t.Parent == null || t.Parent.Name != main + "rPh").Select(t => t.Value)));
            // styles -> dates
            var dateStyle = new List<bool>();
            XDocument st = LoadXml(z, "xl/styles.xml");
            if (st != null)
            {
                var codes = new Dictionary<int, string>();
                XElement nf = st.Root.Element(main + "numFmts");
                if (nf != null) foreach (XElement f in nf.Elements(main + "numFmt")) { int id; if (int.TryParse(Attr(f, "numFmtId"), out id)) codes[id] = Attr(f, "formatCode"); }
                XElement xfs = st.Root.Element(main + "cellXfs");
                if (xfs != null)
                    foreach (XElement xf in xfs.Elements(main + "xf"))
                    {
                        int id; int.TryParse(Attr(xf, "numFmtId"), out id);
                        string code; codes.TryGetValue(id, out code);
                        dateStyle.Add(IsDateFormat(id, code));
                    }
            }
            var result = new List<SheetData>();
            XElement sheets = wb.Root.Element(main + "sheets");
            if (sheets == null) return result;
            foreach (XElement sh in sheets.Elements(main + "sheet"))
            {
                string rid = sh.Attribute(rel + "id") != null ? sh.Attribute(rel + "id").Value : "";
                string target;
                if (!targets.TryGetValue(rid, out target)) continue;
                var data = new SheetData { Name = Attr(sh, "name") };
                XDocument sx = LoadXml(z, target);
                if (sx == null) continue;
                XElement sd = sx.Root.Element(main + "sheetData");
                if (sd == null) { result.Add(data); continue; }
                int lastRow = 0;
                foreach (XElement row in sd.Elements(main + "row"))
                {
                    int rn;
                    if (!int.TryParse(Attr(row, "r"), out rn)) rn = lastRow + 1;
                    if (rn > MaxRows) { data.Truncated = true; break; }
                    while (data.Rows.Count < rn - 1) data.Rows.Add(new string[0]);
                    lastRow = rn;
                    var cells = new List<string>();
                    int col = 0;
                    foreach (XElement c in row.Elements(main + "c"))
                    {
                        string r = Attr(c, "r");
                        if (r.Length > 0) col = ColIndex(r);
                        if (col >= MaxCols) { col++; continue; }
                        while (cells.Count < col) cells.Add("");
                        cells.Add(CellValue(c, main, shared, dateStyle, date1904));
                        col++;
                    }
                    while (cells.Count > 0 && cells[cells.Count - 1].Length == 0) cells.RemoveAt(cells.Count - 1);
                    data.Rows.Add(cells.ToArray());
                }
                result.Add(data);
            }
            return result;
        }

        static string NumText(double d)
        {
            if (Math.Abs(d - Math.Round(d)) < 1e-9 && Math.Abs(d) < 1e15) return ((long)Math.Round(d)).ToString(CultureInfo.InvariantCulture);
            return d.ToString("0.###############", Fmt.Fr);
        }

        static string CellValue(XElement c, XNamespace main, List<string> shared, List<bool> dateStyle, bool date1904)
        {
            string t = Attr(c, "t");
            XElement v = c.Element(main + "v");
            if (t == "inlineStr")
            {
                XElement isel = c.Element(main + "is");
                return isel == null ? "" : string.Concat(isel.Descendants(main + "t").Select(x => x.Value));
            }
            if (v == null) return "";
            string raw = v.Value;
            if (t == "s")
            {
                int i;
                if (int.TryParse(raw, out i) && i >= 0 && i < shared.Count) return shared[i];
                return "";
            }
            if (t == "str") return raw;
            if (t == "b") return raw == "1" ? "Oui" : "Non";
            if (t == "e") return "";
            double d;
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return raw;
            int si;
            bool isDate = int.TryParse(Attr(c, "s"), out si) && si >= 0 && si < dateStyle.Count && dateStyle[si];
            if (isDate && d > 0 && d < 2958466)
            {
                try
                {
                    DateTime bas = date1904 ? new DateTime(1904, 1, 1) : new DateTime(1899, 12, 30);
                    DateTime dt = bas.AddDays(Math.Floor(d));
                    return dt.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
                }
                catch { }
            }
            return NumText(d);
        }

        // ================================================================= ODS
        static List<SheetData> ReadOds(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var z = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    XDocument doc = LoadXml(z, "content.xml");
                    if (doc == null) throw new InvalidOperationException("Ce fichier ne ressemble pas à un classeur OpenDocument (content.xml absent).");
                    XNamespace table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
                    XNamespace office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
                    XNamespace text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
                    var result = new List<SheetData>();
                    foreach (XElement t in doc.Descendants(table + "table"))
                    {
                        if (t.Parent != null && t.Parent.Name == table + "table-cell") continue;
                        var data = new SheetData { Name = Attr(t, table + "name") };
                        foreach (XElement row in t.Descendants(table + "table-row"))
                        {
                            int rep = RepAttr(row, table + "number-rows-repeated");
                            var cells = new List<string>();
                            foreach (XElement c in row.Elements().Where(e => e.Name == table + "table-cell" || e.Name == table + "covered-table-cell"))
                            {
                                int crep = RepAttr(c, table + "number-columns-repeated");
                                string val = OdsValue(c, office, text);
                                if (val.Length == 0) { if (cells.Count + crep > MaxCols) crep = Math.Max(0, MaxCols - cells.Count); }
                                else if (crep > 50) crep = 1;
                                for (int k = 0; k < crep && cells.Count < MaxCols; k++) cells.Add(val);
                            }
                            while (cells.Count > 0 && cells[cells.Count - 1].Length == 0) cells.RemoveAt(cells.Count - 1);
                            if (cells.Count == 0) rep = Math.Min(rep, 1);
                            for (int k = 0; k < rep; k++)
                            {
                                if (data.Rows.Count >= MaxRows) { data.Truncated = true; break; }
                                data.Rows.Add(cells.ToArray());
                            }
                            if (data.Truncated) break;
                        }
                        result.Add(data);
                    }
                    return result;
                }
            }
            catch (InvalidDataException)
            {
                throw new InvalidOperationException("Ce fichier n'est pas un classeur OpenDocument valide.");
            }
        }

        static string Attr(XElement e, XName name)
        {
            XAttribute a = e.Attribute(name);
            return a == null ? "" : a.Value;
        }

        static int RepAttr(XElement e, XName name)
        {
            int n;
            if (int.TryParse(Attr(e, name), out n) && n > 0) return n;
            return 1;
        }

        static string OdsValue(XElement c, XNamespace office, XNamespace text)
        {
            string vt = Attr(c, office + "value-type");
            if (vt == "float" || vt == "percentage" || vt == "currency")
            {
                double d;
                if (double.TryParse(Attr(c, office + "value"), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return NumText(d);
            }
            if (vt == "date")
            {
                string dv = Attr(c, office + "date-value");
                DateTime dt;
                if (dv.Length >= 10 && DateTime.TryParseExact(dv.Substring(0, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)) return dt.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            }
            if (vt == "boolean") return Attr(c, office + "boolean-value") == "true" ? "Oui" : "Non";
            return string.Join("\n", c.Elements(text + "p").Select(p => p.Value));
        }
    }
}
