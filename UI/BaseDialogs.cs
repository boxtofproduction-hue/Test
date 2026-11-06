using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace GestionGym
{
    // ============================================================ nouvelle base
    public class NewBaseDialog : DialogBase
    {
        readonly TextBox tbYear = Ui.Text(90);
        public int Year;

        public NewBaseDialog(int suggested, string title) : base(title, 520)
        {
            var l = Ui.Lbl("Une nouvelle base est créée avec les activités, créneaux et packs par défaut (modifiables ensuite), dans le dossier « Actuel ».");
            l.Margin = new Thickness(0, 0, 0, 10);
            AddBody(l);
            var f = new FormPanel(120);
            tbYear.Text = suggested.ToString();
            f.Add("Année de la base", tbYear);
            AddBody(f);
            AddButton("Créer la base", () =>
            {
                int y; string err;
                if (!Ui.ReadInt(tbYear, "Année", 2000, 2200, out y, out err)) { Ui.Error(err); return; }
                Year = y;
                DialogResult = true;
            }, true, false);
            AddButton("Annuler", () => { DialogResult = false; }, false, true);
            Loaded += (s, e) => { tbYear.Focus(); tbYear.SelectAll(); };
        }
    }

    // ============================================================ ouvrir une base
    public class OpenBaseDialog : DialogBase
    {
        readonly ListBox list = new ListBox { Height = 240 };
        readonly CheckBox chkRead = new CheckBox { Content = "Ouvrir en lecture seule (recommandé pour une archive)", IsChecked = true, Margin = new Thickness(0, 8, 0, 0) };
        readonly List<BaseInfo> bases;
        public string ResultFolder;
        public bool ResultArchived;
        public bool ResultReadOnly;

        public OpenBaseDialog(Database current) : base("Ouvrir une base", 700)
        {
            bases = BaseManager.Scan().Where(b => current == null || !string.Equals(Path.GetFullPath(b.Folder), Path.GetFullPath(current.Folder), StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(b => b.Year).ThenBy(b => b.Archived).ToList();
            AddBody(Ui.Lbl("Bases trouvées dans les dossiers « Actuel » et « Archive » (la base ouverte n'est pas listée) :"));
            foreach (BaseInfo b in bases) list.Items.Add(b.Display);
            list.Margin = new Thickness(0, 6, 0, 0);
            AddBody(list);
            AddBody(chkRead);
            list.SelectionChanged += (s, e) =>
            {
                int i = list.SelectedIndex;
                if (i >= 0 && i < bases.Count) chkRead.IsChecked = bases[i].Archived;
            };
            list.MouseDoubleClick += (s, e) => OpenSelected();
            if (bases.Count == 0) ShowMessage("Aucune autre base trouvée. Utilise « Parcourir un autre dossier… » pour en désigner une.", Sev.Warning);
            AddButton("Ouvrir", OpenSelected, true, false);
            AddButton("Parcourir un autre dossier…", Browse, false, false);
            AddButton("Annuler", () => { DialogResult = false; }, false, true);
        }

        void OpenSelected()
        {
            int i = list.SelectedIndex;
            if (i < 0 || i >= bases.Count) { Ui.Warn("Sélectionne une base dans la liste."); return; }
            ResultFolder = bases[i].Folder;
            ResultArchived = bases[i].Archived;
            ResultReadOnly = chkRead.IsChecked == true;
            DialogResult = true;
        }

        void Browse()
        {
            var fb = new System.Windows.Forms.FolderBrowserDialog { Description = "Choisis le dossier d'une base (il doit contenir le fichier meta.csv)", SelectedPath = AppPaths.Root };
            if (fb.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            if (!Database.LooksLikeBase(fb.SelectedPath)) { Ui.Error("Ce dossier ne contient pas de base (fichier meta.csv introuvable)."); return; }
            ResultFolder = fb.SelectedPath;
            ResultArchived = false;
            ResultReadOnly = chkRead.IsChecked == true;
            DialogResult = true;
        }
    }

    // ============================================================ planifier l'année suivante
    public class PlanNextDialog : DialogBase
    {
        readonly Database db;
        readonly TextBox tbYear = Ui.Text(90);
        readonly List<CheckBox> checks = new List<CheckBox>();
        readonly CheckBox chkAllClients = new CheckBox { Content = "Reporter tous les clients (sans leurs présences)", Margin = new Thickness(0, 4, 0, 0) };
        readonly CheckBox chkPurchases = new CheckBox { Content = "Reporter les achats encore en cours (abonnements, packs non expirés) et leurs clients", IsChecked = true, Margin = new Thickness(0, 4, 0, 0) };
        readonly CheckBox chkArchive = new CheckBox { Content = "Archiver une copie de la base actuelle dans le dossier Archive", IsChecked = true, Margin = new Thickness(0, 4, 0, 0) };
        readonly CheckBox chkOpen = new CheckBox { Content = "Ouvrir la nouvelle base après sa création", IsChecked = true, Margin = new Thickness(0, 4, 0, 0) };
        public PlanOptions Options;
        public bool OpenAfter;

        public PlanNextDialog(Database db) : base("Planifier une base pour l'année suivante", 640)
        {
            this.db = db;
            var f = new FormPanel(140);
            tbYear.Text = (db.Year + 1).ToString();
            f.Add("Année à planifier", tbYear);
            AddBody(f);
            var t = Ui.Lbl("Configuration à copier depuis la base " + db.Year + " :", true);
            t.Margin = new Thickness(0, 10, 0, 4);
            AddBody(t);
            foreach (ConfigSection sec in ConfigImport.Sections)
            {
                var cb = new CheckBox { Content = sec.Label + "  –  " + sec.Help, IsChecked = true, Margin = new Thickness(0, 2, 0, 0), Tag = sec.Key };
                checks.Add(cb);
                AddBody(cb);
            }
            var t2 = Ui.Lbl("Reports :", true);
            t2.Margin = new Thickness(0, 10, 0, 2);
            AddBody(t2);
            AddBody(chkPurchases);
            AddBody(chkAllClients);
            AddBody(chkArchive);
            AddBody(chkOpen);
            ShowMessage("Les crédits déjà consommés des packs reportés sont conservés. Les présences ne sont pas reportées.", Sev.Ok);
            AddButton("Créer la base", Go, true, false);
            AddButton("Annuler", () => { DialogResult = false; }, false, true);
        }

        void Go()
        {
            int y; string err;
            if (!Ui.ReadInt(tbYear, "Année", 2000, 2200, out y, out err)) { Ui.Error(err); return; }
            if (y == db.Year) { Ui.Error("L'année planifiée doit être différente de l'année de la base ouverte (" + db.Year + ")."); return; }
            BaseInfo ex = BaseManager.ActiveBaseFor(y);
            if (ex != null && !Ui.Confirm("Une base " + y + " existe déjà dans « Actuel » :\n" + ex.Folder + "\n\nCréer quand même une nouvelle base " + y + " à côté ? (l'existante ne sera pas modifiée)")) return;
            var o = new PlanOptions { NewYear = y, CarryAllClients = chkAllClients.IsChecked == true, CarryActivePurchases = chkPurchases.IsChecked == true, ArchiveCurrent = chkArchive.IsChecked == true };
            foreach (CheckBox c in checks) if (c.IsChecked == true) o.Sections.Add((string)c.Tag);
            Options = o;
            OpenAfter = chkOpen.IsChecked == true;
            DialogResult = true;
        }
    }

    // ============================================================ importer une configuration
    public class ImportConfigDialog : DialogBase
    {
        readonly Database target;
        readonly ComboBox cbSource;
        readonly List<BaseInfo> bases;
        readonly List<CheckBox> checks = new List<CheckBox>();
        readonly RadioButton rbMerge = new RadioButton { Content = "Fusionner : ajouter seulement ce qui manque (ne remplace rien)", IsChecked = true, Margin = new Thickness(0, 4, 0, 0) };
        readonly RadioButton rbReplace = new RadioButton { Content = "Remplacer : la configuration importée écrase celle de la base ouverte", Margin = new Thickness(0, 4, 0, 0) };
        readonly CheckBox chkAlign = new CheckBox { Content = "Programme : conserver le même jour de la semaine (décale les dates de quelques jours si besoin)", IsChecked = true, Margin = new Thickness(0, 6, 0, 0) };
        string browsed;

        public ImportConfigDialog(Database target) : base("Importer une configuration depuis une autre base", 680)
        {
            this.target = target;
            bases = BaseManager.Scan().Where(b => !string.Equals(Path.GetFullPath(b.Folder), Path.GetFullPath(target.Folder), StringComparison.OrdinalIgnoreCase)).OrderByDescending(b => b.Year).ToList();
            AddBody(Ui.Lbl("Base source :", true));
            cbSource = Ui.Combo(bases.Select(b => b.Display), 620);
            cbSource.Margin = new Thickness(0, 4, 0, 4);
            AddBody(cbSource);
            var br = Ui.Btn("Parcourir un autre dossier…", Browse);
            br.HorizontalAlignment = HorizontalAlignment.Left;
            AddBody(br);
            var t = Ui.Lbl("Éléments à importer :", true);
            t.Margin = new Thickness(0, 10, 0, 4);
            AddBody(t);
            foreach (ConfigSection sec in ConfigImport.Sections)
            {
                var cb = new CheckBox { Content = sec.Label + "  –  " + sec.Help, IsChecked = true, Margin = new Thickness(0, 2, 0, 0), Tag = sec.Key };
                checks.Add(cb);
                AddBody(cb);
            }
            AddBody(rbMerge);
            AddBody(rbReplace);
            AddBody(chkAlign);
            if (bases.Count > 0) cbSource.SelectedIndex = 0;
            AddButton("Importer", Run, true, false);
            AddButton("Fermer", () => { DialogResult = true; }, false, true);
        }

        void Browse()
        {
            var fb = new System.Windows.Forms.FolderBrowserDialog { Description = "Choisis le dossier de la base source (il doit contenir meta.csv)", SelectedPath = AppPaths.Root };
            if (fb.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            if (!Database.LooksLikeBase(fb.SelectedPath)) { Ui.Error("Ce dossier ne contient pas de base (fichier meta.csv introuvable)."); return; }
            browsed = fb.SelectedPath;
            cbSource.SelectedIndex = -1;
            cbSource.Text = browsed;
            ShowMessage("Source : " + browsed, Sev.Ok);
        }

        void Run()
        {
            if (!Ui.CanEdit(target)) return;
            string folder = null;
            if (cbSource.SelectedIndex >= 0) folder = bases[cbSource.SelectedIndex].Folder;
            else if (browsed != null) folder = browsed;
            if (folder == null) { Ui.Warn("Choisis d'abord la base source."); return; }
            var keys = checks.Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToList();
            if (keys.Count == 0) { Ui.Warn("Coche au moins un élément à importer."); return; }
            bool replace = rbReplace.IsChecked == true;
            if (replace && !Ui.Confirm("Le mode « Remplacer » écrase la configuration de la base ouverte pour les éléments cochés.\nUne sauvegarde automatique est faite à l'enregistrement. Continuer ?")) return;
            var warnings = new List<string>();
            Database src;
            try { src = Database.Open(folder, true, false, warnings); }
            catch (Exception ex) { Ui.Error("Impossible de lire la base source :\n" + ex.Message); return; }
            string report = null;
            if (!Ui.Try(() => { report = ConfigImport.Run(src, target, keys, replace, chkAlign.IsChecked == true); })) return;
            string m = "Importation depuis la base " + src.Year + " terminée :\n\n" + report;
            if (warnings.Count > 0) m += "\n\nAvertissements à la lecture de la source :\n" + string.Join("\n", warnings);
            Ui.Info(m);
            ShowMessage("✔ Importation terminée. N'oublie pas d'enregistrer (Ctrl+S).", Sev.Ok);
        }
    }
}
