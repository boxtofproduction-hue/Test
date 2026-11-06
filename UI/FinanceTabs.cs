using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace GestionGym
{
    // ================================================================= panneau libellé / valeur
    public class KvPanel : Border
    {
        readonly Grid g = new Grid();

        public KvPanel()
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Child = g;
            Background = Brushes.White;
            BorderBrush = new SolidColorBrush(Color.FromRgb(210, 215, 225));
            BorderThickness = new Thickness(1);
            Padding = new Thickness(6);
            Margin = new Thickness(0, 0, 10, 10);
        }

        public void SetLines(IEnumerable<KV> lines)
        {
            g.Children.Clear();
            g.RowDefinitions.Clear();
            int r = 0;
            foreach (KV kv in lines)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var a = new TextBlock { Text = kv.Label, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 3, 16, 3), FontWeight = kv.Level == 2 ? FontWeights.Bold : FontWeights.Normal };
                Brush fg = kv.Level == 1 ? Ui.Red : (kv.Level == 2 ? Ui.Blue : Brushes.Black);
                var b = new TextBlock { Text = kv.Value ?? "", TextAlignment = TextAlignment.Right, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 3, 4, 3), FontWeight = kv.Level == 0 ? FontWeights.Normal : FontWeights.Bold, Foreground = fg };
                Grid.SetRow(a, r); Grid.SetColumn(a, 0);
                Grid.SetRow(b, r); Grid.SetColumn(b, 1);
                g.Children.Add(a); g.Children.Add(b);
                r++;
            }
        }
    }

    // ================================================================= utilitaires d'écran
    public static class TabKit
    {
        public static GridCol[] Cols(Block b)
        {
            var c = new GridCol[b.Headers.Length];
            for (int i = 0; i < c.Length; i++)
            {
                string f = i < b.Formats.Length ? b.Formats[i] : null;
                double w = i < b.Widths.Length ? b.Widths[i] : 90;
                bool right = f != null && f != "dd/MM/yyyy";
                if (f == "0.0") f = "N1";
                c[i] = new GridCol(b.Headers[i], w, f, right);
            }
            return c;
        }

        public static List<Row> Rows(Block b)
        {
            var l = new List<Row>();
            foreach (object[] r in b.Rows)
            {
                bool total = r.Length > 0 && (r[0] as string) == "TOTAL";
                l.Add(new Row { Cells = r, Tag = total ? "TOTAL" : null, Level = 0 });
            }
            return l;
        }

        public static StackPanel Toolbar(params Button[] b)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            foreach (Button x in b) sp.Children.Add(x);
            return sp;
        }

        public static TextBlock Note(string t)
        {
            return new TextBlock { Text = t, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Grey, FontSize = 11, Margin = new Thickness(0, 0, 0, 8) };
        }

        public static Database Blank() { return Database.CreateBlank(DateTime.Now.Year, ""); }
        public static double[] D(IEnumerable<decimal> v) { return v.Select(x => (double)x).ToArray(); }
        public static string[] Months() { return Enumerable.Range(1, 12).Select(m => Fmt.MonthName(m)).ToArray(); }
    }

    // ================================================================= fenêtre de paramètres générique
    public class SettingsDialog : DialogBase
    {
        readonly Database db;
        readonly List<FieldDef> fields;
        readonly Dictionary<string, Control> ctl = new Dictionary<string, Control>();

        public SettingsDialog(Database db, string title, List<FieldDef> fields, string intro) : base(title, 820)
        {
            this.db = db;
            this.fields = fields;
            if (!string.IsNullOrEmpty(intro)) AddBody(new TextBlock { Text = intro, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Grey, Margin = new Thickness(0, 0, 0, 8) });
            FormPanel form = null;
            string lastGroup = null;
            foreach (FieldDef f in fields)
            {
                if (form == null || f.Group != lastGroup)
                {
                    if (f.Group.Length > 0) AddBody(new TextBlock { Text = f.Group, FontWeight = FontWeights.Bold, Foreground = Ui.Blue, Margin = new Thickness(0, 10, 0, 2) });
                    form = new FormPanel(420);
                    AddBody(form);
                    lastGroup = f.Group;
                }
                Control c = MakeControl(f);
                ctl[f.Key] = c;
                string label = f.Label + (f.Kind == FKind.Pct ? "" : "");
                form.Add(label, c);
            }
            AddButton("Valeurs par défaut", ResetDefaults, false, false);
            AddButton("Enregistrer", Ok, true, false);
            AddButton("Annuler", () => { DialogResult = false; }, false, true);
        }

        Control MakeControl(FieldDef f)
        {
            string cur = Fld.Str(db, f.Key);
            switch (f.Kind)
            {
                case FKind.List:
                    {
                        var items = new List<string>(f.Options);
                        if (cur.Length > 0 && !items.Contains(cur)) items.Add(cur);
                        var cb = Ui.Combo(items, 300);
                        cb.SelectedItem = cur.Length > 0 ? cur : f.Default;
                        return cb;
                    }
                case FKind.Date:
                    {
                        DateTime d;
                        DatePicker dp = Ui.Date(Fmt.TryDate(cur, out d) ? (DateTime?)d : null);
                        return dp;
                    }
                default:
                    {
                        var tb = Ui.Text(160);
                        tb.Text = cur;
                        string tip = f.Kind == FKind.Pct ? "Pourcentage : saisis 21,2 pour 21,2 %" : (f.Kind == FKind.Money ? "Montant en euros" : "");
                        if (tip.Length > 0) tb.ToolTip = tip;
                        return tb;
                    }
            }
        }

        void ResetDefaults()
        {
            if (!Ui.Confirm("Remettre toutes les valeurs de cette fenêtre à leur valeur par défaut ?\n(Rien n'est enregistré tant que tu ne cliques pas sur Enregistrer.)")) return;
            foreach (FieldDef f in fields)
            {
                Control c = ctl[f.Key];
                var cb = c as ComboBox; var dp = c as DatePicker; var tb = c as TextBox;
                if (cb != null) cb.SelectedItem = f.Default;
                else if (dp != null) { DateTime d; dp.SelectedDate = Fmt.TryDate(f.Default, out d) ? (DateTime?)d : null; if (f.Default.Length == 0) dp.Text = ""; }
                else if (tb != null) tb.Text = f.Default;
            }
        }

        void Ok()
        {
            var vals = new Dictionary<string, string>();
            var errs = new List<string>();
            foreach (FieldDef f in fields)
            {
                Control c = ctl[f.Key];
                string v = "";
                var cb = c as ComboBox; var dp = c as DatePicker; var tb = c as TextBox;
                if (cb != null)
                {
                    v = (cb.SelectedItem as string) ?? "";
                    if (v.Length == 0) errs.Add(f.Label + " : choisis une valeur dans la liste.");
                }
                else if (dp != null)
                {
                    DateTime d;
                    string txt = (dp.Text ?? "").Trim();
                    if (dp.SelectedDate.HasValue) v = Fmt.Date(dp.SelectedDate.Value);
                    else if (txt.Length == 0) { if (!f.Optional) errs.Add(f.Label + " : choisis une date."); }
                    else if (Fmt.TryDate(txt, out d)) v = Fmt.Date(d);
                    else errs.Add(f.Label + " : date invalide (« " + txt + " »), format jj/mm/aaaa.");
                }
                else if (tb != null)
                {
                    string txt = (tb.Text ?? "").Trim();
                    if (txt.Length == 0)
                    {
                        if (!f.Optional) errs.Add(f.Label + " : valeur manquante.");
                    }
                    else if (f.Kind == FKind.Int)
                    {
                        int i;
                        if (!Fmt.TryInt(txt, out i)) errs.Add(f.Label + " : saisis un nombre entier.");
                        else if (i < f.Min || i > f.Max) errs.Add(f.Label + " : valeur entre " + Fmt.Dec(f.Min) + " et " + Fmt.Dec(f.Max) + ".");
                        else v = i.ToString();
                    }
                    else if (f.Kind == FKind.Text) v = Fmt.Clean(txt);
                    else
                    {
                        decimal d;
                        if (!Fmt.TryDec(txt, out d)) errs.Add(f.Label + " : saisis un nombre (virgule ou point).");
                        else if (d < f.Min || d > f.Max) errs.Add(f.Label + " : valeur entre " + Fmt.Dec(f.Min) + " et " + Fmt.Dec(f.Max) + ".");
                        else v = Fmt.Dec(d);
                    }
                }
                vals[f.Key] = v;
            }
            if (errs.Count > 0)
            {
                ShowMessage(string.Join("\n", errs.Take(8)) + (errs.Count > 8 ? "\n… et " + (errs.Count - 8) + " autre(s) erreur(s)." : ""), Sev.Error);
                return;
            }
            if (Ui.Try(() => db.SetSettings(vals))) DialogResult = true;
        }
    }

    // ================================================================= saisie d'une charge fixe
    public class ChargeDialog : DialogBase
    {
        readonly Database db;
        readonly int index;
        readonly TextBox tbLabel = Ui.Text(330), tbAmount = Ui.Text(110);
        readonly ComboBox cbCat = Ui.Combo(Fld.ChargeCats, 330), cbFreq = Ui.Combo(Fld.Frequences, 160);
        readonly DatePicker dpStart, dpEnd;

        public ChargeDialog(Database db, int index, Charge init) : base(index < 0 ? "Nouvelle charge fixe" : "Modifier la charge fixe", 640)
        {
            this.db = db;
            this.index = index;
            Charge c = init ?? new Charge { Start = new DateTime(db.Year, 1, 1) };
            dpStart = Ui.Date(c.Start);
            dpEnd = Ui.Date(c.End);
            var f = new FormPanel(190);
            f.Add("Libellé", tbLabel);
            f.Add("Catégorie", cbCat);
            f.Add("Montant (€)", tbAmount);
            f.Add("Fréquence", cbFreq);
            f.Add("Début (date)", dpStart);
            f.Add("Fin (vide = illimité)", dpEnd);
            AddBody(f);
            AddBody(TabKit.Note("Mensuel = chaque mois depuis le début ; trimestriel / semestriel / annuel = tous les 3 / 6 / 12 mois à partir du mois de début ; ponctuel = le mois de la date de début."));
            tbLabel.Text = c.Label;
            cbCat.SelectedItem = c.Category;
            tbAmount.Text = c.Amount == 0 ? "" : Fmt.Dec(c.Amount);
            cbFreq.SelectedItem = c.Frequency;
            tbLabel.TextChanged += (s, e) => Live();
            tbAmount.TextChanged += (s, e) => Live();
            cbFreq.SelectionChanged += (s, e) => Live();
            dpStart.SelectedDateChanged += (s, e) => Live();
            dpEnd.SelectedDateChanged += (s, e) => Live();
            AddButton("Enregistrer", Ok, true, false);
            AddButton("Annuler", () => { DialogResult = false; }, false, true);
            Live();
        }

        bool Read(out Charge c, out string error)
        {
            c = new Charge(); error = null;
            c.Label = Fmt.Clean(tbLabel.Text);
            c.Category = (cbCat.SelectedItem as string) ?? "Autres";
            c.Frequency = (cbFreq.SelectedItem as string) ?? "Mensuel";
            decimal a;
            if (!Ui.ReadDec(tbAmount, "Montant", 0, 100000000m, out a, out error)) return false;
            c.Amount = a;
            DateTime d;
            if (!Ui.ReadDate(dpStart, "Début", out d, out error)) return false;
            c.Start = d;
            if (dpEnd.SelectedDate.HasValue || (dpEnd.Text ?? "").Trim().Length > 0)
            {
                DateTime e;
                if (!Ui.ReadDate(dpEnd, "Fin", out e, out error)) return false;
                c.End = e;
            }
            error = db.ValidateCharge(c);
            return error == null;
        }

        void Live()
        {
            Charge c; string err;
            if (!Read(out c, out err)) { ShowMessage(tbLabel.Text.Trim().Length == 0 && tbAmount.Text.Trim().Length == 0 ? "" : err, Sev.Warning); return; }
            ShowMessage("✔ Total sur l'année " + db.Year + " : " + Fmt.Money(c.YearTotal(db.Year)), Sev.Ok);
        }

        void Ok()
        {
            Charge c; string err;
            if (!Read(out c, out err)) { ShowMessage(err, Sev.Error); return; }
            bool done = Ui.Try(() => { if (index < 0) db.AddCharge(c); else db.UpdateCharge(index, c); });
            if (done) DialogResult = true;
        }
    }

    // ================================================================= onglet Charges fixes
    public class ChargesTab : Grid, ITab
    {
        Database db;
        readonly DataPanel panel;
        readonly BarChart chMonth = new BarChart("Charges fixes par mois (€)", Ui.Red);
        readonly BarChart chCat = new BarChart("Charges par catégorie (€)", Ui.Orange);
        readonly TextBlock total = new TextBlock { FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 4) };

        public ChargesTab()
        {
            Margin = new Thickness(10);
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(250) });
            Block b = ReportBuilder.ChargesTable(TabKit.Blank());
            panel = new DataPanel(TabKit.Cols(b));
            panel.AddTool("Nouvelle charge", () => { if (Ui.CanEdit(db)) new ChargeDialog(db, -1, null).ShowDialog(); });
            panel.AddTool("Modifier", Edit);
            panel.AddTool("Dupliquer", Duplicate);
            panel.AddTool("Supprimer", Delete);
            panel.RowActivated += r => Edit();
            Children.Add(panel);
            SetRow(total, 1);
            Children.Add(total);
            var g = new UniformGrid { Columns = 2 };
            g.Children.Add(chMonth); g.Children.Add(chCat);
            SetRow(g, 2);
            Children.Add(g);
        }

        public void Bind(Database d) { db = d; Refresh(); }

        public void Refresh()
        {
            if (db == null) return;
            Block b = ReportBuilder.ChargesTable(db);
            var rows = new List<Row>();
            for (int i = 0; i < b.Rows.Count; i++)
            {
                bool isTotal = i == db.Charges.Count && db.Charges.Count > 0;
                rows.Add(new Row { Cells = b.Rows[i], Tag = isTotal ? (object)"TOTAL" : (object)i });
            }
            panel.SetRows(rows);
            decimal[] bm = Finance.ChargesByMonth(db);
            total.Text = "Total des charges fixes " + db.Year + " : " + Fmt.Money(bm.Sum()) + "   (" + Fmt.Plural(db.Charges.Count, "ligne", "lignes") + ")";
            chMonth.SetData(TabKit.Months(), TabKit.D(bm), "N0");
            var cc = Finance.ChargesByCategory(db);
            chCat.SetData(cc.Keys.ToArray(), cc.Values.Select(x => (double)x).ToArray(), "N0");
        }

        int Current()
        {
            Row r = panel.Selected;
            if (r == null || !(r.Tag is int)) { Ui.Warn("Sélectionne d'abord une charge dans la liste."); return -1; }
            return (int)r.Tag;
        }

        void Edit()
        {
            if (!Ui.CanEdit(db)) return;
            int i = Current();
            if (i >= 0) new ChargeDialog(db, i, db.Charges[i].Clone()).ShowDialog();
        }

        void Duplicate()
        {
            if (!Ui.CanEdit(db)) return;
            int i = Current();
            if (i < 0) return;
            Charge c = db.Charges[i].Clone();
            c.Label = c.Label + " (copie)";
            new ChargeDialog(db, -1, c).ShowDialog();
        }

        void Delete()
        {
            if (!Ui.CanEdit(db)) return;
            int i = Current();
            if (i < 0) return;
            if (Ui.Confirm("Supprimer la charge « " + db.Charges[i].Label + " » ?")) Ui.Try(() => db.DeleteCharge(i));
        }
    }

    // ================================================================= onglet URSSAF
    public class UrssafTab : ScrollViewer, ITab
    {
        Database db;
        readonly KvPanel kv = new KvPanel();
        readonly BarChart chCa = new BarChart("CA par mois (€)", Ui.Blue);
        readonly BarChart chCot = new BarChart("Cotisations par mois (€)", Ui.Orange);
        readonly BarChart chRes = new BarChart("Résultat avant impôt par mois (€)", Ui.Green);
        readonly DataPanel monthly;

        public UrssafTab()
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            var root = new StackPanel { Margin = new Thickness(10) };
            root.Children.Add(TabKit.Toolbar(
                Ui.Btn("Profil URSSAF…", () => Edit(Fld.Urssaf, "Profil URSSAF", "Renseigne ton statut et ta situation : l'onglet calcule cotisations, impôt estimé, plafonds et TVA.")),
                Ui.Btn("Taux et barèmes…", () => Edit(Fld.UrssafParams, "Taux et barèmes (2026 – à vérifier)", "Taux 2026 issus de sources secondaires : vérifie-les avant toute déclaration. Les pourcentages se saisissent sans le signe % (ex. 21,2)."))));
            root.Children.Add(TabKit.Note("Estimation indicative (impôt simplifié sans décote ni plafonnement du quotient familial). À valider avec un expert-comptable."));
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 420 });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(kv, 0);
            g.Children.Add(kv);
            var right = new StackPanel();
            right.Children.Add(chCa); right.Children.Add(chCot); right.Children.Add(chRes);
            Grid.SetColumn(right, 1);
            g.Children.Add(right);
            root.Children.Add(g);
            monthly = new DataPanel(TabKit.Cols(ReportBuilder.UrssafMonthlyTable(Finance.Urssaf(TabKit.Blank()))));
            monthly.Height = 410;
            root.Children.Add(monthly);
            Content = root;
        }

        void Edit(List<FieldDef> f, string title, string intro)
        {
            if (!Ui.CanEdit(db)) return;
            new SettingsDialog(db, title, f, intro).ShowDialog();
        }

        public void Bind(Database d) { db = d; Refresh(); }

        public void Refresh()
        {
            if (db == null) return;
            UrssafResult u = Finance.Urssaf(db);
            kv.SetLines(u.Lines());
            string[] m = TabKit.Months();
            chCa.SetData(m, TabKit.D(u.Months.Select(x => x.Ca)), "N0");
            chCot.SetData(m, TabKit.D(u.Months.Select(x => x.Total)), "N0");
            chRes.SetData(m, TabKit.D(u.Months.Select(x => x.ResAv)), "N0");
            monthly.SetRows(TabKit.Rows(ReportBuilder.UrssafMonthlyTable(u)));
        }
    }

    // ================================================================= onglet CFE
    public class CfeTab : ScrollViewer, ITab
    {
        Database db;
        readonly KvPanel kv = new KvPanel();
        readonly BarChart ch = new BarChart("CFE à payer par mois (€)", Ui.Orange);

        public CfeTab()
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            var root = new StackPanel { Margin = new Thickness(10) };
            root.Children.Add(TabKit.Toolbar(
                Ui.Btn("Profil CFE…", () => Edit(Fld.Cfe, "Profil CFE", "Reprends les chiffres de ton avis de CFE : valeur locative, taux, base minimale.")),
                Ui.Btn("Barème et paramètres…", () => Edit(Fld.CfeParams, "Barème de base minimale et paramètres CFE", "Les 3 premières tranches viennent d'un article 2026 ; les 3 dernières sont à VÉRIFIER. Ta commune fixe la base dans la fourchette."))));
            root.Children.Add(TabKit.Note("Exonérée l'année de création ; base réduite de 50 % l'année suivante ; exonération si CA N-2 ≤ 5 000 € sans local. Avec un local, la CFE repose sur la valeur locative (au minimum la base minimale). Estimation indicative."));
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 420 });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(kv, 0);
            g.Children.Add(kv);
            Grid.SetColumn(ch, 1);
            g.Children.Add(ch);
            root.Children.Add(g);
            Content = root;
        }

        void Edit(List<FieldDef> f, string title, string intro)
        {
            if (!Ui.CanEdit(db)) return;
            new SettingsDialog(db, title, f, intro).ShowDialog();
        }

        public void Bind(Database d) { db = d; Refresh(); }

        public void Refresh()
        {
            if (db == null) return;
            UrssafResult u = Finance.Urssaf(db);
            CfeResult c = Finance.Cfe(db, u);
            kv.SetLines(c.Lines());
            ch.SetData(TabKit.Months(), TabKit.D(c.Months), "N0");
        }
    }

    // ================================================================= onglet Rentabilité clients
    public class RentaTab : ScrollViewer, ITab
    {
        Database db;
        RentaResult result;
        readonly KvPanel kv = new KvPanel();
        readonly DataPanel clients;
        readonly DataPanel segPanel;
        readonly ComboBox cbSeg = Ui.Combo(new string[0], 260);
        readonly BarChart chSeg = new BarChart("Revenu et marge par segment (€)", Ui.Blue);
        bool filling;

        public RentaTab()
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            var root = new StackPanel { Margin = new Thickness(10) };
            root.Children.Add(TabKit.Toolbar(Ui.Btn("Paramètres de fidélité…", EditParams)));
            root.Children.Add(TabKit.Note("Revenu = packs encaissés dans l'année. Les charges fixes sont réparties par présence ; les cotisations suivent le taux effectif de l'onglet URSSAF. Les remises proposées protègent une marge minimale."));
            root.Children.Add(kv);
            Block cb = ReportBuilder.RentaClientsTable(new RentaResult());
            clients = new DataPanel(TabKit.Cols(cb));
            clients.Height = 430;
            root.Children.Add(new TextBlock { Text = "Détail par client (clique sur un en-tête pour trier ; le rang est la position par marge nette)", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 6, 0, 4) });
            root.Children.Add(clients);
            var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 6) };
            top.Children.Add(new TextBlock { Text = "Analyse par segment : ", FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
            top.Children.Add(cbSeg);
            root.Children.Add(top);
            segPanel = new DataPanel(TabKit.Cols(ReportBuilder.SegmentTable("x", new List<Segment>())));
            segPanel.Height = 250;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(segPanel, 0); g.Children.Add(segPanel);
            Grid.SetColumn(chSeg, 1); chSeg.Height = 250; chSeg.Margin = new Thickness(10, 0, 0, 0); g.Children.Add(chSeg);
            root.Children.Add(g);
            cbSeg.SelectionChanged += (s, e) => { if (!filling) ShowSegment(); };
            Content = root;
        }

        void EditParams()
        {
            if (!Ui.CanEdit(db)) return;
            new SettingsDialog(db, "Paramètres de fidélité et de remise", Fld.Renta, "Ces paramètres servent à repérer les clients fidèles et à proposer une remise sans entamer la marge.").ShowDialog();
        }

        public void Bind(Database d) { db = d; Refresh(); }

        public void Refresh()
        {
            if (db == null) return;
            UrssafResult u = Finance.Urssaf(db);
            result = Finance.Renta(db, u);
            kv.SetLines(result.Lines());
            clients.SetRows(TabKit.Rows(ReportBuilder.RentaClientsTable(result)));
            string keep = cbSeg.SelectedItem as string;
            filling = true;
            cbSeg.Items.Clear();
            foreach (string k in result.Segments.Keys) cbSeg.Items.Add(k);
            cbSeg.SelectedItem = keep != null && result.Segments.ContainsKey(keep) ? keep : result.Segments.Keys.First();
            filling = false;
            ShowSegment();
        }

        void ShowSegment()
        {
            string k = cbSeg.SelectedItem as string;
            if (result == null || k == null || !result.Segments.ContainsKey(k)) return;
            var segs = result.Segments[k];
            segPanel.SetRows(TabKit.Rows(ReportBuilder.SegmentTable(k, segs)));
            chSeg.SetData(segs.Select(s => s.Name).ToArray(), segs.Select(s => (double)Math.Max(0, s.Revenue)).ToArray(), "N0");
        }
    }

    // ================================================================= onglet Tracking des abonnements
    public class TrackingTab : Grid, ITab
    {
        Database db;
        readonly DataPanel panel;

        public TrackingTab()
        {
            Margin = new Thickness(10);
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Children.Add(TabKit.Note("Une ligne par achat de pack : période, statut, crédits et revenu encaissé mois par mois (base caisse : une échéance de paiement par mois ou par an selon le pack)."));
            panel = new DataPanel(TabKit.Cols(ReportBuilder.TrackingTable(TabKit.Blank())));
            SetRow(panel, 1);
            Children.Add(panel);
        }

        public void Bind(Database d) { db = d; Refresh(); }

        public void Refresh()
        {
            if (db == null) return;
            panel.SetRows(TabKit.Rows(ReportBuilder.TrackingTable(db)));
        }
    }

    // ================================================================= onglet Reporting
    public class ReportingTab : ScrollViewer, ITab
    {
        Database db;
        readonly WrapPanel cards = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        readonly BarChart chCa = new BarChart("CA par mois (€)", Ui.Blue);
        readonly BarChart chCharges = new BarChart("Charges fixes par mois (€)", Ui.Red);
        readonly BarChart chCot = new BarChart("Cotisations par mois (€)", Ui.Orange);
        readonly BarChart chRes = new BarChart("Résultat avant impôt par mois (€)", Ui.Green);
        readonly BarChart chCat = new BarChart("Charges fixes par catégorie (€)", Ui.Red);
        readonly BarChart chFill = new BarChart("Remplissage par mois (%)", Ui.Blue);
        readonly DataPanel monthly;

        public ReportingTab()
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            var root = new StackPanel { Margin = new Thickness(10) };
            root.Children.Add(cards);
            var g = new UniformGrid { Columns = 2 };
            g.Children.Add(chCa); g.Children.Add(chCharges); g.Children.Add(chCot); g.Children.Add(chRes); g.Children.Add(chFill); g.Children.Add(chCat);
            root.Children.Add(g);
            monthly = new DataPanel(TabKit.Cols(ReportBuilder.MonthlyTable(TabKit.Blank(), Finance.Urssaf(TabKit.Blank()))));
            monthly.Height = 400;
            root.Children.Add(monthly);
            root.Children.Add(TabKit.Note("Pour obtenir ce rapport en Excel, OpenOffice ou PDF : menu Fichier > Exporter."));
            Content = root;
        }

        public void Bind(Database d) { db = d; Refresh(); }

        public void Refresh()
        {
            cards.Children.Clear();
            if (db == null) return;
            UrssafResult u = Finance.Urssaf(db);
            CfeResult c = Finance.Cfe(db, u);
            RentaResult r = Finance.Renta(db, u);
            cards.Children.Add(Ui.Card("CA retenu", Fmt.Money0(u.Ca), Ui.Blue));
            cards.Children.Add(Ui.Card("Charges fixes", Fmt.Money0(u.Total.Charges), Ui.Red));
            cards.Children.Add(Ui.Card("Cotisations URSSAF", Fmt.Money0(u.Total.Total), Ui.Orange));
            cards.Children.Add(Ui.Card("Impôt estimé", Fmt.Money0(u.Impot), Ui.Orange));
            cards.Children.Add(Ui.Card("Résultat net", Fmt.Money0(u.Net), u.Net < 0 ? Ui.Red : Ui.Green));
            cards.Children.Add(Ui.Card("CFE de l'année", Fmt.Money0(c.Total), Ui.Orange));
            cards.Children.Add(Ui.Card("Seuil de rentabilité (CA/mois)", Fmt.Money0(u.Seuil), Ui.Blue));
            cards.Children.Add(Ui.Card("Remise fidélité conseillée", Fmt.Dec(r.Advised * 100) + " %", Ui.Green));
            string[] m = TabKit.Months();
            chCa.SetData(m, TabKit.D(u.Months.Select(x => x.Ca)), "N0");
            chCharges.SetData(m, TabKit.D(u.Months.Select(x => x.Charges)), "N0");
            chCot.SetData(m, TabKit.D(u.Months.Select(x => x.Total)), "N0");
            chRes.SetData(m, TabKit.D(u.Months.Select(x => x.ResAv)), "N0");
            var fill = new double[12];
            for (int i = 1; i <= 12; i++)
            {
                int ses = db.Sessions.Count(s => s.Date.Year == db.Year && s.Date.Month == i);
                int pres = db.Attendances.Count(a => a.Date.Year == db.Year && a.Date.Month == i);
                fill[i - 1] = ses == 0 ? 0 : pres * 100.0 / (ses * (double)db.Capacity);
            }
            chFill.SetData(m, fill, "N0");
            var cc = Finance.ChargesByCategory(db);
            chCat.SetData(cc.Keys.ToArray(), cc.Values.Select(x => (double)x).ToArray(), "N0");
            monthly.SetRows(TabKit.Rows(ReportBuilder.MonthlyTable(db, u)));
        }
    }
}
