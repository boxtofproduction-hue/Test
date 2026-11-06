using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace GestionGym
{
    public static class St
    {
        public const int Title = 1, Header = 2, Text = 3, Num = 4, Int = 5, Date = 6, Dec1 = 7, TotText = 8, TotNum = 9, Note = 10,
            PairLabel = 11, PairValue = 12, BlockTitle = 13, PairWarn = 14, TotInt = 15, Sub = 16;
    }

    public class LCell
    {
        public object Value;
        public int Style;
    }

    public class ChartPlace
    {
        public Block Block;
        public int HeaderRow, FirstRow, LastRow;    // plage de données (lignes, 0-based)
        public int SeriesCount;
        public int AnchorRow, AnchorCol;
        public int Number;                          // numéro de graphique dans le classeur
    }

    /// <summary>Mise en page commune (Excel et OpenOffice) d'une feuille : cellules, styles, graphiques, largeurs.</summary>
    public class SheetLayout
    {
        public Sheet Sheet;
        public string Name;
        public readonly SortedDictionary<int, SortedDictionary<int, LCell>> Cells = new SortedDictionary<int, SortedDictionary<int, LCell>>();
        public readonly List<ChartPlace> Charts = new List<ChartPlace>();
        public readonly Dictionary<int, double> ColChars = new Dictionary<int, double>();
        public int MaxRow = -1, MaxCol = 0;
        public const int ChartRows = 18, ChartCols = 8;

        void Put(int r, int c, object v, int style)
        {
            SortedDictionary<int, LCell> row;
            if (!Cells.TryGetValue(r, out row)) { row = new SortedDictionary<int, LCell>(); Cells[r] = row; }
            row[c] = new LCell { Value = v, Style = style };
            if (r > MaxRow) MaxRow = r;
            if (c > MaxCol) MaxCol = c;
        }

        void Width(int c, double chars)
        {
            double cur;
            if (!ColChars.TryGetValue(c, out cur) || cur < chars) ColChars[c] = Math.Min(chars, 70);
        }

        static string FormatOf(string[] f, int i) { return f != null && i < f.Length ? f[i] : null; }

        public static int StyleFor(object v, string fmt, bool total)
        {
            if (v == null) return total ? St.TotText : St.Text;
            if (v is string) return total ? St.TotText : St.Text;
            if (v is DateTime) return St.Date;
            if (v is int || v is long) return total ? St.TotInt : St.Int;
            if (v is decimal || v is double || v is float)
            {
                if (fmt == "N0") return total ? St.TotInt : St.Int;
                if (fmt == "N1" || fmt == "0.0") return St.Dec1;
                return total ? St.TotNum : St.Num;
            }
            return St.Text;
        }

        public static SheetLayout Build(Sheet s, string subtitle, string uniqueName)
        {
            var L = new SheetLayout { Sheet = s, Name = uniqueName };
            int r = 0;
            L.Put(r, 0, s.Title, St.Title);
            for (int c = 1; c < 8; c++) L.Put(r, c, null, St.Title);
            r++;
            L.Put(r, 0, subtitle, St.Sub);
            r += 2;
            foreach (Block b in s.Blocks)
            {
                switch (b.Kind)
                {
                    case BlockKind.Table:
                        {
                            if (!string.IsNullOrEmpty(b.Title)) { L.Put(r, 0, b.Title, St.BlockTitle); r++; }
                            for (int c = 0; c < b.Headers.Length; c++)
                            {
                                L.Put(r, c, b.Headers[c], St.Header);
                                L.Width(c, (c < b.Widths.Length ? b.Widths[c] : 90) / 7.0 + 1);
                            }
                            r++;
                            foreach (object[] row in b.Rows)
                            {
                                bool total = row.Length > 0 && (row[0] as string) == "TOTAL";
                                for (int c = 0; c < b.Headers.Length; c++)
                                {
                                    object v = c < row.Length ? row[c] : null;
                                    L.Put(r, c, v, StyleFor(v, FormatOf(b.Formats, c), total));
                                }
                                r++;
                            }
                            r++;
                            break;
                        }
                    case BlockKind.Pairs:
                        {
                            if (!string.IsNullOrEmpty(b.Title)) { L.Put(r, 0, b.Title, St.BlockTitle); r++; }
                            foreach (KV kv in b.Pairs)
                            {
                                L.Put(r, 0, kv.Label, St.PairLabel);
                                L.Put(r, 1, kv.Value, kv.Level == 1 ? St.PairWarn : St.PairValue);
                                r++;
                            }
                            L.Width(0, 58);
                            L.Width(1, 30);
                            r++;
                            break;
                        }
                    case BlockKind.Chart:
                        {
                            int top = r;
                            if (!string.IsNullOrEmpty(b.Title)) { L.Put(r, 0, b.Title, St.BlockTitle); r++; }
                            int ns = b.Series.Count;
                            var cp = new ChartPlace { Block = b, SeriesCount = ns, HeaderRow = r };
                            L.Put(r, 0, "", St.Header);
                            for (int i = 0; i < ns; i++) L.Put(r, 1 + i, b.Series[i].Name, St.Header);
                            r++;
                            cp.FirstRow = r;
                            int maxLabel = 8;
                            for (int k = 0; k < b.Labels.Length; k++)
                            {
                                L.Put(r, 0, b.Labels[k], St.Text);
                                maxLabel = Math.Max(maxLabel, (b.Labels[k] ?? "").Length);
                                for (int i = 0; i < ns; i++)
                                {
                                    double v = k < b.Series[i].Values.Length ? b.Series[i].Values[k] : 0;
                                    if (double.IsNaN(v) || double.IsInfinity(v) || Math.Abs(v) > 1e15) v = 0;
                                    L.Put(r, 1 + i, (decimal)Math.Round(v, 2), St.Num);
                                }
                                r++;
                            }
                            cp.LastRow = r - 1;
                            L.Width(0, Math.Min(40, maxLabel + 2));
                            for (int i = 0; i < ns; i++) L.Width(1 + i, 14);
                            cp.AnchorRow = top;
                            cp.AnchorCol = ns + 2;
                            L.Charts.Add(cp);
                            cp.Number = 0;
                            int endRow = Math.Max(r, top + ChartRows);
                            r = endRow + 1;
                            break;
                        }
                    case BlockKind.Note:
                        L.Put(r, 0, b.Text, St.Note);
                        r += 2;
                        break;
                }
            }
            return L;
        }

        public static string ColName(int c)
        {
            string s = "";
            c++;
            while (c > 0) { int m = (c - 1) % 26; s = (char)('A' + m) + s; c = (c - 1) / 26; }
            return s;
        }

        public static string Num(decimal v) { return v.ToString(CultureInfo.InvariantCulture); }
        public static string Num(double v) { return v.ToString("R", CultureInfo.InvariantCulture); }

        public static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char ch in s)
            {
                if (ch == '&') sb.Append("&amp;");
                else if (ch == '<') sb.Append("&lt;");
                else if (ch == '>') sb.Append("&gt;");
                else if (ch == '"') sb.Append("&quot;");
                else if (ch < 0x20 && ch != '\t' && ch != '\n' && ch != '\r') continue;
                else if (ch >= 0xD800 && ch <= 0xDFFF) sb.Append('?');
                else if (ch == 0xFFFE || ch == 0xFFFF) continue;
                else sb.Append(ch);
            }
            return sb.ToString();
        }

        /// <summary>Noms de feuilles uniques, valides (31 caractères, sans []:*?/\).</summary>
        public static List<string> SheetNames(IEnumerable<Sheet> sheets)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var res = new List<string>();
            foreach (Sheet s in sheets)
            {
                string n = new string(s.Name.Where(ch => "[]:*?/\\'".IndexOf(ch) < 0).ToArray()).Trim();
                if (n.Length == 0) n = "Feuille";
                if (n.Length > 31) n = n.Substring(0, 31);
                string u = n; int k = 2;
                while (!used.Add(u)) { string suf = " (" + k + ")"; u = n.Substring(0, Math.Min(n.Length, 31 - suf.Length)) + suf; k++; }
                res.Add(u);
            }
            return res;
        }
    }
}
