using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace GestionGym
{
    /// <summary>Générateur PDF autonome (aucune bibliothèque) : A3 ou A4 paysage, un onglet par page (ou plusieurs si le tableau est long), titre sur chaque page, graphiques vectoriels.</summary>
    public class PdfBuilder
    {
        // ---------------------------------------------------------------- métriques Helvetica (1/1000 em), caractères 32 à 126
        static readonly int[] Wd = {
            278,278,355,556,556,889,667,191,333,333,389,584,278,333,278,278,
            556,556,556,556,556,556,556,556,556,556,278,278,584,584,584,556,
            1015,667,667,722,722,667,611,778,722,278,500,667,556,833,722,778,
            667,778,722,667,611,722,667,944,667,667,611,278,278,278,469,556,
            333,556,556,500,556,556,278,556,556,222,222,500,222,833,556,556,
            556,556,333,500,278,556,500,722,500,500,500,334,260,334,584 };

        public static double CharW(char c, bool bold)
        {
            double w;
            if (c >= 32 && c <= 126) w = Wd[c - 32];
            else if (c == '€') w = 556;
            else if (c == '•') w = 350;
            else if (c == '–') w = 556;
            else if (c == '—') w = 1000;
            else if (c >= 0xC0 && c <= 0xC5) w = 667;
            else if (c == 0xC7) w = 722;
            else if (c >= 0xC8 && c <= 0xCB) w = 667;
            else if (c >= 0xCC && c <= 0xCF) w = 278;
            else if (c >= 0xD2 && c <= 0xD6) w = 778;
            else if (c >= 0xD9 && c <= 0xDC) w = 722;
            else if (c >= 0xE0 && c <= 0xE5) w = 556;
            else if (c == 0xE7) w = 500;
            else if (c >= 0xE8 && c <= 0xEB) w = 556;
            else if (c >= 0xEC && c <= 0xEF) w = 278;
            else if (c >= 0xF2 && c <= 0xF6) w = 556;
            else if (c >= 0xF9 && c <= 0xFC) w = 556;
            else w = 556;
            return bold ? w * 1.07 : w;
        }

        public static double TextW(string s, double fs, bool bold)
        {
            double t = 0;
            foreach (char c in s) t += CharW(c, bold);
            return t * fs / 1000.0;
        }

        /// <summary>Convertit le texte en caractères WinAnsi (Latin-1 + quelques signes) ; le reste devient « ? ».</summary>
        public static string Enc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c == ' ' || c == ' ' || c == ' ' || c == ' ') sb.Append(' ');
                else if (c == '\t' || c == '\r' || c == '\n') sb.Append(' ');
                else if (c < 32) continue;
                else if (c < 127) sb.Append(c);
                else if (c == '€') sb.Append('\u0080');
                else if (c == '•' || c == '●') sb.Append('\u0095');
                else if (c == '–') sb.Append('\u0096');
                else if (c == '—') sb.Append('\u0097');
                else if (c == '’' || c == '‘') sb.Append('\'');
                else if (c == '“' || c == '”') sb.Append('"');
                else if (c == '…') sb.Append("...");
                else if (c == '⚠') sb.Append('!');
                else if (c == '✔' || c == '✓') sb.Append('\u0095');
                else if (c == '✖' || c == '✗') sb.Append('x');
                else if (c >= 0xA0 && c <= 0xFF) sb.Append(c);
                else if (c >= 0x80 && c <= 0x9F) sb.Append(c);
                else sb.Append('?');
            }
            return sb.ToString();
        }

        static string Esc(string s) { return s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)"); }
        static string N(double v) { return v.ToString("0.##", CultureInfo.InvariantCulture); }

        public static string Rgb(string hex)
        {
            if (hex == null || hex.Length != 6) hex = "7F7F7F";
            int r = Convert.ToInt32(hex.Substring(0, 2), 16), g = Convert.ToInt32(hex.Substring(2, 2), 16), b = Convert.ToInt32(hex.Substring(4, 2), 16);
            return N(r / 255.0) + " " + N(g / 255.0) + " " + N(b / 255.0);
        }

        // ---------------------------------------------------------------- état
        readonly double W, H;
        const double M = 28;
        readonly List<StringBuilder> pages = new List<StringBuilder>();
        StringBuilder cur;
        double y;
        string title = "", subtitle = "";
        readonly string footer;

        public PdfBuilder(bool a3, string footer, string subtitle)
        {
            if (a3) { W = 1190.55; H = 841.89; } else { W = 841.89; H = 595.28; }
            this.footer = footer;
            this.subtitle = subtitle;
        }

        double Avail { get { return W - 2 * M; } }
        double Bottom { get { return M + 14; } }

        // ---------------------------------------------------------------- primitives
        void Fill(double x, double yy, double w, double h, string hex)
        {
            cur.Append(Rgb(hex) + " rg " + N(x) + " " + N(yy) + " " + N(w) + " " + N(h) + " re f\n");
        }

        void Box(double x, double yy, double w, double h, string hex, double lw)
        {
            cur.Append(Rgb(hex) + " RG " + N(lw) + " w " + N(x) + " " + N(yy) + " " + N(w) + " " + N(h) + " re S\n");
        }

        void Line(double x1, double y1, double x2, double y2, string hex, double lw)
        {
            cur.Append(Rgb(hex) + " RG " + N(lw) + " w " + N(x1) + " " + N(y1) + " m " + N(x2) + " " + N(y2) + " l S\n");
        }

        void Text(string s, double x, double baseline, double fs, bool bold, string hex, bool italic = false)
        {
            if (string.IsNullOrEmpty(s)) return;
            string f = italic ? "F3" : (bold ? "F2" : "F1");
            cur.Append("BT /" + f + " " + N(fs) + " Tf " + Rgb(hex) + " rg " + N(x) + " " + N(baseline) + " Td (" + Esc(Enc(s)) + ") Tj ET\n");
        }

        void TextRight(string s, double xRight, double baseline, double fs, bool bold, string hex)
        {
            Text(s, xRight - TextW(Enc(s), fs, bold), baseline, fs, bold, hex);
        }

        void TextCenter(string s, double xc, double baseline, double fs, bool bold, string hex)
        {
            Text(s, xc - TextW(Enc(s), fs, bold) / 2, baseline, fs, bold, hex);
        }

        public static string Fit(string s, double maxW, double fs, bool bold)
        {
            s = Enc(s);
            if (TextW(s, fs, bold) <= maxW) return s;
            double dots = TextW("...", fs, bold);
            int n = s.Length;
            while (n > 0 && TextW(s.Substring(0, n), fs, bold) + dots > maxW) n--;
            return s.Substring(0, n).TrimEnd() + "...";
        }

        public static List<string> Wrap(string text, double maxW, double fs, bool bold, int maxLines)
        {
            var lines = new List<string>();
            string t = Enc(text);
            var words = t.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string line = "";
            foreach (string w0 in words)
            {
                string w = w0;
                while (TextW(w, fs, bold) > maxW && w.Length > 1)
                {
                    int k = w.Length - 1;
                    while (k > 1 && TextW(w.Substring(0, k), fs, bold) > maxW) k--;
                    if (line.Length > 0) { lines.Add(line); line = ""; }
                    lines.Add(w.Substring(0, k));
                    w = w.Substring(k);
                }
                string trial = line.Length == 0 ? w : line + " " + w;
                if (TextW(trial, fs, bold) <= maxW) line = trial;
                else { lines.Add(line); line = w; }
            }
            if (line.Length > 0) lines.Add(line);
            if (lines.Count > maxLines)
            {
                lines = lines.Take(maxLines).ToList();
                lines[maxLines - 1] = Fit(lines[maxLines - 1] + " ...", maxW, fs, bold);
            }
            return lines;
        }

        // ---------------------------------------------------------------- pages
        void NewPage(bool continued)
        {
            cur = new StringBuilder();
            pages.Add(cur);
            Fill(0, H - M - 6, W, M + 6, "1F3864");
            Text(title + (continued ? "  (suite)" : ""), M, H - M + 6, 16, true, "FFFFFF");
            TextRight("Gestion GYM", W - M, H - M + 6, 10, false, "D9E2F3");
            Text(subtitle, M, H - M - 18, 8, false, "595959");
            y = H - M - 30;
        }

        bool Ensure(double h)
        {
            if (y - h >= Bottom) return false;
            NewPage(true);
            return true;
        }

        // ---------------------------------------------------------------- feuille
        public void AddSheet(Sheet sh)
        {
            title = sh.Title;
            NewPage(false);
            var bl = sh.Blocks;
            for (int i = 0; i < bl.Count; i++)
            {
                Block b = bl[i];
                switch (b.Kind)
                {
                    case BlockKind.Table: DrawTable(b); break;
                    case BlockKind.Pairs: DrawPairs(b); break;
                    case BlockKind.Note: DrawNote(b); break;
                    case BlockKind.Chart:
                        {
                            Block b2 = (i + 1 < bl.Count && bl[i + 1].Kind == BlockKind.Chart) ? bl[i + 1] : null;
                            DrawCharts(b, b2);
                            if (b2 != null) i++;
                            break;
                        }
                }
            }
        }

        void BlockTitle(string t)
        {
            if (string.IsNullOrEmpty(t)) return;
            Ensure(40);
            Text(t, M, y - 10, 11, true, "2F5597");
            y -= 18;
        }

        static string CellText(object v, string fmt)
        {
            if (v == null) return "";
            if (v is string) return (string)v;
            if (v is DateTime) return ((DateTime)v).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            if (v is int || v is long) return Convert.ToInt64(v).ToString(Fmt.Fr);
            double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
            if (fmt == "N0") return d.ToString("N0", Fmt.Fr);
            if (fmt == "N1" || fmt == "0.0") return d.ToString("N1", Fmt.Fr);
            return d.ToString("N2", Fmt.Fr);
        }

        void DrawTable(Block b)
        {
            int n = b.Headers.Length;
            if (n == 0) return;
            BlockTitle(b.Title);
            var w = new double[n];
            double sum = 0;
            for (int i = 0; i < n; i++) { w[i] = (i < b.Widths.Length ? b.Widths[i] : 90) * 0.75; sum += w[i]; }
            double scale = sum > Avail ? Avail / sum : 1.0;
            for (int i = 0; i < n; i++) w[i] *= scale;
            double fs = Math.Max(5.0, Math.Min(8.5, 8.5 * Math.Min(1.0, scale * 1.25)));
            double rowH = fs * 1.8;
            int hl = 1;
            var hdrLines = new List<List<string>>();
            for (int i = 0; i < n; i++)
            {
                var l = Wrap(b.Headers[i], w[i] - 4, fs, true, 3);
                hdrLines.Add(l);
                hl = Math.Max(hl, l.Count);
            }
            double hdrH = hl * fs * 1.2 + 6;
            int rowNo = 0;
            bool needHeader = true;
            foreach (object[] row in b.Rows)
            {
                if (needHeader || y - rowH < Bottom)
                {
                    if (y - (hdrH + rowH) < Bottom) NewPage(true);
                    DrawHeader(b, w, hdrLines, fs, hdrH);
                    needHeader = false;
                }
                bool total = row.Length > 0 && (row[0] as string) == "TOTAL";
                double x = M;
                if (total) Fill(M, y - rowH, w.Sum(), rowH, "D9E2F3");
                else if (rowNo % 2 == 1) Fill(M, y - rowH, w.Sum(), rowH, "F5F7FB");
                for (int c = 0; c < n; c++)
                {
                    object v = c < row.Length ? row[c] : null;
                    string fmt = c < b.Formats.Length ? b.Formats[c] : null;
                    string t = CellText(v, fmt);
                    double bl = y - rowH / 2 - fs * 0.35;
                    if (t.Length > 0)
                    {
                        string ft = Fit(t, w[c] - 4, fs, total);
                        bool numeric = v != null && !(v is string) && !(v is DateTime);
                        if (numeric) TextRight(ft, x + w[c] - 2, bl, fs, total, "000000");
                        else if (v is DateTime) TextCenter(ft, x + w[c] / 2, bl, fs, total, "000000");
                        else Text(ft, x + 2, bl, fs, total, "000000");
                    }
                    x += w[c];
                }
                Line(M, y - rowH, M + w.Sum(), y - rowH, "D0D4DC", 0.3);
                y -= rowH;
                rowNo++;
            }
            if (b.Rows.Count == 0)
            {
                DrawHeader(b, w, hdrLines, fs, hdrH);
                Text("(aucune donnée)", M + 4, y - fs - 2, fs + 1, false, "7F7F7F", true);
                y -= fs * 2.5;
            }
            y -= 14;
        }

        void DrawHeader(Block b, double[] w, List<List<string>> hdrLines, double fs, double hdrH)
        {
            double x = M;
            Fill(M, y - hdrH, w.Sum(), hdrH, "2F5597");
            for (int c = 0; c < w.Length; c++)
            {
                var l = hdrLines[c];
                double top = y - (hdrH - l.Count * fs * 1.2) / 2;
                for (int k = 0; k < l.Count; k++)
                    TextCenter(l[k], x + w[c] / 2, top - (k + 1) * fs * 1.2 + fs * 0.25, fs, true, "FFFFFF");
                if (c > 0) Line(x, y - hdrH, x, y, "FFFFFF", 0.3);
                x += w[c];
            }
            y -= hdrH;
        }

        void DrawPairs(Block b)
        {
            BlockTitle(b.Title);
            double fs = 9, rh = 15;
            double total = Math.Min(Avail, 760);
            double lw = total * 0.66;
            int idx = 0;
            foreach (KV kv in b.Pairs)
            {
                Ensure(rh + 4);
                if (idx % 2 == 0) Fill(M, y - rh, total, rh, "F5F7FB");
                bool bold = kv.Level > 0;
                string col = kv.Level == 1 ? "C00000" : (kv.Level == 2 ? "1F3864" : "000000");
                double bl = y - rh / 2 - fs * 0.35;
                Text(Fit(kv.Label, lw - 6, fs, kv.Level == 2), M + 3, bl, fs, kv.Level == 2, "000000");
                TextRight(Fit(kv.Value ?? "", total - lw - 6, fs, bold), M + total - 3, bl, fs, bold, col);
                Line(M, y - rh, M + total, y - rh, "D0D4DC", 0.3);
                y -= rh;
                idx++;
            }
            y -= 14;
        }

        void DrawNote(Block b)
        {
            double fs = 8;
            var lines = Wrap(b.Text, Avail, fs, false, 6);
            Ensure(lines.Count * fs * 1.3 + 8);
            foreach (string l in lines)
            {
                Text(l, M, y - fs, fs, false, "595959", true);
                y -= fs * 1.3;
            }
            y -= 10;
        }

        // ---------------------------------------------------------------- graphiques
        void DrawCharts(Block a, Block b)
        {
            double ch = Math.Min(230, Math.Max(170, Avail * 0.2));
            Ensure(ch + 12);
            double gap = 20;
            double cw = b == null ? Math.Min(Avail, 640) : (Avail - gap) / 2;
            double top = y;
            DrawChart(a, M, top, cw, ch);
            if (b != null) DrawChart(b, M + cw + gap, top, cw, ch);
            y = top - ch - 14;
        }

        static double NiceStep(double range, int ticks)
        {
            if (range <= 0) return 1;
            double raw = range / ticks;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            double f = raw / mag;
            double nf = f <= 1 ? 1 : f <= 2 ? 2 : f <= 5 ? 5 : 10;
            return nf * mag;
        }

        static string Num(double v, double step)
        {
            if (step < 1) return v.ToString("0.0#", Fmt.Fr);
            return v.ToString("N0", Fmt.Fr);
        }

        void DrawChart(Block b, double x, double top, double w, double h)
        {
            Box(x, top - h, w, h, "D0D4DC", 0.6);
            TextCenter(b.Title, x + w / 2, top - 13, 9.5, true, "000000");
            int ns = b.Series.Count, nc = b.Labels.Length;
            if (ns == 0 || nc == 0) { TextCenter("(aucune donnée)", x + w / 2, top - h / 2, 8, false, "7F7F7F"); return; }
            double lw = 6.5;
            double minV = 0, maxV = 0;
            foreach (Series s in b.Series) foreach (double v in s.Values) { if (!double.IsNaN(v) && !double.IsInfinity(v)) { minV = Math.Min(minV, v); maxV = Math.Max(maxV, v); } }
            if (maxV == minV) maxV = minV + 1;
            double step = NiceStep(maxV - minV, 5);
            double lo = Math.Floor(minV / step) * step, hi = Math.Ceiling(maxV / step) * step;
            if (hi == lo) hi = lo + step;
            double legendH = ns > 1 ? 16 : 4;
            double plotTop = top - 24, plotBottom = top - h + legendH + (b.Horizontal ? 8 : 18);
            double plotLeft, plotRight = x + w - 12;
            if (b.Horizontal)
            {
                double maxLab = 0;
                foreach (string l in b.Labels) maxLab = Math.Max(maxLab, TextW(Enc(l ?? ""), lw, false));
                plotLeft = x + Math.Min(maxLab, w * 0.4) + 10;
            }
            else
            {
                double maxTick = Math.Max(TextW(Num(lo, step), lw, false), TextW(Num(hi, step), lw, false));
                plotLeft = x + maxTick + 10;
            }
            double pw = plotRight - plotLeft, ph = plotTop - plotBottom;
            if (pw < 20 || ph < 20) return;
            // grille et graduations
            int nt = (int)Math.Round((hi - lo) / step);
            for (int t = 0; t <= nt; t++)
            {
                double val = lo + t * step;
                if (b.Horizontal)
                {
                    double gx = plotLeft + (val - lo) / (hi - lo) * pw;
                    Line(gx, plotBottom, gx, plotTop, "E0E0E0", 0.4);
                    TextCenter(Num(val, step), gx, plotBottom - 7, lw, false, "595959");
                }
                else
                {
                    double gy = plotBottom + (val - lo) / (hi - lo) * ph;
                    Line(plotLeft, gy, plotRight, gy, "E0E0E0", 0.4);
                    TextRight(Num(val, step), plotLeft - 3, gy - lw * 0.35, lw, false, "595959");
                }
            }
            double slot = (b.Horizontal ? ph : pw) / nc;
            double groupW = slot * 0.78;
            double barW = groupW / ns;
            bool showVals = nc * ns <= 16 && barW >= 11;
            double zero = b.Horizontal ? plotLeft + (0 - lo) / (hi - lo) * pw : plotBottom + (0 - lo) / (hi - lo) * ph;
            for (int c = 0; c < nc; c++)
            {
                double s0 = (b.Horizontal ? plotTop - (c + 1) * slot : plotLeft + c * slot) + (slot - groupW) / 2;
                for (int si = 0; si < ns; si++)
                {
                    double v = c < b.Series[si].Values.Length ? b.Series[si].Values[c] : 0;
                    if (double.IsNaN(v) || double.IsInfinity(v)) v = 0;
                    string col = b.Series[si].Color;
                    if (b.Horizontal)
                    {
                        double bx2 = plotLeft + (v - lo) / (hi - lo) * pw;
                        double by = s0 + (ns - 1 - si) * barW;
                        Fill(Math.Min(zero, bx2), by, Math.Abs(bx2 - zero), barW, col);
                        if (showVals || nc <= 10 && ns == 1)
                            Text(v.ToString("N0", Fmt.Fr), (v >= 0 ? bx2 : zero) + 2, by + barW / 2 - 2.2, 6, false, "000000");
                    }
                    else
                    {
                        double bx = s0 + si * barW;
                        double by2 = plotBottom + (v - lo) / (hi - lo) * ph;
                        Fill(bx, Math.Min(zero, by2), barW, Math.Abs(by2 - zero), col);
                        if (showVals && v != 0)
                            TextCenter(v.ToString("N0", Fmt.Fr), bx + barW / 2, (v >= 0 ? by2 + 2 : by2 - 7), 6, false, "000000");
                    }
                }
                // libellé de catégorie
                string lab = b.Labels[c] ?? "";
                if (b.Horizontal) TextRight(Fit(lab, plotLeft - x - 6, lw, false), plotLeft - 4, s0 + groupW / 2 - 2.2, lw, false, "000000");
                else TextCenter(Fit(lab, slot - 2, lw, false), plotLeft + c * slot + slot / 2, plotBottom - 9, lw, false, "000000");
            }
            // axes
            if (b.Horizontal) Line(zero, plotBottom, zero, plotTop, "7F7F7F", 0.7);
            else Line(plotLeft, zero, plotRight, zero, "7F7F7F", 0.7);
            // légende
            if (ns > 1)
            {
                double lx = x + 10, ly = top - h + 6;
                foreach (Series s in b.Series)
                {
                    Fill(lx, ly, 7, 7, s.Color);
                    Text(s.Name, lx + 10, ly + 0.5, 7, false, "000000");
                    lx += 20 + TextW(Enc(s.Name), 7, false) + 10;
                }
            }
        }

        // ---------------------------------------------------------------- assemblage du fichier
        public void Save(string path)
        {
            int n = pages.Count;
            for (int i = 0; i < n; i++)
            {
                cur = pages[i];
                TextRight("Page " + (i + 1) + " / " + n, W - M, M - 4, 8, false, "595959");
                Text(footer, M, M - 4, 8, false, "595959");
            }
            var enc = Encoding.GetEncoding(28591);
            var ms = new MemoryStream();
            var offsets = new List<long>();
            Action<string> wr = s => { byte[] bt = enc.GetBytes(s); ms.Write(bt, 0, bt.Length); };
            wr("%PDF-1.4\n%âãÏÓ\n");
            int firstPage = 6;   // objets : 1 catalogue, 2 pages, 3-5 polices, puis (page, contenu) par page
            Action<int, string> obj = (id, body) =>
            {
                while (offsets.Count < id) offsets.Add(0);
                offsets[id - 1] = ms.Position;
                wr(id + " 0 obj\n" + body + "\nendobj\n");
            };
            obj(1, "<< /Type /Catalog /Pages 2 0 R >>");
            var kids = new StringBuilder();
            for (int i = 0; i < n; i++) kids.Append((firstPage + 2 * i) + " 0 R ");
            obj(2, "<< /Type /Pages /Kids [" + kids + "] /Count " + n + " >>");
            obj(3, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
            obj(4, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
            obj(5, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Oblique /Encoding /WinAnsiEncoding >>");
            for (int i = 0; i < n; i++)
            {
                int pid = firstPage + 2 * i, cid = pid + 1;
                obj(pid, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + N(W) + " " + N(H) + "] /Resources << /Font << /F1 3 0 R /F2 4 0 R /F3 5 0 R >> >> /Contents " + cid + " 0 R >>");
                string content = pages[i].ToString();
                obj(cid, "<< /Length " + enc.GetByteCount(content) + " >>\nstream\n" + content + "\nendstream");
            }
            long xref = ms.Position;
            wr("xref\n0 " + (offsets.Count + 1) + "\n0000000000 65535 f \n");
            foreach (long o in offsets) wr(o.ToString("0000000000") + " 00000 n \n");
            wr("trailer\n<< /Size " + (offsets.Count + 1) + " /Root 1 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
            string tmp = path + ".tmp";
            File.WriteAllBytes(tmp, ms.ToArray());
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public static void Write(string path, List<Sheet> sheets, string subtitle, string footer, bool a3)
        {
            var pdf = new PdfBuilder(a3, footer, subtitle);
            foreach (Sheet s in sheets) pdf.AddSheet(s);
            pdf.Save(path);
        }
    }
}
