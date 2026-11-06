using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace GestionGym
{
    public class MainWindow : Window
    {
        public static MainWindow Current;
        public const string AppVersion = "3.0";

        Database db;
        readonly TabControl tabs = new TabControl();
        readonly DashboardTab dashboard = new DashboardTab();
        readonly ClientsTab clients = new ClientsTab();
        readonly PacksTab packs = new PacksTab();
        readonly PurchasesTab purchases = new PurchasesTab();
        readonly AttendancesTab attendances = new AttendancesTab();
        readonly PlanningTab planning = new PlanningTab();
        readonly TrackingTab tracking = new TrackingTab();
        readonly ChargesTab charges = new ChargesTab();
        readonly UrssafTab urssaf = new UrssafTab();
        readonly CfeTab cfe = new CfeTab();
        readonly RentaTab renta = new RentaTab();
        readonly ReportingTab reporting = new ReportingTab();
        readonly ConfigTab config = new ConfigTab();
        readonly TextBlock sbBase = new TextBlock();
        readonly TextBlock sbState = new TextBlock();
        readonly TextBlock sbInfo = new TextBlock();
        readonly DispatcherTimer recoveryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(90) };
        bool closing;

        public MainWindow()
        {
            Current = this;
            Ui.MainWin = this;
            Title = "Gestion GYM";
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = WindowState.Maximized;
            MinWidth = 960;
            MinHeight = 640;
            FontSize = 13;
            BuildLayout();
            Loaded += (s, e) => Startup();
            Closing += OnClosing;
            PreviewKeyDown += OnKey;
            recoveryTimer.Tick += (s, e) => EmergencySave();
            recoveryTimer.Start();
        }

        // ------------------------------------------------------------ mise en page
        void BuildLayout()
        {
            var dock = new DockPanel();

            var menu = new Menu();
            var file = new MenuItem { Header = "_Fichier" };
            file.Items.Add(Item("_Nouvelle base…", NewBase, "Ctrl+N"));
            file.Items.Add(Item("_Ouvrir une base (actuelle ou archive)…", OpenBase, "Ctrl+O"));
            file.Items.Add(new Separator());
            file.Items.Add(Item("_Enregistrer", () => SaveNow(), "Ctrl+S"));
            file.Items.Add(Item("Enregistrer _sous (copie dans Actuel)…", SaveAs, null));
            file.Items.Add(Item("_Archiver une copie de la base ouverte", ArchiveNow, null));
            file.Items.Add(new Separator());
            file.Items.Add(Item("_Planifier une base pour l'année suivante…", PlanNext, null));
            file.Items.Add(Item("Importer une _configuration depuis une autre base…", ImportConfig, null));
            file.Items.Add(new Separator());
            file.Items.Add(Item("_Importer un fichier Excel / CSV / XLSM / ODS…", ImportFile, null));
            var exp = new MenuItem { Header = "E_xporter" };
            exp.Items.Add(Item("Excel (.xlsx)…", () => ExportData("xlsx"), null));
            exp.Items.Add(Item("OpenOffice / LibreOffice (.ods)…", () => ExportData("ods"), null));
            exp.Items.Add(Item("PDF…", () => ExportData("pdf"), null));
            exp.Items.Add(Item("Plusieurs formats à la fois…", () => ExportData(null), null));
            file.Items.Add(exp);
            file.Items.Add(new Separator());
            file.Items.Add(Item("Ouvrir le dossier de l'application", OpenAppFolder, null));
            file.Items.Add(Item("_Quitter", () => Close(), "Alt+F4"));
            menu.Items.Add(file);

            var data = new MenuItem { Header = "_Données" };
            data.Items.Add(Item("Nouveau _client…", () => { if (Ui.CanEdit(db)) new ClientDialog(db, null).ShowDialog(); }, null));
            data.Items.Add(Item("_Vendre un pack…", () => { if (Ui.CanEdit(db)) new SaleDialog(db, null).ShowDialog(); }, null));
            data.Items.Add(Item("Marquer une _présence…", () => { if (Ui.CanEdit(db)) new PresenceDialog(db, null).ShowDialog(); }, null));
            data.Items.Add(Item("Nouvelle _charge fixe…", () => { if (Ui.CanEdit(db)) new ChargeDialog(db, -1, null).ShowDialog(); }, null));
            data.Items.Add(new Separator());
            data.Items.Add(Item("Nouveau _pack…", () => { if (Ui.CanEdit(db)) new PackDialog(db, null, false).ShowDialog(); }, null));
            data.Items.Add(Item("_Copier une journée…", () => { if (Ui.CanEdit(db)) new CopyDayDialog(db).ShowDialog(); }, null));
            menu.Items.Add(data);

            var view = new MenuItem { Header = "_Affichage" };
            view.Items.Add(Item("_Actualiser", RefreshAll, "F5"));
            menu.Items.Add(view);

            var help = new MenuItem { Header = "A_ide" };
            help.Items.Add(Item("_Aide d'utilisation", () => HelpWindow.ShowTopic(CurrentTabTitle()), "F1"));
            help.Items.Add(Item("À _propos", About, null));
            menu.Items.Add(help);

            DockPanel.SetDock(menu, Dock.Top);
            dock.Children.Add(menu);

            var status = new StatusBar();
            status.Items.Add(new StatusBarItem { Content = sbBase });
            status.Items.Add(new Separator());
            status.Items.Add(new StatusBarItem { Content = sbState });
            status.Items.Add(new Separator());
            status.Items.Add(new StatusBarItem { Content = sbInfo });
            DockPanel.SetDock(status, Dock.Bottom);
            dock.Children.Add(status);

            AddTab("Tableau de bord", dashboard);
            AddTab("Clients", clients);
            AddTab("Packs (catalogue)", packs);
            AddTab("Achats de packs", purchases);
            AddTab("Tracking abonnements", tracking);
            AddTab("Présences", attendances);
            AddTab("Planning", planning);
            AddTab("Charges fixes", charges);
            AddTab("URSSAF", urssaf);
            AddTab("CFE", cfe);
            AddTab("Rentabilité clients", renta);
            AddTab("Reporting", reporting);
            AddTab("Configuration", config);
            tabs.SelectionChanged += (s, e) =>
            {
                if (!ReferenceEquals(e.OriginalSource, tabs)) return;
                RefreshCurrent();
            };
            dock.Children.Add(tabs);
            Content = dock;
        }

        void AddTab(string header, UIElement content)
        {
            tabs.Items.Add(new TabItem { Header = header, Content = content, Padding = new Thickness(10, 4, 10, 4) });
        }

        static MenuItem Item(string header, Action a, string gesture)
        {
            var mi = new MenuItem { Header = header, InputGestureText = gesture };
            if (a != null) mi.Click += (s, e) => a();
            return mi;
        }

        // ------------------------------------------------------------ démarrage
        void Startup()
        {
            if (AppPaths.Notice != null) Ui.Info(AppPaths.Notice);
            var warnings = new List<string>();
            Database d = null;
            try
            {
                var active = BaseManager.Scan().Where(b => !b.Archived).ToList();
                BaseInfo pick = active.Where(b => b.Year == DateTime.Now.Year).OrderByDescending(b => b.SavedOn).FirstOrDefault()
                    ?? active.Where(b => b.Year <= DateTime.Now.Year).OrderByDescending(b => b.Year).FirstOrDefault()
                    ?? active.OrderByDescending(b => b.Year).FirstOrDefault();
                if (pick != null) d = Database.Open(pick.Folder, false, false, warnings);
            }
            catch (Exception ex)
            {
                Ui.Error("Impossible d'ouvrir la dernière base :\n" + ex.Message + "\n\nUne nouvelle base va être proposée ; tes fichiers existants ne sont pas modifiés.");
                d = null;
            }
            if (d == null)
            {
                var dlg = new NewBaseDialog(DateTime.Now.Year, "Bienvenue – créer la base de départ");
                if (dlg.ShowDialog() != true) { Application.Current.Shutdown(); return; }
                d = CreateBase(dlg.Year);
                if (d == null) { Application.Current.Shutdown(); return; }
            }
            SetDatabase(d, warnings);
        }

        Database CreateBase(int year)
        {
            try
            {
                Database nd = Database.CreateNew(year, BaseManager.NewActiveFolder(year));
                if (!nd.AcquireLock()) { Ui.Error("Impossible de verrouiller le dossier de la nouvelle base."); return null; }
                nd.Save();
                return nd;
            }
            catch (Exception ex)
            {
                Ui.Error("Impossible de créer la base :\n" + ex.Message);
                return null;
            }
        }

        void SetDatabase(Database nd, List<string> warnings)
        {
            if (db != null) { db.Changed -= OnDbChanged; db.ReleaseLock(); }
            db = nd;
            db.Changed += OnDbChanged;
            dashboard.Bind(db); clients.Bind(db); packs.Bind(db); purchases.Bind(db); attendances.Bind(db); planning.Bind(db); tracking.Bind(db); charges.Bind(db); urssaf.Bind(db); cfe.Bind(db); renta.Bind(db); reporting.Bind(db); config.Bind(db);
            UpdateChrome();
            if (warnings != null && warnings.Count > 0)
            {
                if (!db.ReadOnly) db.Snapshot("a_l_ouverture");
                Ui.ShowText("Avertissements à l'ouverture de la base", "Certaines lignes ou valeurs ont dû être ignorées ou corrigées à la lecture :\n\n• " + string.Join("\n• ", warnings) + "\n\nUne copie de sauvegarde des fichiers d'origine a été faite dans le sous-dossier _sauvegardes de la base.");
            }
            CheckRecovery();
        }

        void OnDbChanged()
        {
            RefreshCurrent();
            UpdateChrome();
        }

        void RefreshCurrent()
        {
            var ti = tabs.SelectedItem as TabItem;
            if (ti == null) return;
            ITab t = ti.Content as ITab;
            if (t != null) t.Refresh();
        }

        void RefreshAll()
        {
            if (db == null) return;
            db.Refresh();
        }

        void UpdateChrome()
        {
            if (db == null) return;
            string tag = db.Archived ? "  [ARCHIVE – lecture seule]" : (db.ReadOnly ? "  [lecture seule]" : "");
            Title = "Gestion GYM – Base " + db.Year + tag + (db.Dirty ? "  *" : "");
            sbBase.Text = "Base " + db.Year + "  –  " + db.Folder;
            sbState.Text = db.ReadOnly ? "Lecture seule" : (db.Dirty ? "● Modifications non enregistrées (Ctrl+S)" : "✔ Enregistré");
            sbInfo.Text = db.Clients.Count + " clients  •  " + db.Purchases.Count + " achats  •  " + db.Attendances.Count + " présences";
        }

        // ------------------------------------------------------------ enregistrement
        public bool SaveNow()
        {
            if (db == null) return true;
            if (db.ReadOnly) { Ui.CanEdit(db); return false; }
            try
            {
                db.Save();
                ClearRecovery();
                UpdateChrome();
                return true;
            }
            catch (Exception ex)
            {
                Ui.Error("Échec de l'enregistrement :\n" + ex.Message + "\n\nVérifie l'espace disque et les droits d'écriture du dossier. Tes données sont toujours en mémoire : corrige le problème puis réessaie (Ctrl+S), ou utilise « Enregistrer sous ».");
                return false;
            }
        }

        /// <summary>Propose d'enregistrer les modifications avant de quitter / changer de base. Retourne false si l'utilisateur annule.</summary>
        bool ConfirmLeave()
        {
            if (db == null || !db.Dirty || db.ReadOnly) return true;
            MessageBoxResult r = Ui.YesNoCancel("Enregistrer les modifications de la base " + db.Year + " ?");
            if (r == MessageBoxResult.Cancel) return false;
            if (r == MessageBoxResult.Yes) return SaveNow();
            return true;
        }

        // ------------------------------------------------------------ actions du menu Fichier
        void NewBase()
        {
            if (!ConfirmLeave()) return;
            var dlg = new NewBaseDialog(DateTime.Now.Year, "Nouvelle base");
            if (dlg.ShowDialog() != true) return;
            BaseInfo ex = BaseManager.ActiveBaseFor(dlg.Year);
            if (ex != null && Ui.Confirm("Une base " + dlg.Year + " existe déjà dans « Actuel » :\n" + ex.Folder + "\n\nL'ouvrir plutôt que d'en créer une nouvelle ?"))
            {
                OpenFolder(ex.Folder, false, false);
                return;
            }
            if (db != null) db.ReleaseLock();
            Database nd = CreateBase(dlg.Year);
            if (nd == null) { if (db != null && !db.ReadOnly) db.AcquireLock(); return; }
            SetDatabase(nd, null);
        }

        void OpenBase()
        {
            if (!ConfirmLeave()) return;
            var dlg = new OpenBaseDialog(db);
            if (dlg.ShowDialog() != true) return;
            OpenFolder(dlg.ResultFolder, dlg.ResultArchived, dlg.ResultReadOnly);
        }

        void OpenFolder(string folder, bool archived, bool readOnly)
        {
            bool same = db != null && string.Equals(Path.GetFullPath(db.Folder), Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase);
            if (same) db.ReleaseLock();
            var warnings = new List<string>();
            Database nd;
            try { nd = Database.Open(folder, readOnly, archived, warnings); }
            catch (Exception ex)
            {
                Ui.Error("Impossible d'ouvrir cette base :\n" + ex.Message);
                if (same && !db.ReadOnly) db.AcquireLock();
                return;
            }
            SetDatabase(nd, warnings);
        }

        void SaveAs()
        {
            if (db == null) return;
            if (!db.ReadOnly && db.Dirty && !SaveNow()) return;
            try
            {
                string f = BaseManager.NewActiveFolder(db.Year);
                db.SaveCopy(f);
                if (Ui.Confirm("Copie enregistrée dans :\n" + f + "\n\nOuvrir cette copie (modifiable) maintenant ?")) OpenFolder(f, false, false);
            }
            catch (Exception ex) { Ui.Error("Impossible d'enregistrer la copie :\n" + ex.Message); }
        }

        void ArchiveNow()
        {
            if (db == null) return;
            if (!db.ReadOnly && db.Dirty && Ui.Confirm("Enregistrer d'abord les modifications en cours ?")) { if (!SaveNow()) return; }
            try
            {
                string f = BaseManager.ArchiveCopy(db);
                Ui.Info("Copie de la base " + db.Year + " archivée dans :\n" + f);
            }
            catch (Exception ex) { Ui.Error("Impossible d'archiver :\n" + ex.Message); }
        }

        void PlanNext()
        {
            if (db == null) return;
            var dlg = new PlanNextDialog(db);
            if (dlg.ShowDialog() != true) return;
            PlanOptions o = dlg.Options;
            try
            {
                string report;
                Database nd = BaseManager.Plan(db, o, out report);
                string arch = "";
                if (o.ArchiveCurrent) arch = "\nCopie de la base " + db.Year + " archivée : " + BaseManager.ArchiveCopy(db);
                if (!nd.AcquireLock()) { Ui.Error("Impossible de verrouiller le dossier de la nouvelle base."); return; }
                nd.Save();
                Ui.Info("Base " + o.NewYear + " créée dans :\n" + nd.Folder + "\n\n" + report + arch);
                if (dlg.OpenAfter)
                {
                    if (!ConfirmLeave()) { nd.ReleaseLock(); return; }
                    SetDatabase(nd, null);
                }
                else nd.ReleaseLock();
            }
            catch (Exception ex) { Ui.Error("Impossible de planifier la base :\n" + ex.Message); }
        }

        void ImportConfig()
        {
            if (!Ui.CanEdit(db)) return;
            new ImportConfigDialog(db).ShowDialog();
        }

        void OpenAppFolder()
        {
            try { System.Diagnostics.Process.Start("explorer.exe", "\"" + AppPaths.Root + "\""); }
            catch (Exception ex) { Ui.Error("Impossible d'ouvrir le dossier :\n" + ex.Message); }
        }

        string CurrentTabTitle()
        {
            var ti = tabs.SelectedItem as TabItem;
            string h = ti == null ? null : ti.Header as string;
            if (h == "Charges fixes" || h == "URSSAF" || h == "CFE" || h == "Reporting" || h == "Configuration" || h == "Planning" || h == "Clients" || h == "Présences" || h == "Tableau de bord" || h == "Packs (catalogue)" || h == "Achats de packs" || h == "Tracking abonnements" || h == "Rentabilité clients") return h;
            return "Démarrage rapide";
        }

        void ExportData(string preselect)
        {
            if (db == null) return;
            new ExportDialog(db, preselect).ShowDialog();
        }

        void ImportFile()
        {
            if (db == null || !Ui.CanEdit(db)) return;
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Choisir le fichier à importer",
                Filter = "Classeurs et CSV|*.xlsx;*.xlsm;*.xltx;*.ods;*.csv;*.txt|Tous les fichiers|*.*",
                CheckFileExists = true
            };
            if (dlg.ShowDialog(this) != true) return;
            List<SheetData> sheets;
            Cursor old = Mouse.OverrideCursor;
            Mouse.OverrideCursor = Cursors.Wait;
            try { sheets = FileReaders.Read(dlg.FileName); }
            catch (Exception ex) { Mouse.OverrideCursor = old; Ui.Error("Lecture impossible :\n" + ex.Message); return; }
            Mouse.OverrideCursor = old;
            if (sheets == null || sheets.Count == 0) { Ui.Info("Le fichier ne contient aucune feuille de données."); return; }
            if (!db.ReadOnly) db.Snapshot("avant_import");
            new ImportDialog(db, dlg.FileName, sheets).ShowDialog();
        }

        void About()
        {
            Ui.Info("Gestion GYM – version " + AppVersion + "\n\nDossier de travail :\n" + AppPaths.Root + "\n\n• Actuel : bases en cours (un dossier de fichiers CSV par année)\n• Archive : bases des années précédentes\n• Export Excel / Export OpenOffice / Export PDF : exports\n\nRaccourcis : Ctrl+S enregistrer, Ctrl+N nouvelle base, Ctrl+O ouvrir, F5 actualiser, F1 aide.\n\nChaque enregistrement conserve automatiquement les 10 dernières sauvegardes dans le sous-dossier _sauvegardes de la base.");
        }

        // ------------------------------------------------------------ récupération après incident
        string RecoveryPath(Database d)
        {
            return Path.Combine(AppPaths.Actuel, "_recuperation", Path.GetFileName(d.Folder.TrimEnd('\\', '/')));
        }

        /// <summary>Copie de secours des modifications non enregistrées (toutes les 90 s et en cas d'erreur).</summary>
        public void EmergencySave()
        {
            try
            {
                if (db == null || !db.Dirty || db.ReadOnly || closing) return;
                db.SaveCopy(RecoveryPath(db));
            }
            catch { }
        }

        void ClearRecovery()
        {
            try
            {
                if (db == null) return;
                string rp = RecoveryPath(db);
                if (Directory.Exists(rp)) Directory.Delete(rp, true);
            }
            catch { }
        }

        void CheckRecovery()
        {
            if (db == null || db.ReadOnly || db.Archived) return;
            string rp = RecoveryPath(db);
            if (!Directory.Exists(rp) || !Database.LooksLikeBase(rp)) return;
            if (Ui.Confirm("Une session précédente ne s'est pas fermée correctement : des modifications non enregistrées ont été retrouvées (copie de secours).\n\nLes restaurer maintenant ?\n(Oui = reprendre les modifications retrouvées ; Non = les abandonner)"))
            {
                try
                {
                    db.Snapshot("avant_restauration");
                    foreach (string f in Directory.GetFiles(rp, "*.csv")) File.Copy(f, Path.Combine(db.Folder, Path.GetFileName(f)), true);
                    Directory.Delete(rp, true);
                    OpenFolder(db.Folder, false, false);
                    Ui.Info("Les modifications retrouvées ont été restaurées.");
                }
                catch (Exception ex) { Ui.Error("Restauration impossible :\n" + ex.Message); }
            }
            else
            {
                try { Directory.Delete(rp, true); } catch { }
            }
        }

        // ------------------------------------------------------------ clavier et fermeture
        void OnKey(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            if (ctrl && e.Key == Key.S) { SaveNow(); e.Handled = true; }
            else if (ctrl && e.Key == Key.N) { NewBase(); e.Handled = true; }
            else if (ctrl && e.Key == Key.O) { OpenBase(); e.Handled = true; }
            else if (e.Key == Key.F5) { RefreshAll(); e.Handled = true; }
            else if (e.Key == Key.F1) { HelpWindow.ShowTopic(CurrentTabTitle()); e.Handled = true; }
        }

        void OnClosing(object sender, CancelEventArgs e)
        {
            if (db != null && db.Dirty && !db.ReadOnly)
            {
                MessageBoxResult r = Ui.YesNoCancel("Enregistrer les modifications de la base " + db.Year + " avant de quitter ?");
                if (r == MessageBoxResult.Cancel) { e.Cancel = true; return; }
                if (r == MessageBoxResult.Yes && !SaveNow()) { e.Cancel = true; return; }
            }
            closing = true;
            recoveryTimer.Stop();
            if (db != null)
            {
                if (!db.Dirty) ClearRecovery();
                db.ReleaseLock();
            }
        }
    }
}
