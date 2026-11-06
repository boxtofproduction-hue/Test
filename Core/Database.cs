using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GestionGym
{
    /// <summary>Base de données d'une année : ensemble de fichiers CSV dans un dossier.</summary>
    public class Database
    {
        public const int SchemaVersion = 1;

        public int Year;
        public string Folder = "";
        public bool ReadOnly;
        public bool Dirty;
        public int Version;
        public bool Archived;                       // base ouverte depuis le dossier Archive
        public readonly List<string> Activities = new List<string>();
        public readonly List<string> Slots = new List<string>();
        public readonly List<Pack> Packs = new List<Pack>();
        public readonly List<Client> Clients = new List<Client>();
        public readonly List<Purchase> Purchases = new List<Purchase>();
        public readonly List<Attendance> Attendances = new List<Attendance>();
        public readonly List<Session> Sessions = new List<Session>();
        public readonly List<DayInfo> Days = new List<DayInfo>();
        public readonly List<Charge> Charges = new List<Charge>();
        public readonly Dictionary<string, string> Settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public event Action Changed;
        FileStream lockStream;
        bool snapshotDone;
        Calc calc;
        int calcVersion = -1;

        // ---------------------------------------------------------------- paramètres
        string Get(string key, string def) { string v; return Settings.TryGetValue(key, out v) && v.Length > 0 ? v : def; }

        public int Capacity
        {
            get { int v; return Fmt.TryInt(Get("Capacite", "20"), out v) && v > 0 ? v : 20; }
            set { Settings["Capacite"] = value.ToString(); }
        }
        public string EventActivity
        {
            get { return Get("ActiviteEvent", "Atelier"); }
            set { Settings["ActiviteEvent"] = value; }
        }
        public bool NormalInEvent
        {
            get { return Pack.YesNo(Get("PackNormalAtelier", "Non")); }
            set { Settings["PackNormalAtelier"] = value ? "Oui" : "Non"; }
        }
        public int AlertDays
        {
            get { int v; return Fmt.TryInt(Get("AlerteJours", "30"), out v) && v >= 0 ? v : 30; }
            set { Settings["AlerteJours"] = value.ToString(); }
        }

        /// <summary>Date de référence des statuts : aujourd'hui pour l'année en cours, la fin d'année pour une année passée.</summary>
        public DateTime AsOf
        {
            get
            {
                DateTime t = DateTime.Today;
                if (t.Year == Year) return t;
                if (t.Year > Year) return new DateTime(Year, 12, 31);
                return new DateTime(Year, 1, 1);
            }
        }

        public Calc Calc
        {
            get
            {
                if (calc == null || calcVersion != Version) { calc = new Calc(this); calcVersion = Version; }
                return calc;
            }
        }

        int batchDepth;
        bool batchPending;

        public void Touch()
        {
            Dirty = true;
            Version++;
            if (batchDepth > 0) { batchPending = true; return; }
            if (Changed != null) Changed();
        }

        /// <summary>Regroupe de nombreuses modifications (import) : les écrans ne sont rafraîchis qu'à la fin.</summary>
        public void Batch(Action work)
        {
            batchDepth++;
            try { work(); }
            finally
            {
                batchDepth--;
                if (batchDepth == 0 && batchPending) { batchPending = false; if (Changed != null) Changed(); }
            }
        }

        /// <summary>Ajoute un client issu d'un import : l'âge peut rester inconnu (0).</summary>
        public Client ImportClient(Client c)
        {
            CheckWritable();
            if (string.IsNullOrWhiteSpace(c.LastName) && string.IsNullOrWhiteSpace(c.FirstName)) throw new InvalidOperationException("Client sans nom.");
            if (c.LastName.Length > 60 || c.FirstName.Length > 60) throw new InvalidOperationException("Nom ou prénom trop long (60 caractères maximum).");
            if (c.Age != 0 && (c.Age < 3 || c.Age > 110)) throw new InvalidOperationException("Âge hors limites (3 à 110) : " + c.Age);
            if (c.DistanceKm < 0 || c.DistanceKm > 1000) throw new InvalidOperationException("Distance hors limites (0 à 1000 km).");
            if (string.IsNullOrEmpty(c.Id) || Clients.Any(x => x.Id.Equals(c.Id, StringComparison.OrdinalIgnoreCase))) c.Id = NextClientId();
            Clients.Add(c);
            Touch();
            return c;
        }

        /// <summary>Notifie les écrans sans marquer la base comme modifiée.</summary>
        public void Refresh()
        {
            Version++;
            if (Changed != null) Changed();
        }

        void CheckWritable()
        {
            if (ReadOnly) throw new InvalidOperationException("Cette base est en lecture seule" + (Archived ? " (archive)" : "") + ". Utilise « Enregistrer sous » pour en faire une base modifiable.");
        }

        // ---------------------------------------------------------------- création
        public static Database CreateNew(int year, string folder)
        {
            var db = new Database();
            db.Year = year;
            db.Folder = folder;
            db.Activities.AddRange(new[] { "Musculation", "Cardio", "Yoga", "Zumba", "CrossFit", "Pilates", "Boxe", "Spinning", "Stretching", "Danse", "Atelier" });
            db.Slots.AddRange(new[] { "9h-10h", "10h-11h", "11h-12h", "12h15-13h15", "13h-14h", "14h-15h", "15h-16h", "16h30-17h30", "17h30-18h30", "18h30-19h30", "19h30-20h30", "20h30-21h30", "21h30-22h30" });
            db.Packs.AddRange(DefaultPacks());
            db.Capacity = 20;
            db.EventActivity = "Atelier";
            db.NormalInEvent = false;
            db.AlertDays = 30;
            db.Dirty = true;
            return db;
        }

        /// <summary>Base vide, sans valeurs par défaut (utilisée pour planifier une année).</summary>
        public static Database CreateBlank(int year, string folder)
        {
            var db = new Database();
            db.Year = year;
            db.Folder = folder;
            db.Dirty = true;
            return db;
        }

        public static List<Pack> DefaultPacks()
        {
            var l = new List<Pack>();
            l.Add(new Pack { Name = "Pack Découverte 1", Category = "Normal", Credits = 1, Per = "Total", ValidityMonths = 1, Engagement = "Aucun", EngagementMonths = 0, Payment = "Unique", Eligibility = Lists.OncePerClient, Renewal = false, Price = 15 });
            l.Add(new Pack { Name = "Pack Découverte 5", Category = "Normal", Credits = 5, Per = "Total", ValidityMonths = 2, Engagement = "Aucun", EngagementMonths = 0, Payment = "Unique", Eligibility = Lists.OncePerClient, Renewal = false, Price = 60 });
            l.Add(new Pack { Name = "Pack Séance Unique", Category = "Normal", Credits = 1, Per = "Total", ValidityMonths = 3, Engagement = "Aucun", EngagementMonths = 0, Payment = "Unique", Eligibility = "N.A", Renewal = false, Price = 20 });
            l.Add(new Pack { Name = "Pack Abonnement 2 séances", Category = "Normal", Credits = 2, Per = "Semaine", ValidityMonths = 12, Engagement = "Annuel", EngagementMonths = 12, Payment = "Mensuel", Eligibility = "N.A", Renewal = true, Price = 45 });
            l.Add(new Pack { Name = "Pack Abonnement 3 séances", Category = "Normal", Credits = 3, Per = "Semaine", ValidityMonths = 12, Engagement = "Annuel", EngagementMonths = 12, Payment = "Mensuel", Eligibility = "N.A", Renewal = true, Price = 59 });
            l.Add(new Pack { Name = "Achat Event", Category = "Event", Credits = 1, Per = "Total", ValidityMonths = 1, Engagement = "Aucun", EngagementMonths = 0, Payment = "Unique", Eligibility = "N.A", Renewal = false, Price = 25 });
            return l;
        }

        // ---------------------------------------------------------------- verrou (une seule instance par base)
        bool TryLock()
        {
            try
            {
                lockStream = new FileStream(Path.Combine(Folder, ".verrou"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return true;
            }
            catch { lockStream = null; return false; }
        }

        public void ReleaseLock()
        {
            if (lockStream == null) return;
            try { lockStream.Dispose(); } catch { }
            lockStream = null;
            try { File.Delete(Path.Combine(Folder, ".verrou")); } catch { }
        }

        /// <summary>Prend le verrou sur un nouveau dossier (après création ou "Enregistrer sous").</summary>
        public bool AcquireLock()
        {
            ReleaseLock();
            Directory.CreateDirectory(Folder);
            return TryLock();
        }

        // ---------------------------------------------------------------- ouverture
        public static bool LooksLikeBase(string folder)
        {
            return Directory.Exists(folder) && File.Exists(Path.Combine(folder, "meta.csv"));
        }

        public static int? ReadYear(string folder)
        {
            try
            {
                foreach (string[] r in Csv.Read(Path.Combine(folder, "meta.csv")))
                {
                    int y;
                    if (r.Length >= 2 && r[0].Trim().Equals("Annee", StringComparison.OrdinalIgnoreCase) && Fmt.TryInt(r[1], out y)) return y;
                }
            }
            catch { }
            return null;
        }

        public static Database Open(string folder, bool readOnly, bool archived, List<string> warnings)
        {
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Le dossier de la base est introuvable : " + folder);
            var db = new Database();
            db.Folder = folder;
            db.Archived = archived;

            int? y = ReadYear(folder);
            if (!y.HasValue)
            {
                warnings.Add("Le fichier meta.csv est absent ou illisible : l'année a été déduite du nom du dossier.");
                string digits = new string(Path.GetFileName(folder).Where(char.IsDigit).ToArray());
                int yy;
                if (digits.Length >= 4 && int.TryParse(digits.Substring(0, 4), out yy) && yy > 1990 && yy < 2200) y = yy;
                else throw new InvalidDataException("Impossible de déterminer l'année de cette base (fichier meta.csv manquant). Ce dossier n'est pas une base valide.");
            }
            db.Year = y.Value;

            foreach (string[] r in Csv.Read(Path.Combine(folder, "parametres.csv")))
                if (r.Length >= 2 && r[0].Trim().Length > 0 && !r[0].Trim().Equals("Cle", StringComparison.OrdinalIgnoreCase)) db.Settings[r[0].Trim()] = r[1].Trim();

            ReadList(Path.Combine(folder, "activites.csv"), db.Activities, warnings);
            ReadList(Path.Combine(folder, "creneaux.csv"), db.Slots, warnings);
            if (db.Activities.Count == 0) warnings.Add("Aucune activité définie : ajoute-les dans l'onglet Configuration.");
            if (db.Slots.Count == 0) warnings.Add("Aucun créneau défini : ajoute-les dans l'onglet Configuration.");

            LoadTable(folder, "packs.csv", warnings, (t, r, n) =>
            {
                Pack p; string e = Pack.FromCsv(t, r, out p);
                if (e != null) return e;
                if (db.Packs.Any(x => x.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase))) return "pack en double ignoré (« " + p.Name + " »)";
                db.Packs.Add(p); return null;
            });
            LoadTable(folder, "clients.csv", warnings, (t, r, n) =>
            {
                Client c; string e = Client.FromCsv(t, r, out c);
                if (e != null) return e;
                if (c.Id.Length == 0) c.Id = db.NextClientId();
                if (db.Clients.Any(x => x.Id.Equals(c.Id, StringComparison.OrdinalIgnoreCase))) return "identifiant client en double (" + c.Id + ")";
                db.Clients.Add(c); return null;
            });
            LoadTable(folder, "achats.csv", warnings, (t, r, n) =>
            {
                Purchase p; string e = Purchase.FromCsv(t, r, out p);
                if (e != null) return e;
                if (db.Purchases.Any(x => x.Id == p.Id)) p.Id = db.NextPurchaseId();
                db.Purchases.Add(p); return null;
            });
            LoadTable(folder, "presences.csv", warnings, (t, r, n) =>
            {
                Attendance a; string e = Attendance.FromCsv(t, r, out a);
                if (e != null) return e;
                db.Attendances.Add(a); return null;
            });
            // identifiants de présence en double : on les renumérote
            var seen = new HashSet<int>();
            foreach (Attendance a in db.Attendances)
                if (!seen.Add(a.Id)) { a.Id = db.NextAttendanceId(); seen.Add(a.Id); }

            LoadTable(folder, "seances.csv", warnings, (t, r, n) =>
            {
                DateTime d;
                if (!Fmt.TryDate(t.Get(r, "Date"), out d)) return "date de séance invalide";
                string slot = Fmt.Clean(t.Get(r, "Creneau")), act = Fmt.Clean(t.Get(r, "Activite"));
                if (slot.Length == 0 || act.Length == 0) return "séance sans créneau ou sans activité";
                db.Sessions.Add(new Session { Date = d, Slot = slot, Activity = act }); return null;
            });
            LoadTable(folder, "jours.csv", warnings, (t, r, n) =>
            {
                DateTime d;
                if (!Fmt.TryDate(t.Get(r, "Date"), out d)) return "date invalide";
                db.Days.Add(new DayInfo { Date = d, Nature = Pack.Match(Lists.Natures, t.Get(r, "Nature"), "Normal") }); return null;
            });

            LoadTable(folder, "charges.csv", warnings, (t, r, n) =>
            {
                Charge ch; string e = Charge.FromCsv(t, r, out ch);
                if (e != null) return e;
                db.Charges.Add(ch); return null;
            });

            // cohérence : références orphelines (conservées, signalées)
            int orphP = db.Purchases.Count(p => db.Clients.All(c => c.Id != p.ClientId) || db.Packs.All(k => !k.Name.Equals(p.PackName, StringComparison.OrdinalIgnoreCase)));
            if (orphP > 0) warnings.Add(orphP + " achat(s) font référence à un client ou à un pack inexistant (ils sont signalés dans la colonne Contrôle).");
            int orphA = db.Attendances.Count(a => db.Clients.All(c => c.Id != a.ClientId));
            if (orphA > 0) warnings.Add(orphA + " présence(s) font référence à un client inexistant (signalées dans la colonne Contrôle).");

            db.ReadOnly = readOnly;
            if (!readOnly)
            {
                if (!db.TryLock())
                {
                    db.ReadOnly = true;
                    warnings.Add("Cette base est déjà ouverte dans une autre fenêtre de l'application : elle est ouverte en LECTURE SEULE pour éviter les conflits.");
                }
            }
            db.Dirty = false;
            return db;
        }

        static void ReadList(string path, List<string> target, List<string> warnings)
        {
            bool first = true;
            foreach (string[] r in Csv.Read(path))
            {
                string v = Fmt.Clean(r.Length > 0 ? r[0] : "");
                if (first) { first = false; if (v.Equals("Nom", StringComparison.OrdinalIgnoreCase)) continue; }
                if (v.Length == 0) continue;
                if (target.Any(x => x.Equals(v, StringComparison.OrdinalIgnoreCase))) { warnings.Add("Doublon ignoré dans " + Path.GetFileName(path) + " : " + v); continue; }
                target.Add(v);
            }
        }

        static void LoadTable(string folder, string file, List<string> warnings, Func<CsvTable, string[], int, string> handler)
        {
            string path = Path.Combine(folder, file);
            if (!File.Exists(path)) return;
            CsvTable t;
            try { t = new CsvTable(Csv.Read(path)); }
            catch (Exception ex) { warnings.Add("Fichier " + file + " illisible : " + ex.Message); return; }
            int bad = 0;
            for (int i = 0; i < t.Rows.Count; i++)
            {
                string msg;
                try { msg = handler(t, t.Rows[i], i); }
                catch (Exception ex) { msg = ex.Message; }
                if (msg != null)
                {
                    bad++;
                    if (bad <= 5) warnings.Add(file + " ligne " + (i + 2) + " ignorée : " + msg);
                }
            }
            if (bad > 5) warnings.Add(file + " : " + (bad - 5) + " autre(s) ligne(s) ignorée(s).");
        }

        // ---------------------------------------------------------------- enregistrement
        public void Save()
        {
            CheckWritable();
            Directory.CreateDirectory(Folder);
            if (!snapshotDone)
            {
                Snapshot("avant_modification");
                snapshotDone = true;
            }
            WriteAll(Folder);
            Dirty = false;
        }

        /// <summary>Écrit une copie de la base dans un autre dossier (sans changer la base ouverte).</summary>
        public void SaveCopy(string folder)
        {
            Directory.CreateDirectory(folder);
            WriteAll(folder);
        }

        void WriteAll(string folder)
        {
            Csv.Write(Path.Combine(folder, "parametres.csv"), SettingsRows());
            Csv.Write(Path.Combine(folder, "activites.csv"), new[] { new[] { "Nom" } }.Concat(Activities.Select(a => new[] { a })));
            Csv.Write(Path.Combine(folder, "creneaux.csv"), new[] { new[] { "Nom" } }.Concat(Slots.Select(a => new[] { a })));
            Csv.Write(Path.Combine(folder, "packs.csv"), new[] { Pack.Header }.Concat(Packs.Select(p => p.ToCsv())));
            Csv.Write(Path.Combine(folder, "clients.csv"), new[] { Client.Header }.Concat(Clients.Select(c => c.ToCsv())));
            Csv.Write(Path.Combine(folder, "achats.csv"), new[] { Purchase.Header }.Concat(Purchases.OrderBy(p => p.Id).Select(p => p.ToCsv())));
            Csv.Write(Path.Combine(folder, "presences.csv"), new[] { Attendance.Header }.Concat(Attendances.OrderBy(a => a.Date).ThenBy(a => a.Id).Select(a => a.ToCsv())));
            Csv.Write(Path.Combine(folder, "seances.csv"), new[] { new[] { "Date", "Creneau", "Activite" } }.Concat(Sessions.OrderBy(s => s.Date).ThenBy(s => Slots.IndexOf(s.Slot)).Select(s => new[] { Fmt.Date(s.Date), s.Slot, s.Activity })));
            Csv.Write(Path.Combine(folder, "jours.csv"), new[] { new[] { "Date", "Nature" } }.Concat(Days.Where(d => d.Nature != "Normal").OrderBy(d => d.Date).Select(d => new[] { Fmt.Date(d.Date), d.Nature })));
            Csv.Write(Path.Combine(folder, "charges.csv"), new[] { Charge.Header }.Concat(Charges.Select(c => c.ToCsv())));
            var meta = new List<string[]>();
            meta.Add(new[] { "Cle", "Valeur" });
            meta.Add(new[] { "Application", "GestionGym" });
            meta.Add(new[] { "Schema", SchemaVersion.ToString() });
            meta.Add(new[] { "Annee", Year.ToString() });
            meta.Add(new[] { "Enregistre", DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss") });
            Csv.Write(Path.Combine(folder, "meta.csv"), meta);
        }

        IEnumerable<string[]> SettingsRows()
        {
            yield return new[] { "Cle", "Valeur" };
            foreach (var kv in Settings.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)) yield return new[] { kv.Key, kv.Value };
        }

        public void Snapshot(string tag)
        {
            try
            {
                if (!Directory.Exists(Folder)) return;
                string[] files = Directory.GetFiles(Folder, "*.csv");
                if (files.Length == 0) return;
                string root = Path.Combine(Folder, "_sauvegardes");
                string dir = Path.Combine(root, tag + "_" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(dir);
                foreach (string f in files) File.Copy(f, Path.Combine(dir, Path.GetFileName(f)), true);
                var all = new DirectoryInfo(root).GetDirectories().OrderByDescending(d => d.Name).ToList();
                for (int i = 10; i < all.Count; i++)
                    try { all[i].Delete(true); } catch { }
            }
            catch { /* une sauvegarde qui échoue ne doit pas empêcher l'enregistrement */ }
        }

        // ---------------------------------------------------------------- identifiants
        public string NextClientId()
        {
            int max = 0;
            foreach (Client c in Clients)
            {
                int n;
                if (c.Id.Length > 1 && (c.Id[0] == 'C' || c.Id[0] == 'c') && int.TryParse(c.Id.Substring(1), out n) && n > max) max = n;
            }
            return "C" + (max + 1).ToString("000");
        }
        public int NextPurchaseId() { return Purchases.Count == 0 ? 1 : Purchases.Max(p => p.Id) + 1; }
        public int NextAttendanceId() { return Attendances.Count == 0 ? 1 : Attendances.Max(a => a.Id) + 1; }

        public Client FindClient(string id) { return Clients.FirstOrDefault(c => c.Id == id); }
        public Pack FindPack(string name) { return Packs.FirstOrDefault(p => p.Name.Equals(name ?? "", StringComparison.OrdinalIgnoreCase)); }
        public Purchase FindPurchase(int id) { return Purchases.FirstOrDefault(p => p.Id == id); }

        public string SessionActivity(DateTime d, string slot)
        {
            Session s = Sessions.FirstOrDefault(x => x.Date == d.Date && x.Slot == slot);
            return s == null ? "" : s.Activity;
        }

        public string DayNature(DateTime d)
        {
            DayInfo i = Days.FirstOrDefault(x => x.Date == d.Date);
            return i == null ? "Normal" : i.Nature;
        }

        // ---------------------------------------------------------------- validations
        public string ValidateClient(Client c)
        {
            if (string.IsNullOrWhiteSpace(c.LastName) || string.IsNullOrWhiteSpace(c.FirstName)) return "Le nom et le prénom sont obligatoires.";
            if (c.LastName.Length > 60 || c.FirstName.Length > 60) return "Le nom et le prénom sont limités à 60 caractères.";
            if (c.Age < 3 || c.Age > 110) return "L'âge doit être compris entre 3 et 110 ans.";
            if (c.DistanceKm < 0 || c.DistanceKm > 1000) return "La distance doit être comprise entre 0 et 1000 km.";
            return null;
        }

        public Client FindDuplicate(string last, string first, string exceptId)
        {
            string l = Fmt.Norm(last), f = Fmt.Norm(first);
            return Clients.FirstOrDefault(c => c.Id != exceptId && Fmt.Norm(c.LastName) == l && Fmt.Norm(c.FirstName) == f);
        }

        public string ValidatePack(Pack p, string oldName)
        {
            if (string.IsNullOrWhiteSpace(p.Name)) return "Le nom du pack est obligatoire.";
            if (p.Name.Length > 60) return "Le nom du pack est limité à 60 caractères.";
            if (Packs.Any(x => x.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase) && !x.Name.Equals(oldName ?? "", StringComparison.OrdinalIgnoreCase))) return "Un pack porte déjà ce nom.";
            if (p.Credits < 1 || p.Credits > 1000) return "Les crédits doivent être un nombre entier de séances entre 1 et 1000.";
            if (p.ValidityMonths < 1 || p.ValidityMonths > 12) return "La validité doit être comprise entre 1 et 12 mois.";
            if (p.EngagementMonths < 0 || p.EngagementMonths > 60) return "La durée d'engagement doit être comprise entre 0 et 60 mois.";
            if (p.Engagement != "Aucun" && p.EngagementMonths == 0) return "Un engagement mensuel ou annuel doit avoir une durée en mois.";
            if (p.Engagement == "Aucun" && p.EngagementMonths > 0) return "Une durée d'engagement est renseignée alors que l'engagement est « Aucun ».";
            if (p.Per == "Jour" && p.Credits > 24) return "Plus de 24 séances par jour : vérifie le nombre de crédits.";
            if (p.Price < 0 || p.Price > 100000) return "Le prix doit être compris entre 0 et 100 000 €.";
            if (p.Category == "Event" && !Activities.Any(a => a.Equals(EventActivity, StringComparison.OrdinalIgnoreCase))) return "L'activité Event « " + EventActivity + " » n'existe pas dans la liste des activités (onglet Configuration).";
            return null;
        }

        public List<string> PackWarnings(Pack p)
        {
            var w = new List<string>();
            if (p.StepMonths > 0 && p.EngagementMonths == 0 && !p.Renewal) w.Add("Paiement récurrent sans engagement ni reconduction : les paiements continueront jusqu'à la date de fin saisie sur chaque achat.");
            if (p.Payment == "Unique" && p.Engagement != "Aucun") w.Add("Engagement sans paiement échelonné : le prix est encaissé en une seule fois.");
            if (p.Price == 0) w.Add("Le prix est à 0 €.");
            if (!p.IsPooled && p.Payment == "Unique" && p.ValidityMonths > 0 && p.EngagementMonths == 0) w.Add("Crédits périodiques et paiement unique sans engagement : prévois une date de fin sur chaque achat.");
            return w;
        }

        public string ValidatePurchase(Purchase p, bool isNew)
        {
            Client c = FindClient(p.ClientId);
            if (c == null) return "Client inconnu.";
            Pack k = FindPack(p.PackName);
            if (k == null) return "Pack inconnu.";
            if (isNew && !k.Active) return "Ce pack est désactivé dans le catalogue.";
            if (p.End.HasValue && p.End.Value < p.Start) return "La date de fin ne peut pas précéder la date de début.";
            if (k.OncePerClient && Purchases.Any(x => x.Id != p.Id && x.ClientId == p.ClientId && x.PackName.Equals(p.PackName, StringComparison.OrdinalIgnoreCase)))
                return "Ce pack est limité à un achat par client : " + c.Label + " l'a déjà acheté.";
            if (p.Start.Year < 1990 || p.Start.Year > 2200) return "Date de début invalide.";
            return null;
        }

        public List<string> PurchaseWarnings(Purchase p)
        {
            var w = new List<string>();
            Pack k = FindPack(p.PackName);
            if (k == null) return w;
            if (p.Start.Year != Year) w.Add("La date d'achat n'est pas dans l'année de la base (" + Year + ").");
            if (k.Kind == "Abonnement")
            {
                Calc cc = Calc;
                foreach (Purchase o in Purchases)
                {
                    if (o.Id == p.Id || o.ClientId != p.ClientId) continue;
                    Pack ok = FindPack(o.PackName);
                    if (ok == null || ok.Kind != "Abonnement") continue;
                    DateTime? end = cc.EffectiveEnd(o);
                    if (o.Start <= p.Start && (!end.HasValue || end.Value >= p.Start))
                    { w.Add("Ce client a déjà un abonnement en cours (« " + o.PackName + " » depuis le " + Fmt.Date(o.Start) + "). Pour changer d'abonnement, utilise « Changer de pack »."); break; }
                }
            }
            return w;
        }

        // ---------------------------------------------------------------- opérations (toutes déclenchent Touch)

        // ---------------------------------------------------------------- charges fixes
        public string ValidateCharge(Charge c)
        {
            if (string.IsNullOrWhiteSpace(c.Label)) return "Saisis un libellé pour la charge.";
            if (c.Amount < 0) return "Le montant doit être positif.";
            if (c.Amount > 100000000m) return "Montant trop élevé : vérifie la saisie.";
            if (c.Start.Year < 1990 || c.Start.Year > 2200) return "Date de début invalide.";
            if (c.End.HasValue && c.End.Value < c.Start) return "La date de fin est antérieure à la date de début.";
            return null;
        }

        public void AddCharge(Charge c)
        {
            CheckWritable();
            string e = ValidateCharge(c);
            if (e != null) throw new InvalidOperationException(e);
            Charges.Add(c);
            Touch();
        }

        public void UpdateCharge(int index, Charge c)
        {
            CheckWritable();
            if (index < 0 || index >= Charges.Count) throw new InvalidOperationException("Charge introuvable.");
            string e = ValidateCharge(c);
            if (e != null) throw new InvalidOperationException(e);
            Charges[index] = c;
            Touch();
        }

        public void DeleteCharge(int index)
        {
            CheckWritable();
            if (index < 0 || index >= Charges.Count) throw new InvalidOperationException("Charge introuvable.");
            Charges.RemoveAt(index);
            Touch();
        }

        /// <summary>Enregistre des paramètres fiscaux ou de rentabilité (valeurs déjà validées).</summary>
        public void SetSettings(Dictionary<string, string> values)
        {
            CheckWritable();
            foreach (var kv in values) Settings[kv.Key] = kv.Value;
            Touch();
        }

        public void AddClient(Client c)
        {
            CheckWritable();
            string e = ValidateClient(c);
            if (e != null) throw new InvalidOperationException(e);
            if (string.IsNullOrEmpty(c.Id)) c.Id = NextClientId();
            Clients.Add(c);
            Touch();
        }

        public void UpdateClient(Client c)
        {
            CheckWritable();
            string e = ValidateClient(c);
            if (e != null) throw new InvalidOperationException(e);
            int i = Clients.FindIndex(x => x.Id == c.Id);
            if (i < 0) throw new InvalidOperationException("Client introuvable.");
            Clients[i] = c;
            Touch();
        }

        public bool ClientHasData(string id)
        {
            return Purchases.Any(p => p.ClientId == id) || Attendances.Any(a => a.ClientId == id);
        }

        public void DeleteClient(string id)
        {
            CheckWritable();
            if (ClientHasData(id)) throw new InvalidOperationException("Ce client a des achats ou des présences : il ne peut pas être supprimé. Supprime d'abord ses présences et ses achats.");
            Clients.RemoveAll(c => c.Id == id);
            Touch();
        }

        public void AddPack(Pack p)
        {
            CheckWritable();
            string e = ValidatePack(p, null);
            if (e != null) throw new InvalidOperationException(e);
            Packs.Add(p);
            Touch();
        }

        public void UpdatePack(string oldName, Pack p)
        {
            CheckWritable();
            string e = ValidatePack(p, oldName);
            if (e != null) throw new InvalidOperationException(e);
            int i = Packs.FindIndex(x => x.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase));
            if (i < 0) throw new InvalidOperationException("Pack introuvable.");
            Packs[i] = p;
            if (!oldName.Equals(p.Name, StringComparison.Ordinal))
                foreach (Purchase pu in Purchases) if (pu.PackName.Equals(oldName, StringComparison.OrdinalIgnoreCase)) pu.PackName = p.Name;
            Touch();
        }

        public int SalesCount(string packName)
        {
            return Purchases.Count(p => p.PackName.Equals(packName, StringComparison.OrdinalIgnoreCase));
        }

        public void DeletePack(string name)
        {
            CheckWritable();
            int n = SalesCount(name);
            if (n > 0) throw new InvalidOperationException("Ce pack a " + n + " vente(s) : il ne peut pas être supprimé. Passe-le en « Actif = Non » pour ne plus le proposer.");
            Packs.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            Touch();
        }

        public Purchase AddPurchase(Purchase p)
        {
            CheckWritable();
            string e = ValidatePurchase(p, true);
            if (e != null) throw new InvalidOperationException(e);
            p.Id = NextPurchaseId();
            Purchases.Add(p);
            Touch();
            return p;
        }

        public void UpdatePurchase(Purchase p)
        {
            CheckWritable();
            string e = ValidatePurchase(p, false);
            if (e != null) throw new InvalidOperationException(e);
            int i = Purchases.FindIndex(x => x.Id == p.Id);
            if (i < 0) throw new InvalidOperationException("Achat introuvable.");
            Purchases[i] = p;
            Touch();
        }

        public int AttendanceCountOfPurchase(int purchaseId) { return Attendances.Count(a => a.PurchaseId == purchaseId); }

        public void DeletePurchase(int id)
        {
            CheckWritable();
            foreach (Attendance a in Attendances) if (a.PurchaseId == id) a.PurchaseId = 0;
            Purchases.RemoveAll(p => p.Id == id);
            Touch();
        }

        public void StopPurchase(int id, DateTime lastDay)
        {
            CheckWritable();
            Purchase p = FindPurchase(id);
            if (p == null) throw new InvalidOperationException("Achat introuvable.");
            if (lastDay < p.Start) throw new InvalidOperationException("La date ne peut pas précéder le début (" + Fmt.Date(p.Start) + ").");
            p.End = lastDay;
            p.EndReason = "Arrêt";
            Touch();
        }

        public void ChangePack(int oldId, string newPack, DateTime date)
        {
            CheckWritable();
            Purchase old = FindPurchase(oldId);
            if (old == null) throw new InvalidOperationException("Achat introuvable.");
            if (date <= old.Start) throw new InvalidOperationException("La date doit être postérieure au début de l'achat (" + Fmt.Date(old.Start) + ").");
            if (old.End.HasValue && old.End.Value < date.AddDays(-1)) throw new InvalidOperationException("Cet achat est déjà terminé.");
            var np = new Purchase { ClientId = old.ClientId, PackName = newPack, Start = date };
            string e = ValidatePurchase(np, true);
            if (e != null) throw new InvalidOperationException(e);
            old.End = date.AddDays(-1);
            old.EndReason = "Changement";
            np.Id = NextPurchaseId();
            Purchases.Add(np);
            Touch();
        }

        public Attendance AddAttendance(Attendance a)
        {
            CheckWritable();
            Client c = FindClient(a.ClientId);
            if (c == null) throw new InvalidOperationException("Client inconnu.");
            if (a.Date.Year != Year) throw new InvalidOperationException("La date n'est pas dans l'année de la base (" + Year + ").");
            if (!Slots.Contains(a.Slot)) throw new InvalidOperationException("Créneau inconnu.");
            if (!Activities.Contains(a.Activity)) throw new InvalidOperationException("Activité inconnue.");
            if (Attendances.Any(x => x.ClientId == a.ClientId && x.Date == a.Date.Date && x.Slot == a.Slot)) throw new InvalidOperationException("Ce client est déjà marqué présent à ce créneau.");
            a.Date = a.Date.Date;
            a.Id = NextAttendanceId();
            Attendances.Add(a);
            Touch();
            return a;
        }

        public void DeleteAttendance(int id)
        {
            CheckWritable();
            Attendances.RemoveAll(a => a.Id == id);
            Touch();
        }

        public void SetSession(DateTime d, string slot, string activity)
        {
            CheckWritable();
            if (!Slots.Contains(slot)) throw new InvalidOperationException("Créneau inconnu.");
            if (d.Year != Year) throw new InvalidOperationException("La date n'est pas dans l'année de la base (" + Year + ").");
            Sessions.RemoveAll(s => s.Date == d.Date && s.Slot == slot);
            if (!string.IsNullOrEmpty(activity))
            {
                if (!Activities.Contains(activity)) throw new InvalidOperationException("Activité inconnue.");
                Sessions.Add(new Session { Date = d.Date, Slot = slot, Activity = activity });
            }
            Touch();
        }

        /// <summary>Copie les séances (et éventuellement la nature) d'un jour vers d'autres dates. Retourne le nombre de séances copiées.</summary>
        public int CopyDay(DateTime src, List<DateTime> targets, bool copyNature, bool overwrite, out int skipped)
        {
            CheckWritable();
            skipped = 0;
            var source = Sessions.Where(s => s.Date == src.Date).ToList();
            string nature = DayNature(src);
            int copied = 0;
            foreach (DateTime t in targets)
            {
                if (t.Year != Year || t.Date == src.Date) { skipped += source.Count; continue; }
                foreach (Session s in source)
                {
                    Session ex = Sessions.FirstOrDefault(x => x.Date == t.Date && x.Slot == s.Slot);
                    if (ex != null)
                    {
                        if (!overwrite) { skipped++; continue; }
                        ex.Activity = s.Activity;
                    }
                    else Sessions.Add(new Session { Date = t.Date, Slot = s.Slot, Activity = s.Activity });
                    copied++;
                }
                if (copyNature)
                {
                    Days.RemoveAll(x => x.Date == t.Date);
                    if (nature != "Normal") Days.Add(new DayInfo { Date = t.Date, Nature = nature });
                }
            }
            Touch();
            return copied;
        }

        public void SetDayNature(DateTime d, string nature)
        {
            CheckWritable();
            Days.RemoveAll(x => x.Date == d.Date);
            if (nature != "Normal") Days.Add(new DayInfo { Date = d.Date, Nature = nature });
            Touch();
        }

        // ---------------------------------------------------------------- activités et créneaux
        public void AddActivity(string name)
        {
            CheckWritable();
            name = Fmt.Clean(name);
            if (name.Length == 0) throw new InvalidOperationException("Le nom est vide.");
            if (name.Length > 40) throw new InvalidOperationException("Nom trop long (40 caractères maximum).");
            if (Activities.Count >= 12) throw new InvalidOperationException("12 activités maximum.");
            if (Activities.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Cette activité existe déjà.");
            Activities.Add(name);
            Touch();
        }

        public void RenameActivity(string old, string name)
        {
            CheckWritable();
            name = Fmt.Clean(name);
            if (name.Length == 0) throw new InvalidOperationException("Le nom est vide.");
            if (name.Length > 40) throw new InvalidOperationException("Nom trop long (40 caractères maximum).");
            if (!old.Equals(name, StringComparison.OrdinalIgnoreCase) && Activities.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Cette activité existe déjà.");
            int i = Activities.IndexOf(old);
            if (i < 0) throw new InvalidOperationException("Activité introuvable.");
            Activities[i] = name;
            foreach (Session s in Sessions) if (s.Activity == old) s.Activity = name;
            foreach (Attendance a in Attendances) if (a.Activity == old) a.Activity = name;
            if (EventActivity == old) EventActivity = name;
            Touch();
        }

        public void DeleteActivity(string name)
        {
            CheckWritable();
            if (EventActivity == name) throw new InvalidOperationException("Cette activité est l'activité « Event » des packs Event. Change d'abord l'activité Event.");
            int n = Sessions.Count(s => s.Activity == name) + Attendances.Count(a => a.Activity == name);
            if (n > 0) throw new InvalidOperationException("Cette activité est utilisée par " + n + " séance(s) ou présence(s) : elle ne peut pas être supprimée.");
            Activities.Remove(name);
            Touch();
        }

        public void AddSlot(string name)
        {
            CheckWritable();
            name = Fmt.Clean(name);
            if (name.Length == 0) throw new InvalidOperationException("Le nom est vide.");
            if (name.Length > 20) throw new InvalidOperationException("Nom trop long (20 caractères maximum).");
            if (Slots.Count >= 20) throw new InvalidOperationException("20 créneaux maximum.");
            if (Slots.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Ce créneau existe déjà.");
            Slots.Add(name);
            Touch();
        }

        public void RenameSlot(string old, string name)
        {
            CheckWritable();
            name = Fmt.Clean(name);
            if (name.Length == 0) throw new InvalidOperationException("Le nom est vide.");
            if (name.Length > 20) throw new InvalidOperationException("Nom trop long (20 caractères maximum).");
            if (!old.Equals(name, StringComparison.OrdinalIgnoreCase) && Slots.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Ce créneau existe déjà.");
            int i = Slots.IndexOf(old);
            if (i < 0) throw new InvalidOperationException("Créneau introuvable.");
            Slots[i] = name;
            foreach (Session s in Sessions) if (s.Slot == old) s.Slot = name;
            foreach (Attendance a in Attendances) if (a.Slot == old) a.Slot = name;
            Touch();
        }

        public void DeleteSlot(string name)
        {
            CheckWritable();
            int n = Sessions.Count(s => s.Slot == name) + Attendances.Count(a => a.Slot == name);
            if (n > 0) throw new InvalidOperationException("Ce créneau est utilisé par " + n + " séance(s) ou présence(s) : il ne peut pas être supprimé.");
            Slots.Remove(name);
            Touch();
        }

        public void MoveSlot(string name, int delta)
        {
            CheckWritable();
            int i = Slots.IndexOf(name), j = i + delta;
            if (i < 0 || j < 0 || j >= Slots.Count) return;
            string t = Slots[i]; Slots[i] = Slots[j]; Slots[j] = t;
            Touch();
        }
    }
}
