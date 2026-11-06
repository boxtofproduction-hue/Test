using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace GestionGym
{
    /// <summary>Écrit un classeur Excel (.xlsx) sans aucune dépendance : mise en forme, largeurs de colonnes et vrais graphiques Excel.</summary>
    public static class XlsxWriter
    {
        const string NsMain = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        const string NsRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        const string NsPkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";
        const string Hdr = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n";

        static void Add(ZipArchive z, string name, string content)
        {
            ZipArchiveEntry e = z.CreateEntry(name, CompressionLevel.Optimal);
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

        static void WriteTo(ZipArchive z, List<Sheet> sheets, string subtitle)
        {
            List<string> names = SheetLayout.SheetNames(sheets);
            var layouts = new List<SheetLayout>();
            for (int i = 0; i < sheets.Count; i++) layouts.Add(SheetLayout.Build(sheets[i], subtitle, names[i]));
            int chartNo = 0;
            foreach (SheetLayout L in layouts) foreach (ChartPlace cp in L.Charts) cp.Number = ++chartNo;

            // ---- types de contenu
            var ct = new StringBuilder(Hdr);
            ct.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
            ct.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>");
            ct.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
            ct.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
            for (int i = 0; i < layouts.Count; i++)
            {
                ct.Append("<Override PartName=\"/xl/worksheets/sheet" + (i + 1) + ".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
                if (layouts[i].Charts.Count > 0)
                    ct.Append("<Override PartName=\"/xl/drawings/drawing" + (i + 1) + ".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawing+xml\"/>");
            }
            for (int k = 1; k <= chartNo; k++)
                ct.Append("<Override PartName=\"/xl/charts/chart" + k + ".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawingml.chart+xml\"/>");
            ct.Append("</Types>");
            Add(z, "[Content_Types].xml", ct.ToString());

            Add(z, "_rels/.rels", Hdr + "<Relationships xmlns=\"" + NsPkgRel + "\"><Relationship Id=\"rId1\" Type=\"" + NsRel + "/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");

            // ---- classeur
            var wb = new StringBuilder(Hdr);
            wb.Append("<workbook xmlns=\"" + NsMain + "\" xmlns:r=\"" + NsRel + "\"><bookViews><workbookView/></bookViews><sheets>");
            for (int i = 0; i < layouts.Count; i++)
                wb.Append("<sheet name=\"" + SheetLayout.Esc(layouts[i].Name) + "\" sheetId=\"" + (i + 1) + "\" r:id=\"rId" + (i + 1) + "\"/>");
            wb.Append("</sheets></workbook>");
            Add(z, "xl/workbook.xml", wb.ToString());

            var wr = new StringBuilder(Hdr);
            wr.Append("<Relationships xmlns=\"" + NsPkgRel + "\">");
            for (int i = 0; i < layouts.Count; i++)
                wr.Append("<Relationship Id=\"rId" + (i + 1) + "\" Type=\"" + NsRel + "/worksheet\" Target=\"worksheets/sheet" + (i + 1) + ".xml\"/>");
            wr.Append("<Relationship Id=\"rId" + (layouts.Count + 1) + "\" Type=\"" + NsRel + "/styles\" Target=\"styles.xml\"/></Relationships>");
            Add(z, "xl/_rels/workbook.xml.rels", wr.ToString());

            Add(z, "xl/styles.xml", Styles());

            for (int i = 0; i < layouts.Count; i++)
            {
                SheetLayout L = layouts[i];
                Add(z, "xl/worksheets/sheet" + (i + 1) + ".xml", SheetXml(L));
                if (L.Charts.Count > 0)
                {
                    Add(z, "xl/worksheets/_rels/sheet" + (i + 1) + ".xml.rels", Hdr + "<Relationships xmlns=\"" + NsPkgRel + "\"><Relationship Id=\"rId1\" Type=\"" + NsRel + "/drawing\" Target=\"../drawings/drawing" + (i + 1) + ".xml\"/></Relationships>");
                    Add(z, "xl/drawings/drawing" + (i + 1) + ".xml", DrawingXml(L));
                    var dr = new StringBuilder(Hdr);
                    dr.Append("<Relationships xmlns=\"" + NsPkgRel + "\">");
                    for (int k = 0; k < L.Charts.Count; k++)
                        dr.Append("<Relationship Id=\"rId" + (k + 1) + "\" Type=\"" + NsRel + "/chart\" Target=\"../charts/chart" + L.Charts[k].Number + ".xml\"/>");
                    dr.Append("</Relationships>");
                    Add(z, "xl/drawings/_rels/drawing" + (i + 1) + ".xml.rels", dr.ToString());
                    foreach (ChartPlace cp in L.Charts) Add(z, "xl/charts/chart" + cp.Number + ".xml", ChartXml(L, cp));
                }
            }
        }

        static string Styles()
        {
            var s = new StringBuilder(Hdr);
            s.Append("<styleSheet xmlns=\"" + NsMain + "\">");
            s.Append("<numFmts count=\"2\"><numFmt numFmtId=\"164\" formatCode=\"dd/mm/yyyy\"/><numFmt numFmtId=\"165\" formatCode=\"0.0\"/></numFmts>");
            s.Append("<fonts count=\"8\">");
            s.Append("<font><sz val=\"10\"/><name val=\"Arial\"/></font>");                                                   // 0 normal
            s.Append("<font><b/><sz val=\"16\"/><color rgb=\"FFFFFFFF\"/><name val=\"Arial\"/></font>");                       // 1 titre
            s.Append("<font><b/><sz val=\"10\"/><color rgb=\"FFFFFFFF\"/><name val=\"Arial\"/></font>");                       // 2 en-tête
            s.Append("<font><b/><sz val=\"10\"/><name val=\"Arial\"/></font>");                                               // 3 gras
            s.Append("<font><i/><sz val=\"9\"/><color rgb=\"FF595959\"/><name val=\"Arial\"/></font>");                       // 4 note
            s.Append("<font><b/><sz val=\"12\"/><color rgb=\"FF2F5597\"/><name val=\"Arial\"/></font>");                       // 5 titre de bloc
            s.Append("<font><b/><sz val=\"10\"/><color rgb=\"FFC00000\"/><name val=\"Arial\"/></font>");                       // 6 alerte
            s.Append("<font><sz val=\"10\"/><color rgb=\"FF595959\"/><name val=\"Arial\"/></font>");                           // 7 sous-titre
            s.Append("</fonts>");
            s.Append("<fills count=\"6\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>");
            s.Append("<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF1F3864\"/><bgColor indexed=\"64\"/></patternFill></fill>");   // 2 titre
            s.Append("<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF2F5597\"/><bgColor indexed=\"64\"/></patternFill></fill>");   // 3 en-tête
            s.Append("<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFD9E2F3\"/><bgColor indexed=\"64\"/></patternFill></fill>");   // 4 total
            s.Append("<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFF2F2F2\"/><bgColor indexed=\"64\"/></patternFill></fill>");   // 5 libellé
            s.Append("</fills>");
            s.Append("<borders count=\"2\"><border><left/><right/><top/><bottom/><diagonal/></border>");
            s.Append("<border><left style=\"thin\"><color rgb=\"FFBFBFBF\"/></left><right style=\"thin\"><color rgb=\"FFBFBFBF\"/></right><top style=\"thin\"><color rgb=\"FFBFBFBF\"/></top><bottom style=\"thin\"><color rgb=\"FFBFBFBF\"/></bottom><diagonal/></border></borders>");
            s.Append("<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>");
            s.Append("<cellXfs count=\"17\">");
            string[] xf =
            {
                /*0*/ "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>",
                /*1 titre*/ "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\"/>",
                /*2 en-tête*/ "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf>",
                /*3 texte*/ "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\"/>",
                /*4 nombre*/ "<xf numFmtId=\"4\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\"/>",
                /*5 entier*/ "<xf numFmtId=\"1\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\"/>",
                /*6 date*/ "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\"/></xf>",
                /*7 déc 1*/ "<xf numFmtId=\"165\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\"/>",
                /*8 total texte*/ "<xf numFmtId=\"0\" fontId=\"3\" fillId=\"4\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"/>",
                /*9 total nombre*/ "<xf numFmtId=\"4\" fontId=\"3\" fillId=\"4\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"/>",
                /*10 note*/ "<xf numFmtId=\"0\" fontId=\"4\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>",
                /*11 libellé*/ "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"5\" borderId=\"1\" xfId=\"0\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment wrapText=\"1\" vertical=\"center\"/></xf>",
                /*12 valeur*/ "<xf numFmtId=\"0\" fontId=\"3\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"right\" vertical=\"center\"/></xf>",
                /*13 titre bloc*/ "<xf numFmtId=\"0\" fontId=\"5\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>",
                /*14 valeur alerte*/ "<xf numFmtId=\"0\" fontId=\"6\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"right\" vertical=\"center\"/></xf>",
                /*15 total entier*/ "<xf numFmtId=\"1\" fontId=\"3\" fillId=\"4\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"/>",
                /*16 sous-titre*/ "<xf numFmtId=\"0\" fontId=\"7\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>"
            };
            foreach (string x in xf) s.Append(x);
            s.Append("</cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>");
            return s.ToString();
        }

        static string SheetXml(SheetLayout L)
        {
            var s = new StringBuilder(Hdr);
            s.Append("<worksheet xmlns=\"" + NsMain + "\" xmlns:r=\"" + NsRel + "\">");
            s.Append("<sheetPr><pageSetUpPr fitToPage=\"1\"/></sheetPr>");
            s.Append("<dimension ref=\"A1:" + SheetLayout.ColName(Math.Max(L.MaxCol, 0)) + (Math.Max(L.MaxRow, 0) + 1) + "\"/>");
            s.Append("<sheetViews><sheetView workbookViewId=\"0\" showGridLines=\"0\"/></sheetViews>");
            s.Append("<sheetFormatPr defaultRowHeight=\"15\"/>");
            if (L.ColChars.Count > 0)
            {
                s.Append("<cols>");
                foreach (var kv in L.ColChars.OrderBy(k => k.Key))
                    s.Append("<col min=\"" + (kv.Key + 1) + "\" max=\"" + (kv.Key + 1) + "\" width=\"" + SheetLayout.Num(Math.Round(kv.Value, 1)) + "\" customWidth=\"1\"/>");
                s.Append("</cols>");
            }
            s.Append("<sheetData>");
            foreach (var row in L.Cells)
            {
                int r = row.Key + 1;
                bool tall = row.Value.Values.Any(c => c.Style == St.Header);
                s.Append("<row r=\"" + r + "\"" + (tall ? " ht=\"30\" customHeight=\"1\"" : "") + (row.Key == 0 ? " ht=\"26\" customHeight=\"1\"" : "") + ">");
                foreach (var cell in row.Value)
                {
                    string refc = SheetLayout.ColName(cell.Key) + r;
                    object v = cell.Value.Value;
                    int st = cell.Value.Style;
                    if (v == null) s.Append("<c r=\"" + refc + "\" s=\"" + st + "\"/>");
                    else if (v is string) s.Append("<c r=\"" + refc + "\" s=\"" + st + "\" t=\"inlineStr\"><is><t xml:space=\"preserve\">" + SheetLayout.Esc((string)v) + "</t></is></c>");
                    else if (v is DateTime) s.Append("<c r=\"" + refc + "\" s=\"" + st + "\"><v>" + ((int)((DateTime)v - new DateTime(1899, 12, 30)).TotalDays) + "</v></c>");
                    else if (v is int || v is long) s.Append("<c r=\"" + refc + "\" s=\"" + st + "\"><v>" + Convert.ToInt64(v) + "</v></c>");
                    else if (v is decimal) s.Append("<c r=\"" + refc + "\" s=\"" + st + "\"><v>" + SheetLayout.Num((decimal)v) + "</v></c>");
                    else if (v is double) s.Append("<c r=\"" + refc + "\" s=\"" + st + "\"><v>" + SheetLayout.Num((double)v) + "</v></c>");
                    else s.Append("<c r=\"" + refc + "\" s=\"" + st + "\" t=\"inlineStr\"><is><t xml:space=\"preserve\">" + SheetLayout.Esc(v.ToString()) + "</t></is></c>");
                }
                s.Append("</row>");
            }
            s.Append("</sheetData>");
            s.Append("<pageMargins left=\"0.5\" right=\"0.5\" top=\"0.6\" bottom=\"0.6\" header=\"0.3\" footer=\"0.3\"/>");
            s.Append("<pageSetup paperSize=\"8\" orientation=\"landscape\" fitToWidth=\"1\" fitToHeight=\"0\"/>");
            if (L.Charts.Count > 0) s.Append("<drawing r:id=\"rId1\"/>");
            s.Append("</worksheet>");
            return s.ToString();
        }

        static string DrawingXml(SheetLayout L)
        {
            var s = new StringBuilder(Hdr);
            s.Append("<xdr:wsDr xmlns:xdr=\"http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"" + NsRel + "\" xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\">");
            for (int k = 0; k < L.Charts.Count; k++)
            {
                ChartPlace cp = L.Charts[k];
                s.Append("<xdr:twoCellAnchor><xdr:from><xdr:col>" + cp.AnchorCol + "</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>" + cp.AnchorRow + "</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>");
                s.Append("<xdr:to><xdr:col>" + (cp.AnchorCol + SheetLayout.ChartCols) + "</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>" + (cp.AnchorRow + SheetLayout.ChartRows - 1) + "</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>");
                s.Append("<xdr:graphicFrame macro=\"\"><xdr:nvGraphicFramePr><xdr:cNvPr id=\"" + (k + 2) + "\" name=\"Graphique " + (k + 1) + "\"/><xdr:cNvGraphicFramePr/></xdr:nvGraphicFramePr>");
                s.Append("<xdr:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/></xdr:xfrm><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/chart\"><c:chart r:id=\"rId" + (k + 1) + "\"/></a:graphicData></a:graphic></xdr:graphicFrame><xdr:clientData/></xdr:twoCellAnchor>");
            }
            s.Append("</xdr:wsDr>");
            return s.ToString();
        }

        static string Ref(SheetLayout L, int col, int row1, int row2)
        {
            string sh = "'" + L.Name.Replace("'", "''") + "'!";
            string c = SheetLayout.ColName(col);
            if (row1 == row2) return sh + "$" + c + "$" + (row1 + 1);
            return sh + "$" + c + "$" + (row1 + 1) + ":$" + c + "$" + (row2 + 1);
        }

        static string ChartXml(SheetLayout L, ChartPlace cp)
        {
            Block b = cp.Block;
            var s = new StringBuilder(Hdr);
            s.Append("<c:chartSpace xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"" + NsRel + "\"><c:roundedCorners val=\"0\"/><c:chart>");
            s.Append("<c:title><c:tx><c:rich><a:bodyPr/><a:p><a:pPr><a:defRPr sz=\"1200\" b=\"1\"/></a:pPr><a:r><a:rPr lang=\"fr-FR\" sz=\"1200\" b=\"1\"/><a:t>" + SheetLayout.Esc(b.Title) + "</a:t></a:r></a:p></c:rich></c:tx><c:overlay val=\"0\"/></c:title>");
            s.Append("<c:autoTitleDeleted val=\"0\"/><c:plotArea><c:layout/>");
            s.Append("<c:barChart><c:barDir val=\"" + (b.Horizontal ? "bar" : "col") + "\"/><c:grouping val=\"clustered\"/><c:varyColors val=\"0\"/>");
            for (int i = 0; i < b.Series.Count; i++)
            {
                s.Append("<c:ser><c:idx val=\"" + i + "\"/><c:order val=\"" + i + "\"/>");
                s.Append("<c:tx><c:strRef><c:f>" + SheetLayout.Esc(Ref(L, 1 + i, cp.HeaderRow, cp.HeaderRow)) + "</c:f></c:strRef></c:tx>");
                s.Append("<c:spPr><a:solidFill><a:srgbClr val=\"" + b.Series[i].Color + "\"/></a:solidFill></c:spPr><c:invertIfNegative val=\"0\"/>");
                s.Append("<c:cat><c:strRef><c:f>" + SheetLayout.Esc(Ref(L, 0, cp.FirstRow, cp.LastRow)) + "</c:f></c:strRef></c:cat>");
                s.Append("<c:val><c:numRef><c:f>" + SheetLayout.Esc(Ref(L, 1 + i, cp.FirstRow, cp.LastRow)) + "</c:f></c:numRef></c:val></c:ser>");
            }
            s.Append("<c:gapWidth val=\"80\"/><c:axId val=\"111\"/><c:axId val=\"112\"/></c:barChart>");
            s.Append("<c:catAx><c:axId val=\"111\"/><c:scaling><c:orientation val=\"" + (b.Horizontal ? "maxMin" : "minMax") + "\"/></c:scaling><c:delete val=\"0\"/><c:axPos val=\"" + (b.Horizontal ? "l" : "b") + "\"/><c:numFmt formatCode=\"General\" sourceLinked=\"0\"/><c:majorTickMark val=\"out\"/><c:minorTickMark val=\"none\"/><c:tickLblPos val=\"low\"/><c:crossAx val=\"112\"/><c:crosses val=\"autoZero\"/><c:auto val=\"1\"/><c:lblAlgn val=\"ctr\"/><c:lblOffset val=\"100\"/><c:noMultiLvlLbl val=\"0\"/></c:catAx>");
            s.Append("<c:valAx><c:axId val=\"112\"/><c:scaling><c:orientation val=\"minMax\"/></c:scaling><c:delete val=\"0\"/><c:axPos val=\"" + (b.Horizontal ? "b" : "l") + "\"/><c:majorGridlines/><c:numFmt formatCode=\"#,##0\" sourceLinked=\"0\"/><c:majorTickMark val=\"out\"/><c:minorTickMark val=\"none\"/><c:tickLblPos val=\"nextTo\"/><c:crossAx val=\"111\"/><c:crosses val=\"" + (b.Horizontal ? "max" : "autoZero") + "\"/><c:crossBetween val=\"between\"/></c:valAx>");
            s.Append("</c:plotArea>");
            if (b.Series.Count > 1) s.Append("<c:legend><c:legendPos val=\"b\"/><c:overlay val=\"0\"/></c:legend>");
            s.Append("<c:plotVisOnly val=\"1\"/><c:dispBlanksAs val=\"gap\"/></c:chart></c:chartSpace>");
            return s.ToString();
        }
    }
}
