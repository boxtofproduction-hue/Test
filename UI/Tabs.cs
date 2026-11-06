using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace GestionGym
{
    public interface ITab
    {
        void Bind(Database db);
        void Refresh();
    }

    // ============================================================ Clients
    public class ClientsTab : Grid, ITab
    {
        Database db;
        readonly DataPanel panel;

        public ClientsTab()
        {
            Margin = new Thickness(10);
            panel = new DataPanel(new[]
            {
                new GridCol("ID", 60), new GridCol("Nom", 140), new GridCol("Prénom", 130), new GridCol("Sexe", 70), new GridCol("Âge", 50, null, true),
                new GridCol("Distance (km)", 90, "N1", true), new GridCol("Pack (actif ou dernier)", 210), new GridCol("Statut", 90), new GridCol("Début du pack", 100, "dd/MM/yyyy"),
                new GridCol("Présences (année)", 110, null, true), new GridCol("Activité préférée", 130), new GridCol("Créneau préféré", 110), new GridCol("Notes", 0)
            });
            panel.AddTool("Nouveau client", () => { if (Ui.CanEdit(db)) { new ClientDialog(db, null).ShowDialog(); } });
            panel.AddTool("Modifier", EditSelected);
            panel.AddTool("Vendre un pack", Sell);
            panel.AddTool("Supprimer", DeleteSelected);
            panel.RowActivated += r => EditSelected();
            Children.Add(panel);
        }

        public void Bind(Database d) { db = d; Refresh(); }

        public void Refresh()
        {
            if (db == null) { panel.SetRows(new List<Row>()); return; }
            Calc calc = db.Calc;
            var rows = new List<Row>();
            foreach (Client c in db.Clients)
            {
                Purchase main;
                string st = calc.ClientStatus(c, out main);
                int pres; calc.PresencesByClient.TryGetValue(c.Id, out pres);
                rows.Add(new Row
                {
                    Tag = c.Id,
                    Level = st == "Inactif" ? 1 : 0,
                    Cells = new object[] { c.Id, c.LastName, c.FirstName, c.Sex, c.Age, c.DistanceKm, main != null ? main.PackName : "", st, main != null ? (object)main.Start : null, pres, calc.FavoriteActivity(c.Id), calc.FavoriteSlot(c.Id), c.Notes }
                });
            }
            panel.SetRows(rows);
        }

        Client Current()
        {
            Row r = panel.Selected;
            if (r == null) { Ui.Warn("Sélectionne d'abord un client dans la liste."); return null; }
            Client c = db.FindClient((string)r.Tag);
            if (c == null) Ui.Warn("Client introuvable.");
            return c;
        }

        void EditSelected()
        {
            if (!Ui.CanEdit(db)) return;
            Client c = Current();
            if (c != null) new ClientDialog(db, c.Clone()).ShowDialog();
        }

        void Sell()
        {
            if (!Ui.CanEdit(db)) return;
            Client c = Current();
            if (c != null) new SaleDialog(db, c.Id).ShowDialog();
        }

        void DeleteSelected()
        {
            if (!Ui.CanEdit(db)) return;
            Client c = Current();
            if (c == null) return;
            if (db.ClientHasData(c.Id)) { Ui.Warn("« " + c.Label + " » a des achats ou des présences : suppression impossible.\nSupprime d'abord ses présences et ses achats si c'est une erreur de saisie."); return; }
            if (Ui.Confirm("Supprimer le client « " + c.Label + " » ?")) Ui.Try(() => db.DeleteClient(c.Id));
        }
    }

    // ============================================================ Packs
    public class PacksTab : Grid, ITab
    {
        Database db;
        readonly DataPanel panel;

        public PacksTab()
        {
            Margin = new Thickness(10);
            panel = new DataPanel(new[]
            {
                new GridCol("Nom du pack", 210), new GridCol("Catégorie", 80), new GridCol("Crédits", 65, null, true), new GridCol("Par", 70), new GridCol("Validité (mois)", 95, null, true),
                new GridCol("Engagement", 90), new GridCol("Durée (mois)", 85, null, true), new GridCol("Paiement", 80), new GridCol("Éligibilité", 140), new GridCol("Reconduction", 95),
                new GridCol("Prix (€)", 80, "N2", true), new GridCol("Actif", 55), new GridCol("Valeur du pack (€)", 120, "N2", true), new GridCol("Séances incluses", 110, null, true),
                new GridCol("Prix / séance (€)", 110, "N2", true), new GridCol("Ventes", 65, null, true), new GridCol("CA année (€)", 100, "N2", true),
                new GridCol("Séances consommées", 130, null, true), new GridCol("Crédits expirés non utilisés", 160, null, true), new GridCol("Clients actifs", 95, null, true)
            });
            panel.AddTool("Nouveau pack", () => { if (Ui.CanEdit(db)) new PackDialog(db, null, false).ShowDialog(); });
            panel.AddTool("Modifier", () => Act(false));
            panel.AddTool("Dupliquer", () => Act(true));
            panel.AddTool("Supprimer", DeleteSelected);
            panel.RowActivated += r => Act(false);
            Children.Add(panel);
        }

        public void Bind(Database d) { db = d; Refresh(); }

        public void Refresh()
        {
            if (db == null) { panel.SetRows(new List<Row>()); return; }
            Calc calc = db.Calc;
            var rows = new List<Row>();
            foreach (Pack k in db.Packs)
            {
                var sold = db.Purchases.Where(p => p.PackName.Equals(k.Name, StringComparison.OrdinalIgnoreCase)).ToList();
                decimal ca = sold.Sum(p => calc.YearRevenue(p));
                int used = sold.Sum(p => calc.UsedByPurchase.ContainsKey(p.Id) ? calc.UsedByPurchase[p.Id] : 0);
                int expired = sold.Where(p => calc.Status(p) == "Expiré").Sum(p => Math.Max(0, calc.Remaining(p) ?? 0));
                int actifs = sold.Where(p => calc.IsActive(p)).Select(p => p.ClientId).Distinct().Count();
                rows.Add(new Row
                {
                    Tag = k.Name,
                    Level = k.Active ? 0 : 1,
                    Cells = new object[] { k.Name, k.Category, k.Credits, k.Per, k.ValidityMonths, k.Engagement, k.EngagementMonths, k.Payment, k.Eligibility, k.Renewal ? "Oui" : "Non", k.Price, k.Active ? "Oui" : "Non",
                        k.TotalValue, k.IncludedSessions.HasValue ? (object)k.IncludedSessions.Value : null, k.PricePerSession.HasValue ? (object)k.PricePerSession.Value : null, sold.Count, ca, used, expired, actifs }
                });
            }
            panel.SetRows(rows);
        }

        Pack Current()
        {
            Row r = panel.Selected;
            if (r == null) { Ui.Warn("Sélectionne d'abord un pack dans la liste."); return null; }
            return db.FindPack((string)r.Tag);
        }

        void Act(bool duplicate)
        {
            if (!Ui.CanEdit(db)) return;
            Pack k = Current();
            if (k != null) new PackDialog(db, k.Clone(), duplicate).ShowDialog();
        }

        void DeleteSelected()
        {
            if (!Ui.CanEdit(db)) return;
            Pack k = Current();
            if (k == null) return;
            int n = db.SalesCount(k.Name);
            if (n > 0) { Ui.Warn("Ce pack a " + n + " vente(s) : suppression impossible.\nModifie-le et passe « Actif » à Non pour ne plus le proposer."); return; }
            if (Ui.Confirm("Supprimer le pack « " + k.Name + " » ?")) Ui.Try(() => db.DeletePack(k.Name));
        }
    }

    // ============================================================ Achats de packs (abonnements)
    public class EditPurchaseDialog : DialogBase
    {
        readonly Database db;
        readonly Purchase original;
        readonly DatePicker dpStart, dpEnd;
        readonly TextBox tbReason = Ui.Text(220), tbCarried = Ui.Text(70), tbNote = Ui.Text(320);

        public EditPurchaseDialog(Database db, Purchase p) : base("Modifier l'achat", 560)
        {
            this.db = db;
            original = p;
            Client c = db.FindClient(p.ClientId);
            var l = Ui.Lbl("Client : " + (c != null ? c.Label : p.ClientId) + "\nPack : " + p.PackName, true);
            l.Margin = new Thickness(0, 0, 0, 8);
            AddBody(l);
            dpStart = Ui.Date(p.Start);
            dpEnd = Ui.Date(p.End);
            tbReason.Text = p.EndReason;
            tbCarried.Text = p.CarriedUsed.ToString();
            tbNote.Text = p.Note;
            var f = new FormPanel(190);
            f.Add("Date d'achat / début", dpStart);
            f.Add("Date de fin (facultative)", dpEnd);
            f.Add("Motif de fin", tbReason);
            f.Add("Crédits déjà utilisés (report)", tbCarried);
            f.Add("Note", tbNote);
            AddBody(f);
            ShowMessage("Pour changer de pack ou arrêter un abonnement, utilise plutôt les boutons « Changer de pack » et « Arrêter ».", Sev.Ok);
            AddButton("Enregistrer", Save, true, false);
            AddButton("Annuler", () => { DialogResult = false; }, false, true);
        }

        void Save()
        {
            DateTime s; string err;
            if (!Ui.ReadDate(dpStart, "Date de début", out s, out err)) { Ui.Error(err); return; }
            DateTime? end = null;
            if (dpEnd.SelectedDate.HasValue) end = dpEnd.SelectedDate.Value.Date;
            else if (!string.IsNullOrWhiteSpace(dpEnd.Text))
            {
                DateTime e;
                if (!Fmt.TryDate(dpEnd.Text, out e)) { Ui.Error("Date de fin invalide (« " + dpEnd.Text + " »). Format : jj/mm/aaaa."); return; }
                end = e;
            }
            int carried;
            if (!Ui.ReadInt(tbCarried, "Crédits déjà utilisés", 0, 100000, out carried, out err)) { Ui.Error(err); return; }
            Purchase p = original.Clone();
            p.Start = s; p.End = end; p.EndReason = end.HasValue ? Fmt.Clean(tbReason.Text) : ""; p.CarriedUsed = carried; p.Note = Fmt.Clean(tbNote.Text);
            string v = db.ValidatePurchase(p, false);
            if (v != null) { Ui.Error(v); return; }
            if (Ui.Try(() => db.UpdatePurchase(p))) DialogResult = true;
        }
    }

    public class PurchasesTab : Grid, ITab
    {
        Database db;
        readonly DataPanel panel;

        public PurchasesTab()
        {
            Margin = new Thickness(10);
            panel = new DataPanel(new[]
            {
                new GridCol("N°", 50, null, true), new GridCol("Client", 190), new GridCol("Pack", 200), new GridCol("Type", 95), new GridCol("Début", 90, "dd/MM/yyyy"), new GridCol("Fin", 90, "dd/MM/yyyy"),
                new GridCol("Motif de fin", 95), new GridCol("Expiration", 95, "dd/MM/yyyy"), new GridCol("Crédits", 65, null, true), new GridCol("Utilisés", 65, null, true), new GridCol("Restants", 70, null, true),
                new GridCol("Statut", 80), new GridCol("Revenu année (€)", 120, "N2", true), new GridCol("Contrôle", 0)
            });
            panel.AddTool("Vendre un pack", () => { if (Ui.CanEdit(db)) new SaleDialog(db, null).ShowDialog(); });
            panel.AddTool("Modifier", Edit);
            panel.AddTool("Changer de pack", Change);
            panel.AddTool("Arrêter", Stop);
            panel.AddTool("Supprimer", Delete);
            panel.RowActivated += r => Edit();
            Children.Add(panel);
        }

        public void Bind(Database d) { db = d; Refresh(); }

        public void Refresh()
        {
            if (db == null) { panel.SetRows(new List<Row>()); return; }
            Calc calc = db.Calc;
            var rows = new List<Row>();
            foreach (Purchase p in db.Purchases.OrderByDescending(x => x.Start).ThenByDescending(x => x.Id))
            {
                Client c = db.FindClient(p.ClientId);
                Pack k = calc.PackOf(p);
                List<Issue> iss = calc.PurchaseIssues(p);
                int? rest = calc.Remaining(p);
                DateTime? ex = calc.Expiry(p);
                rows.Add(new Row
                {
                    Tag = p.Id,
                    Level = (int)Calc.Worst(iss),
                    Cells = new object[] { p.Id, c != null ? c.Label : p.ClientId, p.PackName, k != null ? k.Kind : "?", p.Start, p.End.HasValue ? (object)p.End.Value : null, p.EndReason,
                        ex.HasValue ? (object)ex.Value : null, k != null && k.IsPooled ? (object)k.Credits : null, calc.UsedCredits(p), rest.HasValue ? (object)rest.Value : null, calc.Status(p), calc.YearRevenue(p), Calc.IssueText(iss) }
                });
            }
            panel.SetRows(rows);
        }

        Purchase Current()
        {
            Row r = panel.Selected;
            if (r == null) { Ui.Warn("Sélectionne d'abord une ligne dans la liste."); return null; }
            return db.FindPurchase((int)r.Tag);
        }

        void Edit()
        {
            if (!Ui.CanEdit(db)) return;
            Purchase p = Current();
            if (p != null) new EditPurchaseDialog(db, p).ShowDialog();
        }

        void Change()
        {
            if (!Ui.CanEdit(db)) return;
            Purchase p = Current();
            if (p == null) return;
            if (p.End.HasValue && p.End.Value < db.AsOf) { Ui.Warn("Cet achat est déjà terminé."); return; }
            new ChangePackDialog(db, p).ShowDialog();
        }

        void Stop()
        {
            if (!Ui.CanEdit(db)) return;
            Purchase p = Current();
            if (p == null) return;
            if (p.End.HasValue) { Ui.Warn("Cet achat a déjà une date de fin (" + Fmt.Date(p.End) + "). Modifie-la avec « Modifier »."); return; }
            var dlg = new DateDialog("Arrêter l'abonnement", "Dernier jour d'abonnement (inclus) :", db.AsOf > p.Start ? db.AsOf : p.Start);
            if (dlg.ShowDialog() != true) return;
            DateTime d = dlg.Result;
            if (d < p.Start) { Ui.Error("La date ne peut pas précéder le début (" + Fmt.Date(p.Start) + ")."); return; }
            if (Ui.Confirm("Arrêter « " + p.PackName + " » (dernier jour : " + Fmt.Date(d) + ") ?")) Ui.Try(() => db.StopPurchase(p.Id, d));
        }

        void Delete()
        {
            if (!Ui.CanEdit(db)) return;
            Purchase p = Current();
            if (p == null) return;
            int n = db.AttendanceCountOfPurchase(p.Id);
            string m = "Supprimer cet achat (correction d'une erreur de saisie) ?";
            if (n > 0) m += "\n" + n + " présence(s) rattachée(s) à ce pack perdront leur pack (elles resteront enregistrées « sans pack »).";
            if (Ui.Confirm(m)) Ui.Try(() => db.DeletePurchase(p.Id));
        }
    }

    // ============================================================ Présences
    public class AttendancesTab : Grid, ITab
    {
        Database db;
        readonly DataPanel panel;

        public AttendancesTab()
        {
            Margin = new Thickness(10);
            panel = new DataPanel(new[]
            {
                new GridCol("Date", 95, "dd/MM/yyyy"), new GridCol("Créneau", 110), new GridCol("Activité", 140), new GridCol("Client", 220), new GridCol("Pack débité", 230), new GridCol("Contrôle", 0)
            });
            panel.AddTool("Marquer une présence", () => { if (Ui.CanEdit(db)) new PresenceDialog(db, null).ShowDialog(); });
            panel.AddTool("Supprimer", Delete);
            Children.Add(panel);
        }

        public void Bind(Database d) { db = d; Refresh(); }

        public void Refresh()
        {
            if (db == null) { panel.SetRows(new List<Row>()); return; }
            Calc calc = db.Calc;
            Dictionary<int, List<Issue>> ctl = calc.ControlAll();
            var rows = new List<Row>();
            foreach (Attendance a in db.Attendances.OrderByDescending(x => x.Date).ThenBy(x => db.Slots.IndexOf(x.Slot)).ThenByDescending(x => x.Id))
            {
                Client c; calc.ClientById.TryGetValue(a.ClientId, out c);
                Purchase p; string pk = "";
                if (a.PurchaseId != 0) { pk = calc.PurchaseById.TryGetValue(a.PurchaseId, out p) ? p.PackName : "(pack inconnu)"; }
                List<Issue> iss; ctl.TryGetValue(a.Id, out iss);
                rows.Add(new Row { Tag = a.Id, Level = (int)Calc.Worst(iss), Cells = new object[] { a.Date, a.Slot, a.Activity, c != null ? c.Label : a.ClientId, pk, Calc.IssueText(iss) } });
            }
            panel.SetRows(rows);
        }

        void Delete()
        {
            if (!Ui.CanEdit(db)) return;
            Row r = panel.Selected;
            if (r == null) { Ui.Warn("Sélectionne d'abord une présence."); return; }
            if (Ui.Confirm("Supprimer cette présence ? Le crédit éventuel est rendu au pack.")) { int id = (int)r.Tag; Ui.Try(() => db.DeleteAttendance(id)); }
        }
    }

    // ============================================================ Planning (séances par jour)
    public class PlanningTab : Grid, ITab
    {
        Database db;
        readonly DatePicker dp = new DatePicker { Width = 150, VerticalAlignment = VerticalAlignment.Center };
        readonly ComboBox cbNature = Ui.Combo(Lists.Natures, 120);
        readonly StackPanel list = new StackPanel();
        readonly TextBlock info = new TextBlock { Foreground = Ui.Grey, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
        bool loading;

        public PlanningTab()
        {
            Margin = new Thickness(10);
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            bar.Children.Add(Ui.Btn("◀ Jour précédent", () => Move(-1)));
            bar.Children.Add(dp);
            bar.Children.Add(new TextBlock { Width = 8 });
            bar.Children.Add(Ui.Btn("Jour suivant ▶", () => Move(1)));
            bar.Children.Add(Ui.Btn("Aujourd'hui", () => { if (db != null) dp.SelectedDate = Today(); }));
            bar.Children.Add(new TextBlock { Text = "Nature du jour : ", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 4, 0) });
            bar.Children.Add(cbNature);
            bar.Children.Add(new TextBlock { Width = 12 });
            bar.Children.Add(Ui.Btn("Copier une journée…", () => { if (Ui.CanEdit(db)) new CopyDayDialog(db).ShowDialog(); }));
            Children.Add(bar);
            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var inner = new StackPanel();
            inner.Children.Add(list);
            inner.Children.Add(info);
            sv.Content = inner;
            SetRow(sv, 1);
            Children.Add(sv);
            dp.SelectedDateChanged += (s, e) => { if (!loading) Refresh(); };
            cbNature.SelectionChanged += (s, e) => { if (!loading) ApplyNature(); };
        }

        DateTime Today() { return db.AsOf; }

        public void Bind(Database d)
        {
            db = d;
            loading = true;
            dp.DisplayDateStart = new DateTime(d.Year, 1, 1);
            dp.DisplayDateEnd = new DateTime(d.Year, 12, 31);
            DateTime cur = dp.SelectedDate.HasValue && dp.SelectedDate.Value.Year == d.Year ? dp.SelectedDate.Value : d.AsOf;
            dp.SelectedDate = cur;
            loading = false;
            Refresh();
        }

        void Move(int delta)
        {
            if (db == null || !dp.SelectedDate.HasValue) return;
            DateTime n = dp.SelectedDate.Value.AddDays(delta);
            if (n.Year != db.Year) { Ui.Info("Cette base couvre uniquement l'année " + db.Year + "."); return; }
            dp.SelectedDate = n;
        }

        public void Refresh()
        {
            list.Children.Clear();
            if (db == null) return;
            DateTime d;
            string err;
            if (!Ui.ReadDate(dp, "Date", out d, out err)) { info.Text = err; return; }
            if (d.Year != db.Year) { info.Text = "Date hors de l'année " + db.Year + "."; return; }
            loading = true;
            cbNature.SelectedItem = db.DayNature(d);
            Calc calc = db.Calc;
            var head = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            string[] hs = { "Créneau", "Activité programmée", "Présents / places" };
            for (int i = 0; i < 3; i++)
            {
                var t = new TextBlock { Text = hs[i], FontWeight = FontWeights.Bold, Foreground = Ui.Blue };
                SetColumn(t, i);
                head.Children.Add(t);
            }
            list.Children.Add(head);
            int sessions = 0, present = 0;
            foreach (string slot in db.Slots)
            {
                string act = db.SessionActivity(d, slot);
                int n = calc.Checker.SlotCount(d, slot);
                var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
                var l = new TextBlock { Text = slot, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
                var cb = new ComboBox { Width = 240, Padding = new Thickness(3), HorizontalAlignment = HorizontalAlignment.Left };
                cb.Items.Add("(aucune séance)");
                foreach (string a in db.Activities) cb.Items.Add(a);
                cb.SelectedItem = act.Length > 0 ? act : "(aucune séance)";
                string slotCopy = slot;
                string actCopy = act;
                cb.SelectionChanged += (s, e) => { if (!loading) ApplySession(d, slotCopy, actCopy, cb.SelectedItem as string); };
                var cnt = new TextBlock { Text = act.Length > 0 || n > 0 ? n + " / " + db.Capacity : "", VerticalAlignment = VerticalAlignment.Center };
                if (n >= db.Capacity) { cnt.Foreground = Ui.Red; cnt.FontWeight = FontWeights.Bold; }
                SetColumn(l, 0); SetColumn(cb, 1); SetColumn(cnt, 2);
                g.Children.Add(l); g.Children.Add(cb); g.Children.Add(cnt);
                list.Children.Add(g);
                if (act.Length > 0) sessions++;
                present += n;
            }
            info.Text = Fmt.Plural(sessions, "séance programmée", "séances programmées") + " – " + Fmt.Plural(present, "présence", "présences") + " ce jour-là" + (db.ReadOnly ? "   (base en lecture seule)" : "");
            loading = false;
        }

        void ApplySession(DateTime d, string slot, string oldAct, string newItem)
        {
            string newAct = (newItem == null || newItem == "(aucune séance)") ? "" : newItem;
            if (newAct == oldAct) return;
            if (!Ui.CanEdit(db)) { Refresh(); return; }
            int n = db.Attendances.Count(a => a.Date == d.Date && a.Slot == slot);
            if (n > 0 && !Ui.Confirm(n + " présence(s) sont déjà enregistrées sur ce créneau (activité « " + (oldAct.Length > 0 ? oldAct : "non programmée") + " »).\nModifier quand même la séance ? Les présences resteront enregistrées avec leur activité actuelle."))
            { Refresh(); return; }
            if (!Ui.Try(() => db.SetSession(d, slot, newAct))) Refresh();
        }

        void ApplyNature()
        {
            DateTime d; string err;
            if (!Ui.ReadDate(dp, "Date", out d, out err)) return;
            string nat = cbNature.SelectedItem as string;
            if (nat == null || nat == db.DayNature(d)) return;
            if (!Ui.CanEdit(db)) { Refresh(); return; }
            if (!Ui.Try(() => db.SetDayNature(d, nat))) Refresh();
        }
    }

    // ============================================================ Configuration
    public class ConfigTab : Grid, ITab
    {
        Database db;
        readonly TextBlock baseInfo = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        readonly TextBox tbCap = Ui.Text(70), tbAlert = Ui.Text(70);
        readonly ComboBox cbEvent = Ui.Combo(new string[0], 200);
        readonly CheckBox chkNormal = new CheckBox { Content = "Oui" };
        readonly TextBlock msg = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        readonly ListBox lbAct = new ListBox { Height = 230 };
        readonly ListBox lbSlot = new ListBox { Height = 230 };
        bool loading;

        public ConfigTab()
        {
            Margin = new Thickness(10);
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
            left.Children.Add(Ui.Lbl("Base ouverte", true));
            left.Children.Add(baseInfo);
            left.Children.Add(Ui.Lbl("Paramètres généraux", true));
            var f = new FormPanel(200);
            f.Add("Capacité max par séance (places)", tbCap);
            f.Add("Activité « Event » (ateliers)", cbEvent);
            f.Add("Packs Normal autorisés aux ateliers", chkNormal);
            f.Add("Alerte avant expiration d'un pack (jours)", tbAlert);
            left.Children.Add(f);
            var apply = Ui.Btn("Appliquer les paramètres", Apply);
            apply.HorizontalAlignment = HorizontalAlignment.Left;
            apply.Margin = new Thickness(0, 8, 0, 0);
            left.Children.Add(apply);
            left.Children.Add(msg);
            SetColumn(left, 0);
            Children.Add(left);

            var mid = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
            mid.Children.Add(Ui.Lbl("Activités (12 maximum)", true));
            mid.Children.Add(lbAct);
            var b1 = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            b1.Children.Add(Ui.Btn("Ajouter", AddAct));
            b1.Children.Add(Ui.Btn("Renommer", RenameAct));
            b1.Children.Add(Ui.Btn("Supprimer", DeleteAct));
            mid.Children.Add(b1);
            SetColumn(mid, 1);
            Children.Add(mid);

            var right = new StackPanel();
            right.Children.Add(Ui.Lbl("Créneaux horaires (20 maximum)", true));
            right.Children.Add(lbSlot);
            var b2 = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            b2.Children.Add(Ui.Btn("Ajouter", AddSlot));
            b2.Children.Add(Ui.Btn("Renommer", RenameSlot));
            b2.Children.Add(Ui.Btn("Supprimer", DeleteSlot));
            b2.Children.Add(Ui.Btn("▲", () => MoveSlot(-1)));
            b2.Children.Add(Ui.Btn("▼", () => MoveSlot(1)));
            right.Children.Add(b2);
            SetColumn(right, 2);
            Children.Add(right);
        }

        public void Bind(Database d) { db = d; Refresh(); }

        public void Refresh()
        {
            if (db == null) return;
            loading = true;
            baseInfo.Text = "Année : " + db.Year + (db.Archived ? "   [ARCHIVE – lecture seule]" : (db.ReadOnly ? "   [lecture seule]" : "")) + "\nDossier : " + db.Folder + "\n"
                + Fmt.Plural(db.Clients.Count, "client", "clients") + ", " + Fmt.Plural(db.Packs.Count, "pack", "packs") + ", " + Fmt.Plural(db.Purchases.Count, "achat", "achats") + ", " + Fmt.Plural(db.Attendances.Count, "présence", "présences") + ", " + Fmt.Plural(db.Sessions.Count, "séance programmée", "séances programmées") + ".";
            tbCap.Text = db.Capacity.ToString();
            tbAlert.Text = db.AlertDays.ToString();
            cbEvent.Items.Clear();
            foreach (string a in db.Activities) cbEvent.Items.Add(a);
            cbEvent.SelectedItem = db.EventActivity;
            chkNormal.IsChecked = db.NormalInEvent;
            lbAct.Items.Clear();
            foreach (string a in db.Activities) lbAct.Items.Add(a);
            lbSlot.Items.Clear();
            foreach (string s in db.Slots) lbSlot.Items.Add(s);
            loading = false;
        }

        void Apply()
        {
            if (!Ui.CanEdit(db)) return;
            int cap, al; string err;
            if (!Ui.ReadInt(tbCap, "Capacité", 1, 500, out cap, out err)) { Fail(err); return; }
            if (!Ui.ReadInt(tbAlert, "Alerte d'expiration", 0, 365, out al, out err)) { Fail(err); return; }
            string ev = cbEvent.SelectedItem as string;
            if (ev == null) { Fail("Choisis l'activité « Event »."); return; }
            db.Capacity = cap; db.AlertDays = al; db.EventActivity = ev; db.NormalInEvent = chkNormal.IsChecked == true;
            db.Touch();
            msg.Foreground = Ui.Green;
            msg.Text = "✔ Paramètres enregistrés.";
        }

        void Fail(string e) { msg.Foreground = Ui.Red; msg.Text = e; }

        string SelAct() { string s = lbAct.SelectedItem as string; if (s == null) Ui.Warn("Sélectionne d'abord une activité."); return s; }
        string SelSlot() { string s = lbSlot.SelectedItem as string; if (s == null) Ui.Warn("Sélectionne d'abord un créneau."); return s; }

        void AddAct() { string v; if (Ui.CanEdit(db) && Ui.Input("Nouvelle activité", "Nom de l'activité :", "", out v)) Ui.Try(() => db.AddActivity(v)); }
        void RenameAct() { if (!Ui.CanEdit(db)) return; string o = SelAct(); string v; if (o != null && Ui.Input("Renommer l'activité", "Nouveau nom :", o, out v)) Ui.Try(() => db.RenameActivity(o, v)); }
        void DeleteAct() { if (!Ui.CanEdit(db)) return; string o = SelAct(); if (o != null && Ui.Confirm("Supprimer l'activité « " + o + " » ?")) Ui.Try(() => db.DeleteActivity(o)); }
        void AddSlot() { string v; if (Ui.CanEdit(db) && Ui.Input("Nouveau créneau", "Nom du créneau (ex. 18h-19h) :", "", out v)) Ui.Try(() => db.AddSlot(v)); }
        void RenameSlot() { if (!Ui.CanEdit(db)) return; string o = SelSlot(); string v; if (o != null && Ui.Input("Renommer le créneau", "Nouveau nom :", o, out v)) Ui.Try(() => db.RenameSlot(o, v)); }
        void DeleteSlot() { if (!Ui.CanEdit(db)) return; string o = SelSlot(); if (o != null && Ui.Confirm("Supprimer le créneau « " + o + " » ?")) Ui.Try(() => db.DeleteSlot(o)); }
        void MoveSlot(int d) { if (!Ui.CanEdit(db)) return; string o = SelSlot(); if (o == null) return; Ui.Try(() => db.MoveSlot(o, d)); int i = db.Slots.IndexOf(o); if (i >= 0) lbSlot.SelectedIndex = i; }
    }

    // ============================================================ Tableau de bord
    public class DashboardTab : ScrollViewer, ITab
    {
        Database db;
        readonly WrapPanel cards = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        readonly ListBox alerts = new ListBox { Height = 150, Margin = new Thickness(0, 0, 10, 10) };
        readonly BarChart chRev = new BarChart("Chiffre d'affaires encaissé par mois (€)", Ui.Blue);
        readonly BarChart chPres = new BarChart("Présences par mois", Ui.Orange);
        readonly BarChart chAct = new BarChart("Présences par activité", Ui.Green);
        readonly BarChart chPack = new BarChart("Ventes de packs par pack (année)", Ui.Blue);
        readonly BarChart chSlot = new BarChart("Présences par créneau", Ui.Orange);

        public DashboardTab()
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            var root = new StackPanel { Margin = new Thickness(10) };
            root.Children.Add(cards);
            root.Children.Add(Ui.Lbl("Alertes", true));
            root.Children.Add(alerts);
            var g = new UniformGrid { Columns = 2 };
            g.Children.Add(chRev); g.Children.Add(chPres); g.Children.Add(chAct); g.Children.Add(chPack); g.Children.Add(chSlot);
            root.Children.Add(g);
            Content = root;
        }

        public void Bind(Database d) { db = d; Refresh(); }

        public void Refresh()
        {
            cards.Children.Clear();
            alerts.Items.Clear();
            if (db == null) return;
            Calc calc = db.Calc;
            int abonnes = 0, packActif = 0, inactifs = 0, sans = 0;
            foreach (Client c in db.Clients)
            {
                Purchase m;
                string st = calc.ClientStatus(c, out m);
                if (st == "Abonné") abonnes++; else if (st == "Pack actif") packActif++; else if (st == "Inactif") inactifs++; else sans++;
            }
            decimal[] rev = calc.RevenueByMonth();
            int[] pres = calc.PresencesByMonth();
            int credits = db.Purchases.Where(p => calc.IsActive(p)).Sum(p => Math.Max(0, calc.Remaining(p) ?? 0));
            int anomalies = calc.ControlAll().Values.Count(l => Calc.Worst(l) == Sev.Error);
            int purchaseIssues = db.Purchases.Count(p => Calc.Worst(calc.PurchaseIssues(p)) == Sev.Error);

            cards.Children.Add(Ui.Card("Clients", db.Clients.Count.ToString(), Ui.Blue));
            cards.Children.Add(Ui.Card("Abonnés actifs", abonnes.ToString(), Ui.Green));
            cards.Children.Add(Ui.Card("Clients avec un pack actif", packActif.ToString(), Ui.Green));
            cards.Children.Add(Ui.Card("Inactifs / sans pack", (inactifs + sans).ToString(), Ui.Orange));
            cards.Children.Add(Ui.Card("CA encaissé (année)", Fmt.Money0(rev.Sum()), Ui.Blue));
            cards.Children.Add(Ui.Card("Présences (année)", pres.Sum().ToString(), Ui.Blue));
            cards.Children.Add(Ui.Card("Taux de remplissage", Fmt.Pct(calc.FillRate()), Ui.Blue));
            cards.Children.Add(Ui.Card("Crédits restants (packs actifs)", credits.ToString(), Ui.Orange));
            cards.Children.Add(Ui.Card("Anomalies à corriger", (anomalies + purchaseIssues).ToString(), (anomalies + purchaseIssues) > 0 ? Ui.Red : Ui.Green));

            if (db.Archived) alerts.Items.Add("ℹ Base d'archive ouverte en lecture seule (année " + db.Year + ").");
            if (db.ReadOnly && !db.Archived) alerts.Items.Add("ℹ Base en lecture seule.");
            if (anomalies > 0) alerts.Items.Add("✖ " + anomalies + " présence(s) en erreur (onglet Présences, colonne Contrôle).");
            if (purchaseIssues > 0) alerts.Items.Add("✖ " + purchaseIssues + " achat(s) en erreur (onglet Achats de packs, colonne Contrôle).");
            foreach (Purchase p in db.Purchases)
            {
                foreach (Issue i in calc.PurchaseIssues(p).Where(x => x.Severity == Sev.Warning))
                {
                    Client c = db.FindClient(p.ClientId);
                    alerts.Items.Add("⚠ " + (c != null ? c.Label : p.ClientId) + " – " + p.PackName + " : " + i.Text);
                }
            }
            if (db.Sessions.Count == 0) alerts.Items.Add("ℹ Aucune séance programmée : utilise l'onglet Planning (ou importe une configuration).");
            if (alerts.Items.Count == 0) alerts.Items.Add("✔ Aucune alerte.");

            string[] mois = Enumerable.Range(1, 12).Select(m => Fmt.MonthName(m)).ToArray();
            chRev.SetData(mois, rev.Select(x => (double)x).ToArray(), "N0");
            chPres.SetData(mois, pres.Select(x => (double)x).ToArray(), "N0");
            var pa = calc.PresencesByActivity();
            chAct.SetData(pa.Keys.ToArray(), pa.Values.Select(x => (double)x).ToArray(), "N0");
            var sp = calc.SalesByPack();
            chPack.SetData(sp.Keys.ToArray(), sp.Values.Select(x => (double)x).ToArray(), "N0");
            var ps = calc.PresencesBySlot();
            chSlot.SetData(ps.Keys.ToArray(), ps.Values.Select(x => (double)x).ToArray(), "N0");
        }
    }
}
