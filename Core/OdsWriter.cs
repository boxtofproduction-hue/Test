using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace GestionGym
{
    /// <summary>Écrit un classeur OpenDocument (.ods) lisible par LibreOffice / OpenOffice, avec graphiques intégrés.</summary>
    public static class OdsWriter
    {
        const string Ns = "xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\" xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" xmlns:table=\"urn:oasis:names:tc:opendocument:xmlns:table:1.0\" xmlns:draw=\"urn:oasis:names:tc:opendocument:xmlns:drawing:1.0\" xmlns:fo=\"urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" xmlns:svg=\"urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0\" xmlns:chart=\"urn:oasis:names:tc:opendocument:xmlns:chart:1.0\" xmlns:number=\"urn:oasis:names:tc:opendocument:xmlns:datastyle:1.0\"";
        const string Hdr = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n";

        static void Add(ZipArchive z, string name, string content, CompressionLevel lvl)
        {
            ZipArchiveEntry e = z.CreateEntry(name, lvl);
            using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false))) w.Write(content);
        }

        public static void Write(string path, List<Sheet> sheets, string subtitle)
        {
            string tmp = path + ".tmp";
            if (File.Exists(tmp)) File.Delete(tmp);
            try
            {
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.ReadWrite))
                using (var z = new ZipArchive(fs, ZipArchiveMode.Create))
                    WriteTo(z, sheets, subtitle);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
        }

        static string Q(string sheet) { return "'" + sheet.Replace("'", "''") + "'"; }

        static void WriteTo(ZipArchive z, List<Sheet> sheets, string subtitle)
        {
            List<string> names = SheetLayout.SheetNames(sheets);
            var layouts = new List<SheetLayout>();
            for (int i = 0; i < sheets.Count; i++) layouts.Add(SheetLayout.Build(sheets[i], subtitle, names[i]));
            int n = 0;
            foreach (SheetLayout L in layouts) foreach (ChartPlace cp in L.Charts) cp.Number = ++n;

            // le fichier « mimetype » doit être le premier et non compressé
            Add(z, "mimetype", "application/vnd.oasis.opendocument.spreadsheet", CompressionLevel.NoCompression);

            var man = new StringBuilder(Hdr);
            man.Append("<manifest:manifest xmlns:manifest=\"urn:oasis:names:tc:opendocument:xmlns:manifest:1.0\" manifest:version=\"1.2\">");
            man.Append("<manifest:file-entry manifest:full-path=\"/\" manifest:version=\"1.2\" manifest:media-type=\"application/vnd.oasis.opendocument.spreadsheet\"/>");
            man.Append("<manifest:file-entry manifest:full-path=\"content.xml\" manifest:media-type=\"text/xml\"/>");
            man.Append("<manifest:file-entry manifest:full-path=\"styles.xml\" manifest:media-type=\"text/xml\"/>");
            for (int k = 1; k <= n; k++)
            {
                man.Append("<manifest:file-entry manifest:full-path=\"Object " + k + "/\" manifest:media-type=\"application/vnd.oasis.opendocument.chart\"/>");
                man.Append("<manifest:file-entry manifest:full-path=\"Object " + k + "/content.xml\" manifest:media-type=\"text/xml\"/>");
            }
            man.Append("</manifest:manifest>");
            Add(z, "META-INF/manifest.xml", man.ToString(), CompressionLevel.Optimal);

            Add(z, "styles.xml", Hdr + "<office:document-styles " + Ns + " office:version=\"1.2\"><office:automatic-styles>"
                + "<style:page-layout style:name=\"pm1\"><style:page-layout-properties fo:page-width=\"42cm\" fo:page-height=\"29.7cm\" style:print-orientation=\"landscape\" fo:margin-left=\"1cm\" fo:margin-right=\"1cm\" fo:margin-top=\"1cm\" fo:margin-bottom=\"1cm\" style:scale-to-X=\"1\" style:scale-to-Y=\"0\"/><style:header-style/><style:footer-style/></style:page-layout>"
                + "</office:automatic-styles><office:master-styles><style:master-page style:name=\"Default\" style:page-layout-name=\"pm1\"/></office:master-styles></office:document-styles>", CompressionLevel.Optimal);

            Add(z, "content.xml", ContentXml(layouts), CompressionLevel.Optimal);
            foreach (SheetLayout L in layouts)
                foreach (ChartPlace cp in L.Charts)
                    Add(z, "Object " + cp.Number + "/content.xml", ChartXml(L, cp), CompressionLevel.Optimal);
        }

        static string CellStyle(int st)
        {
            switch (st)
            {
                case St.Title: return "ceT";
                case St.Header: return "ceH";
                case St.Text: return "ceX";
                case St.Num: return "ceN";
                case St.Int: return "ceI";
                case St.Date: return "ceD";
                case St.Dec1: return "ceO";
                case St.TotText: return "ceTX";
                case St.TotNum: return "ceTN";
                case St.TotInt: return "ceTI";
                case St.Note: return "ceNote";
                case St.PairLabel: return "ceL";
                case St.PairValue: return "ceV";
                case St.PairWarn: return "ceW";
                case St.BlockTitle: return "ceB";
                case St.Sub: return "ceS";
                default: return "ceX";
            }
        }

        static string Styles(List<SheetLayout> layouts, out Dictionary<int, string> colStyles)
        {
            var s = new StringBuilder();
            // formats de nombres
            s.Append("<number:number-style style:name=\"NF2\"><number:number number:decimal-places=\"2\" number:min-integer-digits=\"1\" number:grouping=\"true\"/></number:number-style>");
            s.Append("<number:number-style style:name=\"NF0\"><number:number number:decimal-places=\"0\" number:min-integer-digits=\"1\"/></number:number-style>");
            s.Append("<number:number-style style:name=\"NF1\"><number:number number:decimal-places=\"1\" number:min-integer-digits=\"1\"/></number:number-style>");
            s.Append("<number:date-style style:name=\"DF1\"><number:day number:style=\"long\"/><number:text>/</number:text><number:month number:style=\"long\"/><number:text>/</number:text><number:year number:style=\"long\"/></number:date-style>");
            const string border = "fo:border=\"0.5pt solid #bfbfbf\"";
            s.Append("<style:style style:name=\"ceT\" style:family=\"table-cell\"><style:table-cell-properties fo:background-color=\"#1F3864\"/><style:text-properties fo:color=\"#ffffff\" fo:font-weight=\"bold\" fo:font-size=\"16pt\"/></style:style>");
            s.Append("<style:style style:name=\"ceH\" style:family=\"table-cell\"><style:table-cell-properties fo:background-color=\"#2F5597\" " + border + " fo:wrap-option=\"wrap\" style:vertical-align=\"middle\"/><style:paragraph-properties fo:text-align=\"center\"/><style:text-properties fo:color=\"#ffffff\" fo:font-weight=\"bold\"/></style:style>");
            s.Append("<style:style style:name=\"ceX\" style:family=\"table-cell\"><style:table-cell-properties " + border + "/></style:style>");
            s.Append("<style:style style:name=\"ceN\" style:family=\"table-cell\" style:data-style-name=\"NF2\"><style:table-cell-properties " + border + "/></style:style>");
            s.Append("<style:style style:name=\"ceI\" style:family=\"table-cell\" style:data-style-name=\"NF0\"><style:table-cell-properties " + border + "/></style:style>");
            s.Append("<style:style style:name=\"ceO\" style:family=\"table-cell\" style:data-style-name=\"NF1\"><style:table-cell-properties " + border + "/></style:style>");
            s.Append("<style:style style:name=\"ceD\" style:family=\"table-cell\" style:data-style-name=\"DF1\"><style:table-cell-properties " + border + "/><style:paragraph-properties fo:text-align=\"center\"/></style:style>");
            s.Append("<style:style style:name=\"ceTX\" style:family=\"table-cell\"><style:table-cell-properties fo:background-color=\"#D9E2F3\" " + border + "/><style:text-properties fo:font-weight=\"bold\"/></style:style>");
            s.Append("<style:style style:name=\"ceTN\" style:family=\"table-cell\" style:data-style-name=\"NF2\"><style:table-cell-properties fo:background-color=\"#D9E2F3\" " + border + "/><style:text-properties fo:font-weight=\"bold\"/></style:style>");
            s.Append("<style:style style:name=\"ceTI\" style:family=\"table-cell\" style:data-style-name=\"NF0\"><style:table-cell-properties fo:background-color=\"#D9E2F3\" " + border + "/><style:text-properties fo:font-weight=\"bold\"/></style:style>");
            s.Append("<style:style style:name=\"ceNote\" style:family=\"table-cell\"><style:text-properties fo:font-style=\"italic\" fo:color=\"#595959\" fo:font-size=\"9pt\"/></style:style>");
            s.Append("<style:style style:name=\"ceL\" style:family=\"table-cell\"><style:table-cell-properties fo:background-color=\"#F2F2F2\" " + border + " fo:wrap-option=\"wrap\"/></style:style>");
            s.Append("<style:style style:name=\"ceV\" style:family=\"table-cell\"><style:table-cell-properties " + border + "/><style:paragraph-properties fo:text-align=\"end\"/><style:text-properties fo:font-weight=\"bold\"/></style:style>");
            s.Append("<style:style style:name=\"ceW\" style:family=\"table-cell\"><style:table-cell-properties " + border + "/><style:paragraph-properties fo:text-align=\"end\"/><style:text-properties fo:font-weight=\"bold\" fo:color=\"#C00000\"/></style:style>");
            s.Append("<style:style style:name=\"ceB\" style:family=\"table-cell\"><style:text-properties fo:font-weight=\"bold\" fo:font-size=\"12pt\" fo:color=\"#2F5597\"/></style:style>");
            s.Append("<style:style style:name=\"ceS\" style:family=\"table-cell\"><style:text-properties fo:color=\"#595959\"/></style:style>");
            s.Append("<style:style style:name=\"gr1\" style:family=\"graphic\"><style:graphic-properties draw:stroke=\"none\" draw:fill=\"none\"/></style:style>");
            // styles de colonnes : un par largeur distincte (en 1/10 cm)
            colStyles = new Dictionary<int, string>();
            foreach (SheetLayout L in layouts)
                foreach (double w in L.ColChars.Values)
                {
                    int key = (int)Math.Round(w * 1.9);   // 1 caractère ≈ 0,19 cm
                    if (colStyles.ContainsKey(key)) continue;
                    string nm = "co" + (colStyles.Count + 1);
                    colStyles[key] = nm;
                    s.Append("<style:style style:name=\"" + nm + "\" style:family=\"table-column\"><style:table-column-properties style:column-width=\"" + SheetLayout.Num(key / 10.0m) + "cm\"/></style:style>");
                }
            return s.ToString();
        }

        static string ContentXml(List<SheetLayout> layouts)
        {
            Dictionary<int, string> colStyles;
            string styles = Styles(layouts, out colStyles);
            var s = new StringBuilder(Hdr);
            s.Append("<office:document-content " + Ns + " office:version=\"1.2\"><office:automatic-styles>" + styles + "</office:automatic-styles><office:body><office:spreadsheet>");
            foreach (SheetLayout L in layouts)
            {
                s.Append("<table:table table:name=\"" + SheetLayout.Esc(L.Name) + "\">");
                int ncol = L.MaxCol + 1 + SheetLayout.ChartCols + 4;
                for (int c = 0; c < ncol; c++)
                {
                    double w;
                    if (L.ColChars.TryGetValue(c, out w)) s.Append("<table:table-column table:style-name=\"" + colStyles[(int)Math.Round(w * 1.9)] + "\"/>");
                    else s.Append("<table:table-column/>");
                }
                var anchors = new Dictionary<string, ChartPlace>();
                foreach (ChartPlace cp in L.Charts) anchors[cp.AnchorRow + ":" + cp.AnchorCol] = cp;
                int last = -1;
                foreach (var row in L.Cells)
                {
                    if (row.Key - last > 1) s.Append("<table:table-row table:number-rows-repeated=\"" + (row.Key - last - 1) + "\"><table:table-cell/></table:table-row>");
                    last = row.Key;
                    s.Append("<table:table-row>");
                    int col = 0;
                    var cellCols = row.Value.Keys.ToList();
                    foreach (ChartPlace cp in L.Charts) if (cp.AnchorRow == row.Key && !cellCols.Contains(cp.AnchorCol)) cellCols.Add(cp.AnchorCol);
                    cellCols.Sort();
                    foreach (int c in cellCols)
                    {
                        if (c > col) s.Append("<table:table-cell table:number-columns-repeated=\"" + (c - col) + "\"/>");
                        col = c + 1;
                        LCell cell;
                        ChartPlace chart;
                        anchors.TryGetValue(row.Key + ":" + c, out chart);
                        if (!row.Value.TryGetValue(c, out cell))
                        {
                            s.Append("<table:table-cell>" + Frame(L, chart) + "</table:table-cell>");
                            continue;
                        }
                        s.Append(CellXml(cell));
                    }
                    s.Append("</table:table-row>");
                }
                s.Append("</table:table>");
            }
            s.Append("</office:spreadsheet></office:body></office:document-content>");
            return s.ToString();
        }

        static string CellXml(LCell cell)
        {
            string st = CellStyle(cell.Style);
            object v = cell.Value;
            if (v == null) return "<table:table-cell table:style-name=\"" + st + "\"/>";
            if (v is string)
            {
                string t = (string)v;
                if (t.Length == 0) return "<table:table-cell table:style-name=\"" + st + "\"/>";
                return "<table:table-cell table:style-name=\"" + st + "\" office:value-type=\"string\"><text:p>" + SheetLayout.Esc(t) + "</text:p></table:table-cell>";
            }
            if (v is DateTime)
            {
                DateTime d = (DateTime)v;
                return "<table:table-cell table:style-name=\"" + st + "\" office:value-type=\"date\" office:date-value=\"" + d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + "\"><text:p>" + d.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture) + "</text:p></table:table-cell>";
            }
            string num;
            if (v is decimal) num = SheetLayout.Num((decimal)v);
            else if (v is double) num = SheetLayout.Num((double)v);
            else num = Convert.ToInt64(v).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return "<table:table-cell table:style-name=\"" + st + "\" office:value-type=\"float\" office:value=\"" + num + "\"><text:p>" + num + "</text:p></table:table-cell>";
        }

        static string Frame(SheetLayout L, ChartPlace cp)
        {
            if (cp == null) return "";
            // largeur selon la taille : barres horizontales avec libellés longs plus larges
            return "<draw:frame draw:z-index=\"0\" draw:name=\"Graphique " + cp.Number + "\" draw:style-name=\"gr1\" svg:width=\"17cm\" svg:height=\"8cm\" svg:x=\"0cm\" svg:y=\"0cm\">"
                + "<draw:object xlink:href=\"./Object " + cp.Number + "\" xlink:type=\"simple\" xlink:show=\"embed\" xlink:actuate=\"onLoad\"/></draw:frame>";
        }

        static string ChartXml(SheetLayout L, ChartPlace cp)
        {
            Block b = cp.Block;
            string sh = Q(L.Name);
            var s = new StringBuilder(Hdr);
            s.Append("<office:document-content " + Ns + " office:version=\"1.2\"><office:automatic-styles>");
            s.Append("<style:style style:name=\"ch1\" style:family=\"chart\"><style:chart-properties chart:vertical=\"" + (b.Horizontal ? "true" : "false") + "\" chart:include-hidden-cells=\"false\" chart:auto-position=\"true\" chart:auto-size=\"true\" chart:treat-empty-cells=\"leave-gap\" chart:right-angles=\"true\"/></style:style>");
            for (int i = 0; i < b.Series.Count; i++)
                s.Append("<style:style style:name=\"chS" + i + "\" style:family=\"chart\"><style:graphic-properties draw:stroke=\"none\" draw:fill=\"solid\" draw:fill-color=\"#" + b.Series[i].Color + "\"/></style:style>");
            s.Append("<style:style style:name=\"ch4\" style:family=\"chart\"><style:chart-properties chart:include-hidden-cells=\"false\"/></style:style>");
            s.Append("<style:style style:name=\"chA\" style:family=\"chart\"><style:chart-properties chart:display-label=\"true\" chart:tick-marks-major-inner=\"false\" chart:tick-marks-major-outer=\"true\" chart:axis-position=\"start\" chart:link-data-style-to-source=\"true\"/><style:text-properties fo:font-size=\"8pt\"/></style:style>");
            s.Append("<style:style style:name=\"chW\" style:family=\"chart\"><style:graphic-properties draw:stroke=\"none\" draw:fill=\"solid\" draw:fill-color=\"#ffffff\"/></style:style>");
            s.Append("<style:style style:name=\"chT\" style:family=\"chart\"><style:text-properties fo:font-size=\"12pt\" fo:font-weight=\"bold\"/></style:style>");
            s.Append("</office:automatic-styles><office:body><office:chart>");
            s.Append("<chart:chart svg:width=\"17cm\" svg:height=\"8cm\" chart:class=\"chart:bar\" chart:style-name=\"ch1\">");
            s.Append("<chart:title svg:x=\"1cm\" svg:y=\"0.2cm\" chart:style-name=\"chT\"><text:p>" + SheetLayout.Esc(b.Title) + "</text:p></chart:title>");
            if (b.Series.Count > 1) s.Append("<chart:legend chart:legend-position=\"bottom\" style:legend-expansion=\"wide\"/>");
            string all = sh + ".A" + (cp.HeaderRow + 1) + ":" + sh + "." + SheetLayout.ColName(cp.SeriesCount) + (cp.LastRow + 1);
            s.Append("<chart:plot-area chart:style-name=\"ch4\" table:cell-range-address=\"" + SheetLayout.Esc(all) + "\" chart:data-source-has-labels=\"both\">");
            s.Append("<chart:wall chart:style-name=\"chW\"/>");
            s.Append("<chart:axis chart:dimension=\"x\" chart:name=\"primary-x\" chart:style-name=\"chA\"><chart:categories table:cell-range-address=\"" + SheetLayout.Esc(sh + ".A" + (cp.FirstRow + 1) + ":" + sh + ".A" + (cp.LastRow + 1)) + "\"/></chart:axis>");
            s.Append("<chart:axis chart:dimension=\"y\" chart:name=\"primary-y\" chart:style-name=\"chA\"><chart:grid chart:class=\"major\"/></chart:axis>");
            int npts = cp.LastRow - cp.FirstRow + 1;
            for (int i = 0; i < b.Series.Count; i++)
            {
                string col = SheetLayout.ColName(1 + i);
                s.Append("<chart:series chart:style-name=\"chS" + i + "\" chart:values-cell-range-address=\"" + SheetLayout.Esc(sh + "." + col + (cp.FirstRow + 1) + ":" + sh + "." + col + (cp.LastRow + 1)) + "\" chart:label-cell-address=\"" + SheetLayout.Esc(sh + "." + col + (cp.HeaderRow + 1) + ":" + sh + "." + col + (cp.HeaderRow + 1)) + "\" chart:class=\"chart:bar\"><chart:data-point chart:repeated=\"" + Math.Max(1, npts) + "\"/></chart:series>");
            }
            s.Append("</chart:plot-area>");
            // copie locale des données (requise par certains lecteurs)
            s.Append("<table:table table:name=\"local-table\"><table:table-header-columns><table:table-column/></table:table-header-columns><table:table-columns><table:table-column table:number-columns-repeated=\"" + b.Series.Count + "\"/></table:table-columns>");
            s.Append("<table:table-header-rows><table:table-row><table:table-cell><text:p/></table:table-cell>");
            foreach (Series se in b.Series) s.Append("<table:table-cell office:value-type=\"string\"><text:p>" + SheetLayout.Esc(se.Name) + "</text:p></table:table-cell>");
            s.Append("</table:table-row></table:table-header-rows><table:table-rows>");
            for (int k = 0; k < b.Labels.Length; k++)
            {
                s.Append("<table:table-row><table:table-cell office:value-type=\"string\"><text:p>" + SheetLayout.Esc(b.Labels[k]) + "</text:p></table:table-cell>");
                foreach (Series se in b.Series)
                {
                    double v = k < se.Values.Length ? se.Values[k] : 0;
                    if (double.IsNaN(v) || double.IsInfinity(v)) v = 0;
                    string vs = SheetLayout.Num(Math.Round(v, 2));
                    s.Append("<table:table-cell office:value-type=\"float\" office:value=\"" + vs + "\"><text:p>" + vs + "</text:p></table:table-cell>");
                }
                s.Append("</table:table-row>");
            }
            s.Append("</table:table-rows></table:table></chart:chart></office:chart></office:body></office:document-content>");
            return s.ToString();
        }
    }
}
