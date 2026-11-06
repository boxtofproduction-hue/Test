using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GestionGym
{
    // ============================================================ nouveau client / modification
    public class ClientDialog : DialogBase
    {
        const string NoPack = "(aucun pack pour le moment)";
        readonly Database db;
        readonly Client editing;
        readonly TextBox tbLast = Ui.Text(260), tbFirst = Ui.Text(260), tbAge = Ui.Text(70), tbDist = Ui.Text(90), tbNotes = Ui.Text(300);
        readonly ComboBox cbSex = Ui.Combo(Lists.Sexes, 140);
        ComboBox cbPack;
        DatePicker dpDate;
        TextBlock packInfo;

        public ClientDialog(Database db, Client editing) : base(editing == null ? "Nouveau client" : "Modifier le client", 560)
        {
            this.db = db;
            this.editing = editing;
            var f = new FormPanel(160);
            f.Add("Nom", tbLast);
            f.Add("Prénom", tbFirst);
            f.Add("Sexe", cbSex);
            f.Add("Âge", tbAge);
            f.Add("Distance du GYM (km)", tbDist);
            f.Add("Notes (facultatif)", tbNotes);
            cbSex.SelectedIndex = 0;
            if (editing != null)
            {
                tbLast.Text = editing.LastName;
                tbFirst.Text = editing.FirstName;
                cbSex.SelectedItem = editing.Sex;
                tbAge.Text = editing.Age.ToString();
                tbDist.Text = Fmt.Dec(editing.DistanceKm);
                tbNotes.Text = editing.Notes;
            }
            else
            {
                var names = new List<string> { NoPack };
                names.AddRange(db.Packs.Where(p => p.Active).Select(p => p.Name));
                cbPack = Ui.Combo(names, 320);
                cbPack.SelectedIndex = 0;
                dpDate = Ui.Date(db.AsOf);
                packInfo = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Ui.Grey };
                f.Add("Pack acheté", cbPack);
                f.Add("Date d'achat", dpDate);
                f.AddFull(packInfo);
                cbPack.SelectionChanged += (s, e) => { UpdatePackInfo(); Live(); };
                dpDate.SelectedDateChanged += (s, e) => Live();
            }
            AddBody(f);
            tbLast.TextChanged += (s, e) => Live();
            tbFirst.TextChanged += (s, e) => Live();
            tbAge.TextChanged += (s, e) => Live();
            tbDist.TextChanged += (s, e) => Live();
            cbSex.SelectionChanged += (s, e) => Live();
            AddButton(editing == null ? "Enregistrer le client" : "Enregistrer", Save, true, false);
            AddButton(editing == null ? "Fermer" : "Annuler", () => { DialogResult = editing != null ? (bool?)false : true; }, false, true);
            Loaded += (s, e) => tbLast.Focus();
        }

        void UpdatePackInfo()
        {
            if (cbPack == null) return;
            Pack k = cbPack.SelectedIndex > 0 ? db.FindPack((string)cbPack.SelectedItem) : null;
            packInfo.Text = k == null ? "" : k.Summary;
        }

        bool Build(out Client c, out string err)
        {
            c = new Client();
            err = null;
            c.LastName = Fmt.Clean(tbLast.Text);
            c.FirstName = Fmt.Clean(tbFirst.Text);
            c.Notes = Fmt.Clean(tbNotes.Text);
            if (c.LastName.Length == 0 || c.FirstName.Length == 0) { err = "Le nom et le prénom sont obligatoires."; return false; }
            if (cbSex.SelectedItem == null) { err = "Choisis le sexe."; return false; }
            c.Sex = (string)cbSex.SelectedItem;
            int age; decimal dist;
            if (!Ui.ReadInt(tbAge, "Âge", 3, 110, out age, out err)) return false;
            if (!Ui.ReadDec(tbDist, "Distance", 0, 1000, out dist, out err)) return false;
            c.Age = age;
            c.DistanceKm = dist;
            return true;
        }

        void Live()
        {
            Client c; string err;
            if (!Build(out c, out err)) { ShowMessage(err, Sev.Error); return; }
            Client dup = db.FindDuplicate(c.LastName, c.FirstName, editing != null ? editing.Id : null);
            if (dup != null) { ShowMessage("Un client « " + dup.Label + " » existe déjà avec ce nom et ce prénom.", Sev.Warning); return; }
            ShowMessage("✔ Prêt à enregistrer.", Sev.Ok);
        }

        void Save()
        {
            Client c; string err;
            if (!Build(out c, out err)) { Ui.Error(err); return; }
            Client dup = db.FindDuplicate(c.LastName, c.FirstName, editing != null ? editing.Id : null);
            if (dup != null && !Ui.Confirm("Un client « " + dup.Label + " » existe déjà avec ce nom et ce prénom.\nL'enregistrer quand même ?")) return;

            if (editing != null)
            {
                c.Id = editing.Id;
                if (Ui.Try(() => db.UpdateClient(c))) DialogResult = true;
                return;
            }

            bool sale = cbPack != null && cbPack.SelectedIndex > 0;
            DateTime d = DateTime.Today;
            if (sale)
            {
                string de;
                if (!Ui.ReadDate(dpDate, "Date d'achat", out d, out de)) { Ui.Error(de); return; }
            }
            c.Id = db.NextClientId();
            if (!Ui.Try(() => db.AddClient(c))) return;
            string msg = "Client enregistré : " + c.Label + ".";
            if (sale)
            {
                var p = new Purchase { ClientId = c.Id, PackName = (string)cbPack.SelectedItem, Start = d };
                List<string> w = db.PurchaseWarnings(p);
                bool go = w.Count == 0 || Ui.Confirm(string.Join("\n", w) + "\n\nEnregistrer quand même l'achat du pack ?");
                if (go && Ui.Try(() => db.AddPurchase(p))) msg += " Pack « " + p.PackName + " » enregistré.";
                else msg += " ATTENTION : le pack n'a pas été enregistré (ajoute-le avec « Vendre un pack »).";
            }
            ShowMessage(msg, Sev.Ok);
            tbLast.Text = ""; tbFirst.Text = ""; tbAge.Text = ""; tbDist.Text = ""; tbNotes.Text = "";
            cbSex.SelectedIndex = 0;
            if (cbPack != null) cbPack.SelectedIndex = 0;
            tbLast.Focus();
            ShowMessage(msg, Sev.Ok);
        }
    }

    // ============================================================ pack (catalogue)
    public class PackDialog : DialogBase
    {
        readonly Database db;
        readonly string oldName;
        readonly Pack original;
        readonly TextBox tbName = Ui.Text(320), tbCredits = Ui.Text(70), tbValid = Ui.Text(70), tbEngM = Ui.Text(70), tbPrice = Ui.Text(90);
        readonly ComboBox cbCat = Ui.Combo(Lists.Categories, 150), cbPer = Ui.Combo(Lists.Pers, 150), cbEng = Ui.Combo(Lists.Engagements, 150), cbPay = Ui.Combo(Lists.Payments, 150), cbElig = Ui.Combo(Lists.Eligibilities, 220);
        readonly CheckBox chkRenew = new CheckBox { Content = "Oui : les paiements continuent après l'engagement" };
        readonly CheckBox chkActive = new CheckBox { Content = "Oui : proposé à la vente", IsChecked = true };
        readonly TextBlock summary = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Ui.Grey, Margin = new Thickness(0, 8, 0, 0) };
        bool loading;

        public PackDialog(Database db, Pack existing, bool duplicate) : base(existing == null || duplicate ? "Nouveau pack" : "Modifier le pack", 600)
        {
            this.db = db;
            oldName = (existing != null && !duplicate) ? existing.Name : null;
            original = existing != null && !duplicate ? existing.Clone() : null;
            var f = new FormPanel(200);
            f.Add("Nom du pack", tbName);
            f.Add("Catégorie (Normal / Event)", cbCat);
            f.Add("Crédits (nombre de séances)", tbCredits);
            f.Add("Crédits par", cbPer);
            f.Add("Validité (mois, de 1 à 12)", tbValid);
            f.Add("Type d'engagement", cbEng);
            f.Add("Durée de l'engagement (mois)", tbEngM);
            f.Add("Échéance de paiement", cbPay);
            f.Add("Éligibilité du client", cbElig);
            f.Add("Reconduction tacite", chkRenew);
            f.Add("Prix (€) par échéance", tbPrice);
            f.Add("Actif", chkActive);
            f.AddFull(summary);
            AddBody(f);

            loading = true;
            Pack p = existing != null ? existing : new Pack();
            tbName.Text = duplicate ? p.Name + " (copie)" : p.Name;
            cbCat.SelectedItem = p.Category;
            tbCredits.Text = p.Credits.ToString();
            cbPer.SelectedItem = p.Per;
            tbValid.Text = p.ValidityMonths.ToString();
            cbEng.SelectedItem = p.Engagement;
            tbEngM.Text = p.EngagementMonths.ToString();
            cbPay.SelectedItem = p.Payment;
            cbElig.SelectedItem = p.Eligibility;
            chkRenew.IsChecked = p.Renewal;
            tbPrice.Text = existing != null ? Fmt.Dec(p.Price) : "";
            chkActive.IsChecked = p.Active;
            loading = false;

            tbName.TextChanged += (s, e) => Live();
            tbCredits.TextChanged += (s, e) => Live();
            tbValid.TextChanged += (s, e) => Live();
            tbEngM.TextChanged += (s, e) => Live();
            tbPrice.TextChanged += (s, e) => Live();
            cbCat.SelectionChanged += (s, e) => Live();
            cbPer.SelectionChanged += (s, e) => Live();
            cbPay.SelectionChanged += (s, e) => Live();
            cbElig.SelectionChanged += (s, e) => Live();
            chkRenew.Click += (s, e) => Live();
            chkActive.Click += (s, e) => Live();
            cbEng.SelectionChanged += (s, e) =>
            {
                if (!loading)
                {
                    string v = cbEng.SelectedItem as string;
                    tbEngM.Text = v == "Annuel" ? "12" : (v == "Mensuel" ? "1" : "0");
                }
                Live();
            };
            AddButton("Enregistrer le pack", Save, true, false);
            AddButton("Annuler", () => { DialogResult = false; }, false, true);
            Loaded += (s, e) => { tbName.Focus(); Live(); };
        }

        bool Build(out Pack p, out string err)
        {
            p = new Pack();
            err = null;
            p.Name = Fmt.Clean(tbName.Text);
            if (p.Name.Length == 0) { err = "Saisis le nom du pack."; return false; }
            if (cbCat.SelectedItem == null || cbPer.SelectedItem == null || cbEng.SelectedItem == null || cbPay.SelectedItem == null || cbElig.SelectedItem == null)
            { err = "Renseigne toutes les listes déroulantes."; return false; }
            p.Category = (string)cbCat.SelectedItem;
            p.Per = (string)cbPer.SelectedItem;
            p.Engagement = (string)cbEng.SelectedItem;
            p.Payment = (string)cbPay.SelectedItem;
            p.Eligibility = (string)cbElig.SelectedItem;
            p.Renewal = chkRenew.IsChecked == true;
            p.Active = chkActive.IsChecked == true;
            int cr, va, em; decimal px;
            if (!Ui.ReadInt(tbCredits, "Crédits", 1, 1000, out cr, out err)) return false;
            if (!Ui.ReadInt(tbValid, "Validité", 1, 12, out va, out err)) return false;
            if (!Ui.ReadInt(tbEngM, "Durée d'engagement", 0, 60, out em, out err)) return false;
            if (!Ui.ReadDec(tbPrice, "Prix", 0, 100000, out px, out err)) return false;
            p.Credits = cr; p.ValidityMonths = va; p.EngagementMonths = em; p.Price = px;
            return true;
        }

        void Live()
        {
            if (loading) return;
            Pack p; string err;
            if (!Build(out p, out err)) { ShowMessage(err, Sev.Error); summary.Text = ""; return; }
            string e = db.ValidatePack(p, oldName);
            string cap = p.PaymentCap >= 9999 ? "paiements récurrents sans limite (reconduction)" : Fmt.Plural(p.PaymentCap, "échéance", "échéances");
            string per = p.PricePerSession.HasValue ? Fmt.Money(p.PricePerSession.Value) : "n/a";
            summary.Text = "Résumé : " + p.Summary + "\nValeur totale : " + Fmt.Money(p.TotalValue) + " (" + cap + ") – séances incluses : " + (p.IncludedSessions.HasValue ? p.IncludedSessions.Value.ToString() : "n/a") + " – prix par séance : " + per + " – type : " + p.Kind;
            if (e != null) { ShowMessage(e, Sev.Error); return; }
            List<string> w = db.PackWarnings(p);
            if (w.Count > 0) ShowMessage(string.Join("\n", w), Sev.Warning);
            else ShowMessage("✔ Prêt à enregistrer.", Sev.Ok);
        }

        void Save()
        {
            Pack p; string err;
            if (!Build(out p, out err)) { Ui.Error(err); return; }
            string e = db.ValidatePack(p, oldName);
            if (e != null) { Ui.Error(e); return; }
            List<string> w = db.PackWarnings(p);
            if (w.Count > 0 && !Ui.Confirm(string.Join("\n", w) + "\n\nEnregistrer quand même ce pack ?")) return;
            if (oldName != null)
            {
                int sales = db.SalesCount(oldName);
                bool renamed = !oldName.Equals(p.Name, StringComparison.Ordinal);
                bool core = original != null && (original.Price != p.Price || original.Credits != p.Credits || original.Per != p.Per || original.ValidityMonths != p.ValidityMonths
                    || original.Payment != p.Payment || original.Engagement != p.Engagement || original.EngagementMonths != p.EngagementMonths || original.Renewal != p.Renewal || original.Category != p.Category);
                if (sales > 0 && (renamed || core))
                {
                    string m = sales + " achat(s) existant(s) utilise(nt) ce pack.";
                    if (renamed) m += "\n• Le nouveau nom sera appliqué à ces achats.";
                    if (core) m += "\n• Les crédits, validités, prix et revenus de ces achats seront recalculés avec les nouvelles valeurs.";
                    if (!Ui.Confirm(m + "\n\nContinuer ?")) return;
                }
                if (Ui.Try(() => db.UpdatePack(oldName, p))) DialogResult = true;
            }
            else
            {
                if (Ui.Try(() => db.AddPack(p))) DialogResult = true;
            }
        }
    }

    // ============================================================ vente d'un pack
    public class SaleDialog : DialogBase
    {
        readonly Database db;
        readonly string fixedClient;
        ClientPicker picker;
        readonly ComboBox cbPack;
        readonly DatePicker dp;
        readonly TextBox tbNote = Ui.Text(320);
        readonly TextBlock packInfo = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Ui.Grey };
        string clientId;

        public SaleDialog(Database db, string clientId) : base("Vendre un pack", 600)
        {
            this.db = db;
            fixedClient = clientId;
            this.clientId = clientId;
            if (clientId == null)
            {
                picker = new ClientPicker(db, 140);
                AddBody(picker);
                picker.SelectedChanged += () => { Client c = picker.Selected; this.clientId = c != null ? c.Id : null; Live(); };
            }
            else
            {
                Client c = db.FindClient(clientId);
                var l = Ui.Lbl("Client : " + (c != null ? c.Label : clientId), true);
                l.Margin = new Thickness(0, 0, 0, 8);
                AddBody(l);
            }
            cbPack = Ui.Combo(db.Packs.Where(p => p.Active).Select(p => p.Name), 360);
            dp = Ui.Date(db.AsOf);
            var f = new FormPanel(140);
            f.Add("Pack vendu", cbPack);
            f.AddFull(packInfo);
            f.Add("Date d'achat", dp);
            f.Add("Note (facultatif)", tbNote);
            AddBody(f);
            cbPack.SelectionChanged += (s, e) => { Pack k = db.FindPack(cbPack.SelectedItem as string); packInfo.Text = k == null ? "" : k.Summary; Live(); };
            dp.SelectedDateChanged += (s, e) => Live();
            AddButton("Enregistrer la vente", Sell, true, false);
            AddButton("Fermer", () => { DialogResult = true; }, false, true);
        }

        bool Build(out Purchase p, out string err)
        {
            p = null; err = null;
            if (clientId == null) { err = "Sélectionne d'abord un client."; return false; }
            if (cbPack.SelectedItem == null) { err = "Choisis le pack."; return false; }
            DateTime d;
            if (!Ui.ReadDate(dp, "Date d'achat", out d, out err)) return false;
            p = new Purchase { ClientId = clientId, PackName = (string)cbPack.SelectedItem, Start = d, Note = Fmt.Clean(tbNote.Text) };
            return true;
        }

        void Live()
        {
            Purchase p; string err;
            if (!Build(out p, out err)) { ShowMessage(err, Sev.Warning); return; }
            string e = db.ValidatePurchase(p, true);
            if (e != null) { ShowMessage(e, Sev.Error); return; }
            List<string> w = db.PurchaseWarnings(p);
            var info = new List<string>();
            Pack k = db.FindPack(p.PackName);
            if (k != null && k.IsPooled) info.Add("Utilisable jusqu'au " + Fmt.Date(p.Start.AddMonths(k.ValidityMonths)) + " (" + k.Credits + " crédit(s)).");
            if (k != null) info.Add("Premier encaissement : " + Fmt.Money(k.Price) + (k.StepMonths > 0 ? " puis " + Fmt.Money(k.Price) + (k.StepMonths == 1 ? " chaque mois." : " chaque année.") : "."));
            if (w.Count > 0) ShowMessage(string.Join("\n", w) + (info.Count > 0 ? "\n" + string.Join(" ", info) : ""), Sev.Warning);
            else ShowMessage("✔ " + string.Join(" ", info), Sev.Ok);
        }

        void Sell()
        {
            Purchase p; string err;
            if (!Build(out p, out err)) { Ui.Error(err); return; }
            string e = db.ValidatePurchase(p, true);
            if (e != null) { Ui.Error(e); return; }
            List<string> w = db.PurchaseWarnings(p);
            if (w.Count > 0 && !Ui.Confirm(string.Join("\n", w) + "\n\nVendre quand même ce pack ?")) return;
            if (!Ui.Try(() => db.AddPurchase(p))) return;
            Client c = db.FindClient(p.ClientId);
            ShowMessage("✔ Pack « " + p.PackName + " » vendu à " + (c != null ? c.Label : p.ClientId) + " le " + Fmt.Date(p.Start) + ".", Sev.Ok);
        }
    }

    // ============================================================ changer de pack
    public class ChangePackDialog : DialogBase
    {
        readonly Database db;
        readonly Purchase old;
        readonly ComboBox cbPack;
        readonly DatePicker dp;

        public ChangePackDialog(Database db, Purchase old) : base("Changer de pack", 520)
        {
            this.db = db;
            this.old = old;
            Client c = db.FindClient(old.ClientId);
            var l = Ui.Lbl("Client : " + (c != null ? c.Label : old.ClientId) + "\nPack actuel : " + old.PackName + " (depuis le " + Fmt.Date(old.Start) + ")", true);
            l.Margin = new Thickness(0, 0, 0, 8);
            AddBody(l);
            cbPack = Ui.Combo(db.Packs.Where(p => p.Active && !p.Name.Equals(old.PackName, StringComparison.OrdinalIgnoreCase)).Select(p => p.Name), 320);
            DateTime init = db.AsOf > old.Start ? db.AsOf : old.Start.AddDays(1);
            dp = Ui.Date(init);
            var f = new FormPanel(170);
            f.Add("Nouveau pack", cbPack);
            f.Add("À partir du", dp);
            AddBody(f);
            ShowMessage("L'ancien pack est clôturé la veille (motif « Changement »), le nouveau démarre à la date choisie.", Sev.Ok);
            AddButton("Valider le changement", Apply, true, false);
            AddButton("Annuler", () => { DialogResult = false; }, false, true);
        }

        void Apply()
        {
            if (cbPack.SelectedItem == null) { Ui.Error("Choisis le nouveau pack."); return; }
            DateTime d; string err;
            if (!Ui.ReadDate(dp, "Date", out d, out err)) { Ui.Error(err); return; }
            string pack = (string)cbPack.SelectedItem;
            var np = new Purchase { ClientId = old.ClientId, PackName = pack, Start = d };
            string e = db.ValidatePurchase(np, true);
            if (e != null) { Ui.Error(e); return; }
            if (!Ui.Confirm("Passer ce client de « " + old.PackName + " » à « " + pack + " » le " + Fmt.Date(d) + " ?")) return;
            if (Ui.Try(() => db.ChangePack(old.Id, pack, d))) DialogResult = true;
        }
    }

    // ============================================================ choix d'une date
    public class DateDialog : DialogBase
    {
        readonly DatePicker dp;
        public DateTime Result;

        public DateDialog(string title, string prompt, DateTime init) : base(title, 460)
        {
            var l = Ui.Lbl(prompt);
            l.Margin = new Thickness(0, 0, 0, 8);
            AddBody(l);
            dp = Ui.Date(init);
            AddBody(dp);
            AddButton("OK", () =>
            {
                DateTime d; string err;
                if (!Ui.ReadDate(dp, "Date", out d, out err)) { Ui.Error(err); return; }
                Result = d; DialogResult = true;
            }, true, false);
            AddButton("Annuler", () => { DialogResult = false; }, false, true);
        }
    }

    // ============================================================ marquer une présence
    public class PresenceDialog : DialogBase
    {
        const string NoPackText = "Sans pack (aucun crédit débité)";
        readonly Database db;
        readonly ClientPicker picker;
        readonly DatePicker dp;
        readonly ComboBox cbSlot = new ComboBox { Width = 260, Padding = new Thickness(3), HorizontalAlignment = HorizontalAlignment.Left };
        readonly ComboBox cbAct;
        readonly ComboBox cbPack = new ComboBox { Width = 380, Padding = new Thickness(3), HorizontalAlignment = HorizontalAlignment.Left };
        readonly TextBlock packNote = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Ui.Grey, Margin = new Thickness(0, 2, 0, 0) };
        readonly ListBox hist = new ListBox { Height = 140 };
        readonly List<string> slotNames = new List<string>();
        readonly List<int> packIds = new List<int>();
        readonly List<int> histIds = new List<int>();
        bool loading;
        Button btnAdd;

        public PresenceDialog(Database db, string clientId) : base("Marquer une présence", 900)
        {
            this.db = db;
            picker = new ClientPicker(db, 230);
            picker.Width = 300;
            cbAct = Ui.Combo(db.Activities, 260);
            dp = Ui.Date(db.AsOf);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(picker, 0);
            grid.Children.Add(picker);

            var right = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
            var f = new FormPanel(120);
            f.Add("Date", dp);
            f.Add("Créneau", cbSlot);
            f.Add("Activité", cbAct);
            f.Add("Pack débité", cbPack);
            f.AddFull(packNote);
            right.Children.Add(f);
            right.Children.Add(new TextBlock { Text = "Présences de ce client (sélectionne une ligne puis « Supprimer » pour corriger) :", Margin = new Thickness(0, 10, 0, 2), TextWrapping = TextWrapping.Wrap });
            right.Children.Add(hist);
            var del = Ui.Btn("Supprimer la présence sélectionnée", DeleteSelected);
            del.Margin = new Thickness(0, 6, 0, 0);
            del.HorizontalAlignment = HorizontalAlignment.Left;
            right.Children.Add(del);
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);
            AddBody(grid);

            picker.SelectedChanged += () => { RefillPacks(); RefillHistory(); Live(); };
            dp.SelectedDateChanged += (s, e) => { RefillSlots(); RefillPacks(); Live(); };
            cbSlot.SelectionChanged += (s, e) => { if (loading) return; ApplyActivityFromSlot(); RefillPacks(); Live(); };
            cbAct.SelectionChanged += (s, e) => { if (loading) return; RefillPacks(); Live(); };
            cbPack.SelectionChanged += (s, e) => { if (!loading) Live(); };

            btnAdd = AddButton("Marquer présent", Add, true, false);
            AddButton("Fermer", () => { DialogResult = true; }, false, true);

            RefillSlots();
            if (clientId != null) picker.Select(clientId);
            else { RefillPacks(); Live(); }
        }

        bool TryDate(out DateTime d)
        {
            string err;
            return Ui.ReadDate(dp, "Date", out d, out err);
        }

        void RefillSlots()
        {
            loading = true;
            string keep = cbSlot.SelectedIndex >= 0 && cbSlot.SelectedIndex < slotNames.Count ? slotNames[cbSlot.SelectedIndex] : null;
            cbSlot.Items.Clear();
            slotNames.Clear();
            DateTime d;
            bool ok = TryDate(out d);
            foreach (string s in db.Slots)
            {
                string cfg = ok ? db.SessionActivity(d, s) : "";
                slotNames.Add(s);
                cbSlot.Items.Add(s + (cfg.Length > 0 ? "   –   " + cfg : "   (non programmé)"));
            }
            if (keep != null && slotNames.Contains(keep)) cbSlot.SelectedIndex = slotNames.IndexOf(keep);
            else
            {
                int first = -1;
                if (ok) for (int i = 0; i < slotNames.Count; i++) if (db.SessionActivity(d, slotNames[i]).Length > 0) { first = i; break; }
                cbSlot.SelectedIndex = first;
            }
            loading = false;
            ApplyActivityFromSlot();
        }

        void ApplyActivityFromSlot()
        {
            DateTime d;
            if (cbSlot.SelectedIndex < 0 || !TryDate(out d)) return;
            string cfg = db.SessionActivity(d, slotNames[cbSlot.SelectedIndex]);
            bool was = loading;
            loading = true;
            if (cfg.Length > 0) cbAct.SelectedItem = cfg;
            loading = was;
        }

        Attendance Candidate(int purchaseId, out string err)
        {
            err = null;
            Client c = picker.Selected;
            if (c == null) { err = "Sélectionne d'abord un client."; return null; }
            DateTime d;
            if (!Ui.ReadDate(dp, "Date", out d, out err)) return null;
            if (cbSlot.SelectedIndex < 0) { err = "Choisis un créneau."; return null; }
            if (cbAct.SelectedItem == null) { err = "Choisis une activité."; return null; }
            return new Attendance { ClientId = c.Id, Date = d, Slot = slotNames[cbSlot.SelectedIndex], Activity = (string)cbAct.SelectedItem, PurchaseId = purchaseId };
        }

        void RefillPacks()
        {
            loading = true;
            cbPack.Items.Clear();
            packIds.Clear();
            packNote.Text = "";
            Client c = picker.Selected;
            string err;
            Attendance probe = c != null ? Candidate(0, out err) : null;
            var refused = new List<string>();
            int firstOk = -1;
            if (c != null && probe != null)
            {
                foreach (Purchase p in db.Purchases.Where(x => x.ClientId == c.Id).OrderByDescending(x => x.Start))
                {
                    probe.PurchaseId = p.Id;
                    List<Issue> pi = db.Calc.Checker.PackCheck(probe);
                    Issue bad = pi.FirstOrDefault(i => i.Severity == Sev.Error);
                    if (bad != null) { refused.Add("« " + p.PackName + " » (" + Fmt.Date(p.Start) + ") : " + bad.Text); continue; }
                    int? rest = db.Calc.Remaining(p);
                    string txt = p.PackName + "   (depuis le " + Fmt.Date(p.Start) + ")";
                    if (rest.HasValue) txt += "   –   " + rest.Value + " crédit(s) restant(s)";
                    Issue warn = pi.FirstOrDefault(i => i.Severity == Sev.Warning);
                    if (warn != null) txt += "   ⚠ " + warn.Text;
                    cbPack.Items.Add(txt);
                    packIds.Add(p.Id);
                    if (firstOk < 0) firstOk = cbPack.Items.Count - 1;
                }
            }
            cbPack.Items.Add(NoPackText);
            packIds.Add(0);
            cbPack.SelectedIndex = firstOk >= 0 ? firstOk : cbPack.Items.Count - 1;
            if (refused.Count > 0) packNote.Text = "Packs non utilisables pour cette séance :\n" + string.Join("\n", refused);
            loading = false;
        }

        void RefillHistory()
        {
            hist.Items.Clear();
            histIds.Clear();
            Client c = picker.Selected;
            if (c == null) return;
            foreach (Attendance a in db.Attendances.Where(x => x.ClientId == c.Id).OrderByDescending(x => x.Date).ThenByDescending(x => x.Id).Take(200))
            {
                Purchase p = a.PurchaseId != 0 ? db.FindPurchase(a.PurchaseId) : null;
                hist.Items.Add(Fmt.Date(a.Date) + "   " + a.Slot + "   " + a.Activity + (p != null ? "   [" + p.PackName + "]" : "   [sans pack]"));
                histIds.Add(a.Id);
            }
        }

        void Live()
        {
            string err;
            int pid = cbPack.SelectedIndex >= 0 && cbPack.SelectedIndex < packIds.Count ? packIds[cbPack.SelectedIndex] : 0;
            Attendance a = Candidate(pid, out err);
            if (a == null) { ShowMessage(err, Sev.Warning); btnAdd.IsEnabled = true; return; }
            List<Issue> issues = db.Calc.Checker.Check(a);
            ShowIssues(issues);
            btnAdd.IsEnabled = Calc.Worst(issues) != Sev.Error;
        }

        void Add()
        {
            string err;
            int pid = cbPack.SelectedIndex >= 0 && cbPack.SelectedIndex < packIds.Count ? packIds[cbPack.SelectedIndex] : 0;
            Attendance a = Candidate(pid, out err);
            if (a == null) { Ui.Error(err); return; }
            List<Issue> issues = db.Calc.Checker.Check(a);
            if (issues.Any(i => i.Severity == Sev.Error)) { Ui.Error(string.Join("\n", issues.Where(i => i.Severity == Sev.Error).Select(i => i.Text))); return; }
            var warns = issues.Where(i => i.Severity == Sev.Warning).Select(i => i.Text).ToList();
            if (warns.Count > 0 && !Ui.Confirm(string.Join("\n", warns) + "\n\nEnregistrer quand même la présence ?")) return;
            if (!Ui.Try(() => db.AddAttendance(a))) return;
            Client c = picker.Selected;
            RefillSlots();
            RefillPacks();
            RefillHistory();
            ShowMessage("✔ Présence ajoutée : " + (c != null ? c.Label : "") + " – " + Fmt.Date(a.Date) + " " + a.Slot + ".", Sev.Ok);
        }

        void DeleteSelected()
        {
            int i = hist.SelectedIndex;
            if (i < 0 || i >= histIds.Count) { Ui.Warn("Sélectionne dans la liste la présence à supprimer."); return; }
            if (!Ui.Confirm("Supprimer cette présence ? Le crédit éventuel est rendu au pack.")) return;
            int id = histIds[i];
            if (!Ui.Try(() => db.DeleteAttendance(id))) return;
            RefillPacks();
            RefillHistory();
            Live();
        }
    }

    // ============================================================ copier une journée
    public class CopyDayDialog : DialogBase
    {
        readonly Database db;
        readonly DatePicker dpSrc, dpOne, dpFrom, dpTo;
        readonly RadioButton rbOne = new RadioButton { Content = "Copier vers une seule date", IsChecked = true, Margin = new Thickness(0, 6, 0, 2) };
        readonly RadioButton rbWeek = new RadioButton { Content = "Répéter chaque semaine (même jour de la semaine) entre deux dates", Margin = new Thickness(0, 6, 0, 2) };
        readonly CheckBox chkNature = new CheckBox { Content = "Copier aussi la nature du jour (Normal / Vacances)", IsChecked = true, Margin = new Thickness(0, 6, 0, 0) };
        readonly CheckBox chkOver = new CheckBox { Content = "Remplacer les créneaux déjà configurés à destination", Margin = new Thickness(0, 4, 0, 0) };
        readonly TextBlock srcInfo = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Ui.Grey, Margin = new Thickness(0, 4, 0, 6) };

        public CopyDayDialog(Database db) : base("Copier une journée", 620)
        {
            this.db = db;
            dpSrc = Ui.Date(db.AsOf);
            dpOne = Ui.Date(null);
            dpFrom = Ui.Date(null);
            dpTo = Ui.Date(null);
            var f = new FormPanel(130);
            f.Add("Jour à copier", dpSrc);
            f.AddFull(srcInfo);
            AddBody(f);
            AddBody(rbOne);
            var f1 = new FormPanel(130);
            f1.Add("Date cible", dpOne);
            AddBody(f1);
            AddBody(rbWeek);
            var f2 = new FormPanel(130);
            f2.Add("Du", dpFrom);
            f2.Add("Au", dpTo);
            AddBody(f2);
            AddBody(chkNature);
            AddBody(chkOver);
            dpSrc.SelectedDateChanged += (s, e) => Live();
            dpOne.SelectedDateChanged += (s, e) => Live();
            dpFrom.SelectedDateChanged += (s, e) => Live();
            dpTo.SelectedDateChanged += (s, e) => Live();
            rbOne.Click += (s, e) => Live();
            rbWeek.Click += (s, e) => Live();
            chkNature.Click += (s, e) => Live();
            chkOver.Click += (s, e) => Live();
            AddButton("Copier", Apply, true, false);
            AddButton("Fermer", () => { DialogResult = true; }, false, true);
            Loaded += (s, e) => Live();
        }

        bool Targets(out DateTime src, out List<DateTime> targets, out string err)
        {
            targets = new List<DateTime>();
            err = null;
            if (!Ui.ReadDate(dpSrc, "Jour à copier", out src, out err)) return false;
            if (rbOne.IsChecked == true)
            {
                DateTime t;
                if (!Ui.ReadDate(dpOne, "Date cible", out t, out err)) return false;
                if (t == src) { err = "La date cible est identique au jour copié."; return false; }
                targets.Add(t);
                return true;
            }
            DateTime a, b;
            if (!Ui.ReadDate(dpFrom, "Début de la période", out a, out err)) return false;
            if (!Ui.ReadDate(dpTo, "Fin de la période", out b, out err)) return false;
            if (b < a) { err = "La fin de la période précède son début."; return false; }
            if ((b - a).TotalDays > 400) { err = "La période est trop longue (400 jours maximum)."; return false; }
            for (DateTime d = a; d <= b; d = d.AddDays(1))
                if (d.DayOfWeek == src.DayOfWeek && d != src) targets.Add(d);
            if (targets.Count == 0) { err = "Aucune date de cette période ne tombe le même jour de la semaine que le jour copié."; return false; }
            return true;
        }

        void Live()
        {
            DateTime src; List<DateTime> t; string err;
            DateTime sd;
            string e0;
            if (Ui.ReadDate(dpSrc, "Jour à copier", out sd, out e0))
            {
                int n = db.Sessions.Count(s => s.Date == sd);
                srcInfo.Text = (n == 0 ? "Aucune séance configurée ce jour-là." : Fmt.Plural(n, "séance configurée", "séances configurées")) + " – nature : " + db.DayNature(sd);
            }
            if (!Targets(out src, out t, out err)) { ShowMessage(err, Sev.Warning); return; }
            int inYear = t.Count(x => x.Year == db.Year);
            string m = "Les séances seront copiées vers " + Fmt.Plural(t.Count, "date", "dates") + ".";
            if (inYear < t.Count) m += "\n" + (t.Count - inYear) + " date(s) hors de l'année " + db.Year + " seront ignorées.";
            ShowMessage(m, inYear < t.Count ? Sev.Warning : Sev.Ok);
        }

        void Apply()
        {
            DateTime src; List<DateTime> t; string err;
            if (!Targets(out src, out t, out err)) { Ui.Error(err); return; }
            if (db.Sessions.Count(s => s.Date == src) == 0 && chkNature.IsChecked != true) { Ui.Error("Aucune séance à copier : le jour source n'a pas de séance configurée."); return; }
            if (t.Count > 1 && !Ui.Confirm("Copier vers " + t.Count + " dates ?")) return;
            int copied = 0, skipped = 0;
            if (!Ui.Try(() => { copied = db.CopyDay(src, t, chkNature.IsChecked == true, chkOver.IsChecked == true, out skipped); })) return;
            ShowMessage("✔ " + copied + " séance(s) copiée(s)" + (skipped > 0 ? ", " + skipped + " ignorée(s) (déjà configurées ou hors année)." : "."), Sev.Ok);
        }
    }
}
