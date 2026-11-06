using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GestionGym
{
    // ================================================================= export
    public class ExportDialog : DialogBase
    {
        readonly Database db;
        readonly CheckBox cbX = new CheckBox { Content = "Excel (.xlsx) – tableaux mis en forme et graphiques", Margin = new Thickness(0, 4, 0, 4) };
        readonly CheckBox cbO = new CheckBox { Content = "OpenOffice / LibreOffice (.ods) – tableaux et graphiques", Margin = new Thickness(0, 4, 0, 4) };
        readonly CheckBox cbP = new CheckBox { Content = "PDF – un onglet par page, titre sur chaque page, graphiques", Margin = new Thickness(0, 4, 0, 4) };
        readonly ComboBox cbSize = Ui.Combo(new[] { "A3 paysage (recommandé)", "A4 paysage" }, 240);
        string lastFolder;

        public ExportDialog(Database db, string preselect) : base("Exporter les données", 700)
        {
            this.db = db;
            cbX.IsChecked = preselect == null || preselect == "xlsx";
            cbO.IsChecked = preselect == "ods";
            cbP.IsChecked = preselect == "pdf";
            AddBody(new TextBlock { Text = "L'export contient toutes les données de la base " + db.Year + " (clients, packs, achats, présences, planning, charges, tracking), les calculs (URSSAF, CFE, rentabilité, reporting), les graphiques et la configuration.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
            AddBody(cbX); AddBody(cbO); AddBody(cbP);
            var f = new FormPanel(160);
            f.Add("Format de page du PDF", cbSize);
            cbSize.SelectedIndex = 0;
            AddBody(f);
            AddBody(TabKit.Note("Les fichiers sont créés dans les dossiers « Export Excel », « Export OpenOffice » et « Export PDF » à côté de l'application, avec la date et l'heure dans le nom : aucun export précédent n'est écrasé. Les modifications non enregistrées sont incluses."));
            AddButton("Exporter", Run, true, false);
            AddButton("Ouvrir le dossier", OpenFolder, false, false);
            AddButton("Fermer", () => { DialogResult = true; }, false, true);
        }

        void OpenFolder()
        {
            try { Process.Start("explorer.exe", "\"" + (lastFolder ?? AppPaths.Root) + "\""); }
            catch (Exception ex) { Ui.Error("Impossible d'ouvrir le dossier :\n" + ex.Message); }
        }

        void Run()
        {
            bool x = cbX.IsChecked == true, o = cbO.IsChecked == true, p = cbP.IsChecked == true;
            if (!x && !o && !p) { ShowMessage("Coche au moins un format.", Sev.Error); return; }
            var done = new List<string>();
            var failed = new List<string>();
            Cursor old = Mouse.OverrideCursor;
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                List<Sheet> sheets = ReportBuilder.Build(db);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
                string baseName = "GestionGym_" + db.Year + "_" + stamp;
                string sub = "Base " + db.Year + " – export du " + DateTime.Now.ToString("dd/MM/yyyy HH:mm");
                if (x) Try("Excel", Path.Combine(AppPaths.ExportExcel, baseName + ".xlsx"), path => XlsxWriter.Write(path, sheets, sub), done, failed);
                if (o) Try("OpenOffice", Path.Combine(AppPaths.ExportOpenOffice, baseName + ".ods"), path => OdsWriter.Write(path, sheets, sub), done, failed);
                if (p) Try("PDF", Path.Combine(AppPaths.ExportPdf, baseName + ".pdf"), path => PdfBuilder.Write(path, sheets, sub, "Gestion GYM – base " + db.Year, cbSize.SelectedIndex == 0), done, failed);
            }
            catch (Exception ex) { failed.Add("Préparation des données : " + ex.Message); }
            finally { Mouse.OverrideCursor = old; }
            var msg = new List<string>();
            if (done.Count > 0) msg.Add("✔ Créé : " + string.Join("\n✔ Créé : ", done));
            if (failed.Count > 0) msg.Add("✖ Échec : " + string.Join("\n✖ Échec : ", failed));
            ShowMessage(string.Join("\n", msg), failed.Count > 0 ? Sev.Error : Sev.Ok);
        }

        void Try(string label, string path, Action<string> write, List<string> done, List<string> failed)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                write(path);
                done.Add(path);
                lastFolder = Path.GetDirectoryName(path);
            }
            catch (IOException ex) { failed.Add(label + " : fichier verrouillé ou disque plein (" + ex.Message + ")"); }
            catch (UnauthorizedAccessException) { failed.Add(label + " : accès refusé au dossier d'export."); }
            catch (Exception ex) { failed.Add(label + " : " + ex.Message); }
        }
    }

    // ================================================================= import
    public class ImportDialog : DialogBase
    {
        class Item
        {
            public SheetData Sheet;
            public DetectedTable Det;
            public CheckBox Chk;
            public ComboBox Target;
            public TextBlock Info;
            public Button Preview;
        }

        const string Ignore = "(ne pas importer)";
        readonly Database db;
        readonly string path;
        readonly List<Item> items = new List<Item>();
        readonly CheckBox cbAuto = new CheckBox { Content = "Créer automatiquement les activités et créneaux inconnus", IsChecked = true, Margin = new Thickness(0, 3, 0, 3) };
        readonly CheckBox cbLink = new CheckBox { Content = "Rattacher automatiquement les présences à un pack du client", IsChecked = true, Margin = new Thickness(0, 3, 0, 3) };
        readonly CheckBox cbCsv = new CheckBox { Content = "Conserver les données converties en CSV (dossier Actuel\\_import)", IsChecked = true, Margin = new Thickness(0, 3, 0, 3) };

        public ImportDialog(Database db, string path, List<SheetData> sheets) : base("Importer : " + Path.GetFileName(path), 980)
        {
            this.db = db;
            this.path = path;
            int found = 0;
            foreach (SheetData sh in sheets)
            {
                var it = new Item { Sheet = sh, Det = Importer.Detect(sh, null) };
                if (it.Det != null) found++;
                items.Add(it);
            }
            AddBody(new TextBlock
            {
                Text = found + " tableau(x) reconnu(s) automatiquement sur " + sheets.Count + " feuille(s). Vérifie le type de données de chaque feuille, décoche ce que tu ne veux pas importer, puis clique sur « Importer ». " +
                       "Les données sont ajoutées à la base ouverte (les doublons sont ignorés) et restent entièrement modifiables dans l'application ; rien n'est enregistré sur disque avant Ctrl+S, tu peux donc fermer sans enregistrer pour annuler.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });
            foreach (Item it in items) AddBody(Build(it));
            AddBody(new TextBlock { Text = "Options", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 2) });
            AddBody(cbAuto); AddBody(cbLink); AddBody(cbCsv);
            AddBody(TabKit.Note("Ordre d'import : clients, packs, achats, présences, charges, planning. Pour importer des achats ou des présences, les clients et packs correspondants doivent exister (importe-les dans le même fichier ou avant). Les dates doivent être dans l'année de la base (" + db.Year + ") pour les présences et le planning."));
            AddButton("Importer", Run, true, false);
            AddButton("Annuler", () => { DialogResult = false; }, false, true);
            foreach (Item it in items) Refresh(it);
        }

        UIElement Build(Item it)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            it.Chk = new CheckBox { Margin = new Thickness(0, 4, 8, 0), VerticalAlignment = VerticalAlignment.Top };
            var names = new List<string> { Ignore };
            names.AddRange(Importer.Targets.Select(t => t.Label));
            it.Target = Ui.Combo(names, 175);
            it.Target.VerticalAlignment = VerticalAlignment.Top;
            it.Info = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 4, 8, 0) };
            it.Preview = Ui.Btn("Aperçu…", () => ShowPreview(it));
            it.Preview.VerticalAlignment = VerticalAlignment.Top;
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            var title = new TextBlock { Text = "Feuille « " + it.Sheet.Name + " »", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 8, 0) };
            var left = new StackPanel();
            left.Children.Add(it.Target);
            Grid.SetColumn(it.Chk, 0); Grid.SetColumn(left, 1); Grid.SetColumn(it.Info, 2); Grid.SetColumn(it.Preview, 3);
            g.Children.Add(it.Chk); g.Children.Add(left); g.Children.Add(it.Info); g.Children.Add(it.Preview);
            var card = new Border { Background = System.Windows.Media.Brushes.White, BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(210, 215, 225)), BorderThickness = new Thickness(1), Padding = new Thickness(8) };
            var sp = new StackPanel();
            sp.Children.Add(title);
            sp.Children.Add(g);
            card.Child = sp;
            it.Target.SelectionChanged += (s, e) => OnTarget(it);
            it.Chk.Click += (s, e) => { if (it.Chk.IsChecked == true && it.Det == null) { it.Chk.IsChecked = false; } };
            return card;
        }

        bool loading;

        void Refresh(Item it)
        {
            loading = true;
            if (it.Det == null)
            {
                it.Target.SelectedItem = Ignore;
                it.Chk.IsChecked = false;
                it.Chk.IsEnabled = false;
                it.Preview.IsEnabled = false;
                it.Info.Text = it.Sheet.Rows.Count == 0 ? "Feuille vide." : "Feuille non reconnue : aucun en-tête de colonnes correspondant (Nom, Prénom, Pack, Date, Créneau, Montant…). Choisis un type de données si elle en contient.";
                it.Info.Foreground = Ui.Grey;
            }
            else
            {
                it.Target.SelectedItem = it.Det.Target.Label;
                it.Chk.IsEnabled = true;
                it.Chk.IsChecked = it.Det.Selected && it.Det.Rows.Count > 0;
                it.Preview.IsEnabled = true;
                string[] header = it.Sheet.Rows[it.Det.HeaderRow];
                it.Info.Text = Fmt.Plural(it.Det.Rows.Count, "ligne de données", "lignes de données") + (it.Det.Truncated ? " (fichier tronqué à " + FileReaders.MaxRows + " lignes)" : "") + "  –  en-têtes en ligne " + (it.Det.HeaderRow + 1) + "\nColonnes reconnues : " + it.Det.MappingText(header);
                it.Info.Foreground = it.Det.Rows.Count == 0 ? Ui.Red : System.Windows.Media.Brushes.Black;
            }
            loading = false;
        }

        void OnTarget(Item it)
        {
            if (loading) return;
            string label = it.Target.SelectedItem as string;
            if (label == null || label == Ignore) { it.Det = null; Refresh(it); it.Info.Text = "Feuille ignorée."; return; }
            ImportTargetDef t = Importer.Targets.First(x => x.Label == label);
            DetectedTable d = Importer.Detect(it.Sheet, t.Key);
            if (d == null)
            {
                it.Det = null;
                Refresh(it);
                it.Target.SelectedItem = label;
                it.Info.Text = "Impossible : colonnes obligatoires introuvables pour « " + label + " » (" + string.Join(", ", t.Required) + ").";
                it.Info.Foreground = Ui.Red;
                return;
            }
            it.Det = d;
            Refresh(it);
        }

        void ShowPreview(Item it)
        {
            if (it.Det == null) return;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(string.Join(" | ", it.Det.Target.Canon));
            sb.AppendLine(new string('-', 60));
            foreach (string[] r in it.Det.Rows.Take(25)) sb.AppendLine(string.Join(" | ", r));
            if (it.Det.Rows.Count > 25) sb.AppendLine("… et " + (it.Det.Rows.Count - 25) + " autre(s) ligne(s).");
            Ui.ShowText("Aperçu des données converties – " + it.Sheet.Name, sb.ToString());
        }

        void Run()
        {
            var chosen = new List<DetectedTable>();
            foreach (Item it in items)
            {
                if (it.Det == null) continue;
                it.Det.Selected = it.Chk.IsChecked == true;
                if (it.Det.Selected) chosen.Add(it.Det);
            }
            if (chosen.Count == 0) { ShowMessage("Rien à importer : coche au moins une feuille reconnue.", Sev.Error); return; }
            if (!Ui.Confirm("Importer " + chosen.Sum(t => t.Rows.Count) + " ligne(s) dans la base " + db.Year + " ?")) return;
            ImportReport rep = null;
            var csvs = new List<string>();
            Cursor old = Mouse.OverrideCursor;
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                if (cbCsv.IsChecked == true)
                {
                    string folder = Path.Combine(AppPaths.Actuel, "_import");
                    foreach (DetectedTable t in chosen)
                    {
                        try { csvs.Add(Importer.SaveCsv(t, folder, path)); } catch { }
                    }
                }
                rep = Importer.Apply(db, chosen, new Importer.Options { AutoAddLists = cbAuto.IsChecked == true, LinkAttendances = cbLink.IsChecked == true });
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = old;
                Ui.Error("L'import a échoué :\n" + ex.Message + "\n\nLes lignes déjà importées restent en mémoire : vérifie-les, ou ferme la base sans enregistrer pour tout annuler.");
                return;
            }
            Mouse.OverrideCursor = old;
            var text = new System.Text.StringBuilder();
            text.AppendLine("Import terminé : " + rep.Added + " ajouté(s), " + rep.Skipped + " ignoré(s) (déjà présents), " + rep.Rejected + " rejeté(s).");
            text.AppendLine();
            foreach (string l in rep.Lines) text.AppendLine("• " + l);
            if (rep.Problems.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("Lignes rejetées (corrige-les dans le fichier source puis réimporte-les, ou saisis-les à la main) :");
                foreach (string p in rep.Problems) text.AppendLine("  – " + p);
            }
            if (csvs.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("Copies CSV converties :");
                foreach (string c in csvs) text.AppendLine("  " + c);
            }
            text.AppendLine();
            text.AppendLine("Pense à enregistrer (Ctrl+S) pour conserver l'import. Les anomalies éventuelles (crédits dépassés, packs expirés…) apparaissent en couleur dans les onglets Achats et Présences.");
            Ui.ShowText("Résultat de l'import", text.ToString());
            DialogResult = true;
        }
    }
}
